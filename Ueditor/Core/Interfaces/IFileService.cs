using System.Threading.Tasks;

namespace Ueditor.Core.Interfaces
{
    public interface IFileService
    {
        // 일반 파일 읽기 (인코딩 자동 감지)
        Task<string> ReadTextFileAsync(string filePath);
        
        // 안전한 파일 쓰기 (임시 파일 쓰기 후 덮어쓰기 기법 적용)
        Task SaveTextFileAsync(string filePath, string content);

        // 대용량 파일 정보 추출
        Task<LargeFileInfo> GetLargeFileInfoAsync(string filePath);

        // 대용량 파일용 Chunk 로더
        Task<string> ReadChunkAsync(string filePath, long offset, int length);
    }

    public class LargeFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; } // Bytes
        public bool IsLargeFile => FileSize >= 50 * 1024 * 1024; // 50MB 이상
        public bool IsUltraLargeFile => FileSize >= 200 * 1024 * 1024; // 200MB 이상
    }
}
