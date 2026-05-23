using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
                        string status = line.Substring(0, 2).Trim();
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
    }
}
