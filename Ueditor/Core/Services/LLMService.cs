using System.Threading.Tasks;
using Ueditor.Core.Interfaces;

namespace Ueditor.Core.Services
{
    public class LLMService : ILLMService
    {
        public Task<string> ExplainCodeAsync(string code, string language)
        {
            return Task.FromResult("LLM 모듈이 아직 활성화되지 않았습니다. (Phase 3 기능)");
        }

        public Task<string> SummarizeTextAsync(string text)
        {
            return Task.FromResult("LLM 모듈이 아직 활성화되지 않았습니다. (Phase 3 기능)");
        }

        public Task<string> CustomPromptAsync(string prompt, string context)
        {
            return Task.FromResult("LLM 모듈이 아직 활성화되지 않았습니다. (Phase 3 기능)");
        }

        public Task SaveApiKeyAsync(string provider, string apiKey)
        {
            return Task.CompletedTask;
        }

        public Task<string> GetApiKeyAsync(string provider)
        {
            return Task.FromResult(string.Empty);
        }
    }
}
