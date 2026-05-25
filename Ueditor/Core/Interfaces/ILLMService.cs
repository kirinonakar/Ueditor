using System.Threading.Tasks;

namespace Ueditor.Core.Interfaces
{
    public interface ILLMService
    {
        Task<string> ExplainCodeAsync(string code, string language);
        Task<string> SummarizeTextAsync(string text);
        Task<string> TranslateTextAsync(string text);
        Task<string> ImproveTextAsync(string text);
        Task<string> CustomPromptAsync(string prompt, string context);
        
        // Secure API Key handling
        Task SaveApiKeyAsync(string provider, string apiKey);
        Task<string> GetApiKeyAsync(string provider);
    }
}
