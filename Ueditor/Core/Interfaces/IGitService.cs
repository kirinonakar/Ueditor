using System.Threading.Tasks;
using System.Collections.Generic;

namespace Ueditor.Core.Interfaces
{
    public interface IGitService
    {
        Task<string> GetCurrentBranchAsync(string repoPath);
        Task<Dictionary<string, string>> GetFileStatusesAsync(string repoPath);
        Task<string> GetFileDiffAsync(string repoPath, string filePath);
        Task<string> GetGitFileContentAsync(string repoPath, string filePath);
        Task<bool> StageFileAsync(string repoPath, string filePath);
        Task<bool> StageAllAsync(string repoPath);
        Task<bool> UnstageFileAsync(string repoPath, string filePath);
        Task<bool> RestoreFileAsync(string repoPath, string filePath);
        Task<bool> RestoreAllAsync(string repoPath);
        Task<bool> CommitAsync(string repoPath, string message);
        Task<bool> PushAsync(string repoPath);
        Task<IReadOnlyList<string>> GetRecentHistoryAsync(string repoPath, int maxCount = 20);
        Task<IReadOnlyList<string>> GetBranchesAsync(string repoPath);
    }
}
