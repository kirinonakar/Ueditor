using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Ueditor.Core.Interfaces;

namespace Ueditor.Core.Services
{
    public class FileService : IFileService
    {
        public async Task<string> ReadTextFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("파일을 찾을 수 없습니다.", filePath);

            // Detect encoding
            Encoding encoding = DetectEncoding(filePath);

            using (var reader = new StreamReader(filePath, encoding, detectEncodingFromByteOrderMarks: true))
            {
                return await reader.ReadToEndAsync();
            }
        }

        public async Task SaveTextFileAsync(string filePath, string content)
        {
            // Fail-safe writing: Write to temporary file first, then atomically replace
            string? directory = Path.GetDirectoryName(filePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempFilePath = Path.Combine(directory ?? Path.GetTempPath(), $"._{Path.GetFileName(filePath)}.tmp");
            string backupFilePath = filePath + ".bak";

            try
            {
                // 1. Write content to temp file with UTF-8 encoding (with BOM for Windows notepad compatibility)
                await File.WriteAllTextAsync(tempFilePath, content, Encoding.UTF8);

                // 2. Perform atomic replace
                if (File.Exists(filePath))
                {
                    // Create backup and replace
                    File.Replace(tempFilePath, filePath, backupFilePath);
                    
                    // Cleanup backup if everything went flawlessly
                    if (File.Exists(backupFilePath))
                    {
                        File.Delete(backupFilePath);
                    }
                }
                else
                {
                    // Direct move if new file
                    File.Move(tempFilePath, filePath);
                }
            }
            catch (Exception ex)
            {
                // Safe recover
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
                throw new IOException($"파일 저장 실패 (안전 복구 완료): {ex.Message}", ex);
            }
        }

        public Task<LargeFileInfo> GetLargeFileInfoAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("파일을 찾을 수 없습니다.", filePath);

            var info = new FileInfo(filePath);
            return Task.FromResult(new LargeFileInfo
            {
                FilePath = filePath,
                FileSize = info.Length
            });
        }

        public async Task<string> ReadChunkAsync(string filePath, long offset, int length)
        {
            // Placeholder chunk read for Phase 2
            if (!File.Exists(filePath))
                return string.Empty;

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (offset >= stream.Length)
                    return string.Empty;

                stream.Seek(offset, SeekOrigin.Begin);
                byte[] buffer = new byte[Math.Min(length, stream.Length - offset)];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                
                // Return UTF-8 string representation
                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
        }

        /// <summary>
        /// Simple heuristic to detect file encoding
        /// </summary>
        private Encoding DetectEncoding(string filePath)
        {
            byte[] bom = new byte[4];
            using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                _ = file.Read(bom, 0, 4);
            }

            // Analyze BOM
            if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) return Encoding.UTF8;
            if (bom[0] == 0xff && bom[1] == 0xfe && bom[2] == 0 && bom[3] == 0) return Encoding.UTF32; // UTF-32 LE
            if (bom[0] == 0xff && bom[1] == 0xfe) return Encoding.Unicode; // UTF-16 LE
            if (bom[0] == 0xfe && bom[1] == 0xff) return Encoding.BigEndianUnicode; // UTF-16 BE

            // Fallback to UTF-8 without BOM or local ANSI
            return Encoding.UTF8;
        }
    }
}
