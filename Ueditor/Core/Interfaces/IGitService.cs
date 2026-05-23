using System.Threading.Tasks;
using System.Collections.Generic;

namespace Ueditor.Core.Interfaces
{
    public interface IGitService
    {
        Task<string> GetCurrentBranchAsync(string repoPath);
        Task<Dictionary<string, string>> GetFileStatusesAsync(string repoPath);
    }
}
