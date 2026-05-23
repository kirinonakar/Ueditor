using System.Collections.Generic;
using System.Threading.Tasks;
using Ueditor.Core.Interfaces;

namespace Ueditor.Core.Services
{
    public class GitService : IGitService
    {
        public Task<bool> IsGitRepositoryAsync(string path)
        {
            return Task.FromResult(false);
        }

        public Task<string> GetCurrentBranchAsync(string path)
        {
            return Task.FromResult("main");
        }

        public Task<List<GitChangedFile>> GetChangedFilesAsync(string path)
        {
            return Task.FromResult(new List<GitChangedFile>());
        }

        public Task StageFileAsync(string repoPath, string filePath)
        {
            return Task.CompletedTask;
        }

        public Task UnstageFileAsync(string repoPath, string filePath)
        {
            return Task.CompletedTask;
        }

        public Task CommitAsync(string repoPath, string message)
        {
            return Task.CompletedTask;
        }

        public Task<string> GetFileDiffAsync(string repoPath, string filePath)
        {
            return Task.FromResult(string.Empty);
        }
    }
}
