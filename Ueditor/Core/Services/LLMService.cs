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
            string systemPrompt = "당신은 정확한 개발 문서 해설자입니다. 사용자가 제공한 선택 영역만 근거로 삼아 한글로 설명합니다. 선택 영역이 코드이면 동작 흐름, 주요 식별자/함수, 입력과 출력, 부작용, 주의할 버그 가능성을 설명합니다. 선택 영역이 마크다운/일반 텍스트/설정 파일이면 구조와 의미를 설명합니다. 존재하지 않는 주변 코드나 프로젝트 의도를 추측하지 말고, 불확실한 부분은 '선택 영역만으로는 확인할 수 없음'이라고 명시합니다. 원문을 통째로 반복하지 말고 핵심을 정리합니다.";
            string userContent = $"[선택 영역 언어 또는 파일 유형]\n{language}\n\n[선택 영역]\n{code}";
            return await ExecuteLlmAsync(systemPrompt, userContent);
        }

        public async Task<string> SummarizeTextAsync(string text)
        {
            string systemPrompt = "당신은 정확한 요약 전문가입니다. 사용자가 제공한 선택 영역만 요약합니다. 번역, 해설, 개선, 재작성은 하지 않습니다. 핵심 주장/목적/결론/할 일을 한글로 간결하게 정리하고, 코드인 경우에는 구현 의도와 주요 처리 단계만 요약합니다. 원문에 없는 내용은 추가하지 않습니다.";
            string userContent = $"[요약할 선택 영역]\n{text}";
            return await ExecuteLlmAsync(systemPrompt, userContent);
        }

        public async Task<string> TranslateTextAsync(string text)
        {
            string systemPrompt = "당신은 전문 번역가입니다. 사용자가 제공한 선택 영역만 번역합니다. 한국어가 주된 텍스트이면 자연스러운 영어로 번역하고, 그 외 언어가 주된 텍스트이면 자연스러운 한국어로 번역합니다. 코드 블록, 마크다운 문법, URL, 파일 경로, 변수명, 함수명, 명령어는 보존하고 주석과 일반 문장만 번역합니다. 설명이나 요약을 덧붙이지 말고 번역문만 출력합니다.";
            string userContent = $"[번역할 선택 영역]\n{text}";
            return await ExecuteLlmAsync(systemPrompt, userContent);
        }

        public async Task<string> CustomPromptAsync(string prompt, string context)
        {
            string systemPrompt = "당신은 정확한 개발 보조자입니다. 제공된 선택 영역을 근거로 사용자의 지시사항에 답합니다. 선택 영역에 없는 사실을 단정하지 말고, 필요한 경우 불확실성을 명시합니다.";
            string userContent = $"[컨텍스트 텍스트]\n{context}\n\n[사용자 지시사항]\n{prompt}";
            return await ExecuteLlmAsync(systemPrompt, userContent);
        }

        public Task SaveApiKeyAsync(string provider, string apiKey)
        {
            try
            {
                string targetName = $"Ueditor_LLM_{provider}";
                if (string.IsNullOrEmpty(apiKey))
                {
                    _credentialService.DeleteCredential(targetName);
                }
                else
                {
                    _credentialService.WriteCredential(targetName, "ueditor_user", apiKey);
                }
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
