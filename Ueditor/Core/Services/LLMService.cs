using System;
using System.Threading.Tasks;
using Ueditor.Core.Interfaces;
using Ueditor.Core.Services.LLM;

namespace Ueditor.Core.Services
{
    public class LLMService : ILLMService
    {
        private readonly ISettingsService _settingsService;
        private readonly ICredentialService _credentialService;

        public LLMService(ISettingsService settingsService, ICredentialService credentialService)
        {
            _settingsService = settingsService;
            _credentialService = credentialService;
        }

        public async Task<string> ExplainCodeAsync(string code, string language)
        {
            string systemPrompt = $"당신은 유능한 소프트웨어 아키텍트이자 개발 비서입니다. 입력받은 {language} 코드를 분석하여 이해하기 쉽게 한글로 상세하게 설명해 주십시오. 코드 블록의 동작 원리, 핵심 로직, 성능상의 참고점을 일목요연하게 짚어야 합니다.";
            return await ExecuteLlmAsync(systemPrompt, code);
        }

        public async Task<string> SummarizeTextAsync(string text)
        {
            string systemPrompt = "당신은 텍스트 요약 전문 인공지능 비서입니다. 입력된 텍스트의 핵심 내용을 발췌하여 명확하고 가독성 좋은 개조식 한글 요약본으로 작성해 주십시오.";
            return await ExecuteLlmAsync(systemPrompt, text);
        }

        public async Task<string> CustomPromptAsync(string prompt, string context)
        {
            string systemPrompt = "당신은 사용자의 요청에 유연하고 똑똑하게 답하는 인공지능 개발 비서입니다. 제공된 텍스트 컨텍스트를 참고하여, 사용자의 개별 요청 지시사항에 정확하게 맞추어 정제된 한글 응답을 생성해 주십시오.";
            string userContent = $"[컨텍스트 텍스트]\n{context}\n\n[사용자 지시사항]\n{prompt}";
            return await ExecuteLlmAsync(systemPrompt, userContent);
        }

        public Task SaveApiKeyAsync(string provider, string apiKey)
        {
            try
            {
                string targetName = $"Ueditor_LLM_{provider}";
                _credentialService.WriteCredential(targetName, "ueditor_user", apiKey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed storing API Key securely: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public Task<string> GetApiKeyAsync(string provider)
        {
            try
            {
                string targetName = $"Ueditor_LLM_{provider}";
                string? key = _credentialService.ReadCredential(targetName);
                return Task.FromResult(key ?? string.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed reading secure API Key: {ex.Message}");
                return Task.FromResult(string.Empty);
            }
        }

        // ----------------------------------------------------
        // Private dynamic Provider Dispatcher
        // ----------------------------------------------------

        private async Task<string> ExecuteLlmAsync(string systemPrompt, string userContent)
        {
            var settings = _settingsService.CurrentSettings;
            string providerName = settings.LlmProvider;
            string apiKey = await GetApiKeyAsync(providerName);
            bool requiresApiKey = !providerName.Equals("LM Studio", StringComparison.OrdinalIgnoreCase) &&
                                  !providerName.Equals("LMStudio", StringComparison.OrdinalIgnoreCase);

            if (requiresApiKey && string.IsNullOrEmpty(apiKey))
            {
                return "에러: 해당 LLM API Key가 자격 증명 관리자에 등록되어 있지 않습니다. 설정을 열어 API Key를 먼저 저장해 주십시오.";
            }

            ILLMProvider provider = providerName.ToLower() switch
            {
                "gemini" => new GeminiProvider(),
                "lm studio" => new LMStudioProvider(),
                "lmstudio" => new LMStudioProvider(),
                _ => new OpenAIProvider()
            };

            try
            {
                return await provider.GenerateCompletionAsync(
                    settings.LlmEndpoint,
                    apiKey,
                    settings.LlmModel,
                    systemPrompt,
                    userContent
                );
            }
            catch (Exception ex)
            {
                return $"AI 통신 오류가 발생했습니다: {ex.Message}";
            }
        }
    }
}
