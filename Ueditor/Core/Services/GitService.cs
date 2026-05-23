using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Ueditor.Core.Interfaces;

namespace Ueditor.Core.Services
{
    public class GitService : IGitService
    {
        public async Task<string> GetCurrentBranchAsync(string repoPath)
        {
            if (string.IsNullOrEmpty(repoPath) || !Directory.Exists(repoPath))
                return "Git: 감지 안됨";

            try
            {
                // Check if directory is a git repo first
                string gitDir = Path.Combine(repoPath, ".git");
                if (!Directory.Exists(gitDir) && !File.Exists(gitDir)) // Submodules or worktrees might have .git as file
                {
                    // Check parent directories as well
                    var parent = Directory.GetParent(repoPath);
                    if (parent != null)
                    {
                        return await GetCurrentBranchAsync(parent.FullName);
                    }
                    return "Git: 감지 안됨";
                }

                string output = await RunGitCommandAsync(repoPath, "rev-parse --abbrev-ref HEAD");
                if (string.IsNullOrEmpty(output) || output.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase))
                {
                    return "Git: 감지 안됨";
                }

                return $"Git: {output.Trim()}";
            }
            catch
            {
                return "Git: 감지 안됨";
            }
        }

        public async Task<Dictionary<string, string>> GetFileStatusesAsync(string repoPath)
        {
            var statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(repoPath) || !Directory.Exists(repoPath))
                return statuses;

            try
            {
                string output = await RunGitCommandAsync(repoPath, "status --porcelain");
                if (string.IsNullOrEmpty(output) || output.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase))
                    return statuses;

                string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    if (line.Length >= 4)
                    {
                        string status = line.Substring(0, 2);
                        string relativePath = line.Substring(3).Trim().Replace('/', '\\');
                        
                        // git status --porcelain can wrap paths in quotes if they contain special characters
                        if (relativePath.StartsWith("\"") && relativePath.EndsWith("\""))
                        {
                            relativePath = relativePath.Substring(1, relativePath.Length - 2);
                        }

                        string fullPath = Path.GetFullPath(Path.Combine(repoPath, relativePath));
                        statuses[fullPath] = status;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get git status: {ex.Message}");
            }

            return statuses;
        }

        private async Task<string> RunGitCommandAsync(string workingDir, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                try
                {
                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    
                    // We must wait for exit to ensure cleanup
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode != 0)
                    {
                        return $"fatal: {error}";
                    }

                    return output;
                }
                catch (Exception ex)
                {
                    return $"fatal: {ex.Message}";
                }
            }
        }

        public async Task<string> GetFileDiffAsync(string repoPath, string filePath)
        {
            if (string.IsNullOrEmpty(repoPath) || string.IsNullOrEmpty(filePath))
                return string.Empty;

            try
            {
                // Make path relative to repoPath to satisfy git cli arguments
                string relativePath = Path.GetRelativePath(repoPath, filePath);

                // Run diff. First check unstaged diff, then cached (staged) diff.
                string unstagedDiff = await RunGitCommandAsync(repoPath, $"diff -- \"{relativePath}\"");
                string stagedDiff = await RunGitCommandAsync(repoPath, $"diff --cached -- \"{relativePath}\"");

                // If untracked file, diff might be empty, so let's show the whole file content as addition
                if (string.IsNullOrEmpty(unstagedDiff) && string.IsNullOrEmpty(stagedDiff))
                {
                    // Check if file is untracked
                    string status = await RunGitCommandAsync(repoPath, $"status --porcelain -- \"{relativePath}\"");
                    if (status.StartsWith("?") || status.Trim().Length > 0)
                    {
                        if (File.Exists(filePath))
                        {
                            var lines = await File.ReadAllLinesAsync(filePath);
                            var sb = new StringBuilder();
                            sb.AppendLine($"--- /dev/null");
                            sb.AppendLine($"+++ b/{relativePath}");
                            sb.AppendLine($"@@ -0,0 +1,{lines.Length} @@");
                            foreach (var line in lines)
                            {
                                sb.AppendLine($"+{line}");
                            }
                            return sb.ToString();
                        }
                    }
                    return "변경 내역이 없거나 감지되지 않았습니다.";
                }

                var fullDiff = new StringBuilder();
                if (!string.IsNullOrEmpty(stagedDiff) && !stagedDiff.StartsWith("fatal:"))
                {
                    fullDiff.AppendLine("=== Staged Changes ===");
                    fullDiff.AppendLine(stagedDiff);
                }
                if (!string.IsNullOrEmpty(unstagedDiff) && !unstagedDiff.StartsWith("fatal:"))
                {
                    if (fullDiff.Length > 0) fullDiff.AppendLine();
                    fullDiff.AppendLine("=== Unstaged Changes ===");
                    fullDiff.AppendLine(unstagedDiff);
                }

                return fullDiff.ToString();
            }
            catch (Exception ex)
            {
                return $"fatal: {ex.Message}";
            }
        }

        public async Task<bool> StageFileAsync(string repoPath, string filePath)
        {
            if (string.IsNullOrEmpty(repoPath) || string.IsNullOrEmpty(filePath))
                return false;

            string relativePath = Path.GetRelativePath(repoPath, filePath);
            string output = await RunGitCommandAsync(repoPath, $"add -- \"{relativePath}\"");
            return !output.StartsWith("fatal:");
        }

        public async Task<bool> UnstageFileAsync(string repoPath, string filePath)
        {
            if (string.IsNullOrEmpty(repoPath) || string.IsNullOrEmpty(filePath))
                return false;

            string relativePath = Path.GetRelativePath(repoPath, filePath);
            string output = await RunGitCommandAsync(repoPath, $"reset HEAD -- \"{relativePath}\"");
            return !output.StartsWith("fatal:");
        }

        public async Task<bool> CommitAsync(string repoPath, string message)
        {
            if (string.IsNullOrEmpty(repoPath) || string.IsNullOrEmpty(message))
                return false;

            // Escape quotes in commit message
            string escapedMsg = message.Replace("\"", "\\\"");
            string output = await RunGitCommandAsync(repoPath, $"commit -m \"{escapedMsg}\"");
            return !output.StartsWith("fatal:");
        }
    }
}
