using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ueditor.Core.Interfaces
{
    public interface IGitService
    {
        Task<bool> IsGitRepositoryAsync(string path);
        Task<string> GetCurrentBranchAsync(string path);
        Task<List<GitChangedFile>> GetChangedFilesAsync(string path);
        Task StageFileAsync(string repoPath, string filePath);
        Task UnstageFileAsync(string repoPath, string filePath);
        Task CommitAsync(string repoPath, string message);
        Task<string> GetFileDiffAsync(string repoPath, string filePath);
    }

    public class GitChangedFile
    {
        public string FilePath { get; set; } = string.Empty;
        public string Status { get; set; } = "Modified"; // Modified, Added, Deleted, Untracked
        public bool IsStaged { get; set; } = false;
    }
}
