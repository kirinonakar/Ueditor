using System.Threading.Tasks;
using System.Collections.Generic;

namespace Ueditor.Core.Interfaces
{
    public interface IGitService
    {
        Task<string> GetCurrentBranchAsync(string repoPath);
        Task<Dictionary<string, string>> GetFileStatusesAsync(string repoPath);
        Task<string> GetFileDiffAsync(string repoPath, string filePath);
        Task<bool> StageFileAsync(string repoPath, string filePath);
        Task<bool> UnstageFileAsync(string repoPath, string filePath);
        Task<bool> CommitAsync(string repoPath, string message);
    }
}
