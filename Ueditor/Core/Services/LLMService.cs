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

        private string GetActiveLanguage()
        {
            var lang = _settingsService?.CurrentSettings?.Language;
            if (string.IsNullOrEmpty(lang) || lang.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    lang = System.Globalization.CultureInfo.CurrentUICulture.Name;
                }
                catch
                {
                    lang = "en-US";
                }
            }

            if (lang != null)
            {
                if (lang.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko-KR";
                if (lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja-JP";
            }
            return "en-US";
        }

        public async Task<string> ExplainCodeAsync(string code, string language)
        {
            string langCode = GetActiveLanguage();
            string systemPrompt = langCode switch
            {
                "ja-JP" => "あなたは正確な開発ドキュメント解説者です。ユーザーが提供した選択範囲のみを根拠に、日本語で説明します。選択範囲がコードの場合、動作フロー、主要な識別子・関数、入力と出力、副作用、潜在的なバグの可能性について説明します。選択範囲がMarkdown、一般テキスト、設定ファイルなどの場合は、その構造と意味を説明します。存在しない周辺コードやプロジェクトの意図を推測せず、不確実な部分は『選択範囲だけでは確認不可』と明記してください。原文をそのまま繰り返すのではなく、要点を整理します。",
                "en-US" => "You are an accurate developer documentation explainer. Explain in English, strictly grounding your explanations in the provided text selection. If the selection is code, explain the execution flow, primary identifiers/functions, input and output, side effects, and potential bugs. If the selection is Markdown, plain text, or configuration, explain its structure and meaning. Do not speculate about surrounding code or project intent that is absent, and explicitly write 'cannot be verified from the selection alone' for any uncertainties. Keep it concise without repeating the source text verbatim.",
                _ => "당신은 정확한 개발 문서 해설자입니다. 사용자가 제공한 선택 영역만 근거로 삼아 한글로 설명합니다. 선택 영역이 코드이면 동작 흐름, 주요 식별자/함수, 입력과 출력, 부작용, 주의할 버그 가능성을 설명합니다. 선택 영역이 마크다운/일반 텍스트/설정 파일이면 구조와 의미를 설명합니다. 존재하지 않는 주변 코드나 프로젝트 의도를 추측하지 말고, 불확실한 부분은 '선택 영역만으로는 확인할 수 없음'이라고 명시합니다. 원문을 통째로 반복하지 말고 핵심을 정리합니다."
            };

            string userContent = langCode switch
            {
                "ja-JP" => $"[選択範囲の言語またはファイルタイプ]\n{language}\n\n[選択範囲]\n{code}",
                "en-US" => $"[Selection Language or File Type]\n{language}\n\n[Selection]\n{code}",
                _ => $"[선택 영역 언어 또는 파일 유형]\n{language}\n\n[선택 영역]\n{code}"
            };

            return await ExecuteLlmAsync(systemPrompt, userContent);
        }

        public async Task<string> SummarizeTextAsync(string text)
        {
            string langCode = GetActiveLanguage();
            string systemPrompt = langCode switch
            {
                "ja-JP" => "あなたは正確な要約のスペシャリストです。ユーザーが提供した選択範囲のみを要約します。翻訳、解説、改善、書き換えは行いません。主要な主張、目的、結論、ToDoリストを日本語で簡潔に整理し、コードの場合は実装の意図と主要な処理ステップのみを要約します。原文にない内容は絶対に追加しないでください。挨拶、導入説明、要約の結果を示すラベル（例：『以下は要約結果です』）などの余計なテキストを一切含めず、純粋な要約コンテンツだけを直接出力してください。",
                "en-US" => "You are an accurate summarization expert. Summarize only the provided selection in English. Do not translate, explain, improve, or rewrite. Summarize the key arguments, purposes, conclusions, and action items concisely. If the selection is code, summarize only the implementation intent and major steps. Do not introduce any details not explicitly mentioned in the source. Do not include any greetings, introductory phrases, meta-commentary, or surrounding labels (e.g., 'Here is the summary:'). Output ONLY the final summarized text directly.",
                _ => "당신은 정확한 요약 전문가입니다. 사용자가 제공한 선택 영역만 요약합니다. 번역, 해설, 개선, 재작성은 하지 않습니다. 핵심 주장/목적/결론/할 일을 한글로 간결하게 정리하고, 코드인 경우에는 구현 의도와 주요 처리 단계만 요약합니다. 원문에 없는 내용은 절대 추가하지 마십시오. 인사말, 도입부, 부가 설명, 혹은 '요약 결과입니다:'와 같은 불필요한 메타 안내 문구를 단 한 자도 출력하지 마십시오. 오직 정제된 핵심 요약 본문만 직접적으로 출력해 주십시오."
            };

            string userContent = langCode switch
            {
                "ja-JP" => $"[要約する選択範囲]\n{text}",
                "en-US" => $"[Selection to Summarize]\n{text}",
                _ => $"[요약할 선택 영역]\n{text}"
            };

            return await ExecuteLlmAsync(systemPrompt, userContent);
        }

        public async Task<string> TranslateTextAsync(string text)
        {
            var settings = _settingsService.CurrentSettings;
            string srcLang = settings.LlmSourceLanguage ?? "Auto";
            string tgtLang = settings.LlmTargetLanguage ?? "Korean";

            string srcLangDisplay = srcLang switch
            {
                "Korean" => "한국어 (Korean)",
                "English" => "영어 (English)",
                "Japanese" => "일본어 (Japanese)",
                "Chinese" => "중국어 (Chinese)",
                "French" => "프랑스어 (French)",
                "Spanish" => "스페인어 (Spanish)",
                "German" => "독일어 (German)",
                _ => "자동 감지 (Auto Detect)"
            };

            string tgtLangDisplay = tgtLang switch
            {
                "English" => "영어 (English)",
                "Japanese" => "일본어 (Japanese)",
                "Chinese" => "중국어 (Chinese)",
                "French" => "프랑스어 (French)",
                "Spanish" => "스페인어 (Spanish)",
                "German" => "독일어 (German)",
                _ => "한국어 (Korean)"
            };

            string langCode = GetActiveLanguage();
            string systemPrompt = langCode switch
            {
                "ja-JP" => $"あなたはプロの翻訳家です。ユーザーが提供した選択範囲のみを翻訳します。入力テキストの言語（{srcLangDisplay}）を翻訳対象言語（{tgtLangDisplay}）に正確に翻訳してください。コードブロック、Markdown構文、URL、ファイルパス、変数名、関数名、コマンドなどはそのまま保持し、コメントや一般の文のみを翻訳します。挨拶、導入説明、解説、要約、『以下は翻訳結果です』といったメタテキスト、および不要なマークダウンのコードブロック包み（```）などは一切追加せず、純粋な翻訳結果のテキストのみを出力してください。",
                "en-US" => $"You are a professional translator. Translate only the provided text selection. Translate the input text (Source: {srcLangDisplay}) to the target language (Target: {tgtLangDisplay}). Preserve code blocks, Markdown syntax, URLs, file paths, variable names, function names, and commands intact, translating only comments and prose. Do not add any greetings, explanations, summaries, introductory phrases, meta-commentary, or markdown code block wrapper backticks (e.g. ```) unless the original text itself contained them. Output ONLY the raw translated text directly.",
                _ => $"당신은 전문 번역가입니다. 사용자가 제공한 선택 영역만 번역합니다. 입력 텍스트(원본 언어: {srcLangDisplay})를 대상 언어({tgtLangDisplay})로 정확하고 자연스럽게 번역하십시오. 코드 블록, 마크다운 문법, URL, 파일 경로, 변수명, 함수명, 명령어는 그대로 유지하고 주석과 일반 문장만 번역합니다. 인사말, 도입부 설명, 역주(해설), 요약, 혹은 '번역 결과:' 같은 불필요한 부가 문구나 메타 텍스트를 절대 출력하지 마십시오. 번역 결과를 마크다운 코드 블록(```)으로 감싸지 말고(원문에 백틱이 포함된 경우 제외), 오직 순수한 번역 결과 텍스트 자체만 즉시 출력하십시오."
            };

            string userContent = langCode switch
            {
                "ja-JP" => $"[翻訳する選択範囲]\n{text}",
                "en-US" => $"[Selection to Translate]\n{text}",
                _ => $"[번역할 선택 영역]\n{text}"
            };

            return await ExecuteLlmAsync(systemPrompt, userContent);
        }

        public async Task<string> ImproveTextAsync(string text)
        {
            string langCode = GetActiveLanguage();
            string systemPrompt = langCode switch
            {
                "ja-JP" => "あなたはドキュメント改善のスペシャリストです。提供されたテキストの可読性、Markdownの書式、LaTeXの数式（数式の文法や標準的な表現など）を正確に検証・改善し、洗練された日本語に修正してください。挨拶、余計な説明や『修正しました』などの補足テキスト、コードブロックでの強制的なラッピング（```）を一切行わず、改善・修正されたドキュメントのテキストのみを直接出力してください。",
                "en-US" => "You are a document improvement specialist. Inspect and improve the readability, Markdown formatting, or LaTeX mathematical formulas of the provided text, and refine it beautifully in English. Do not include any greetings, explanations, conversational filler, introductory words, or wrap the response in markdown code blocks (e.g., ```). Output ONLY the refined text directly.",
                _ => "당신은 문서 및 수식 정제 전문가입니다. 제공된 텍스트의 가독성, 마크다운(Markdown) 형식, 또는 LaTeX 수학 공식을 표준 문법과 예쁜 형식에 맞게 개선하여 가장 자연스럽고 깔끔한 한국어/한글로 정제해 주십시오. 인사말, 수정 내역 설명, '개선 완료된 결과입니다:'와 같은 부가 설명이나 메타 코멘트를 단 한 단어도 포함하지 마십시오. 백틱 기호(```)를 사용해 결과물 전체를 마크다운 코드 블록으로 래핑하지 마십시오(원래 원문이 코드 블록이었던 경우 제외). 오직 정제 및 개선된 결과물 본문만 순수하게 직접 출력하십시오."
            };

            string userContent = langCode switch
            {
                "ja-JP" => $"[改善する選択範囲]\n{text}",
                "en-US" => $"[Selection to Improve]\n{text}",
                _ => $"[개선할 선택 영역]\n{text}"
            };

            return await ExecuteLlmAsync(systemPrompt, userContent);
        }

        public async Task<string> CustomPromptAsync(string prompt, string context)
        {
            string langCode = GetActiveLanguage();
            string systemPrompt = langCode switch
            {
                "ja-JP" => "あなたは正確な開発アシスタントです。提供された選択範囲を根拠に、ユーザーの指示に回答します。選択範囲にない事実を断定せず、必要に応じて不確実性を明記してください。日本語で回答してください。",
                "en-US" => "You are an accurate developer assistant. Answer the user's instructions based strictly on the provided text selection. Do not assume facts outside the selection, and state any uncertainty clearly. Write your response in English.",
                _ => "당신은 정확한 개발 보조자입니다. 제공된 선택 영역을 근거로 사용자의 지시사항에 답합니다. 선택 영역에 없는 사실을 단정하지 말고, 필요한 경우 불확실성을 명시합니다. 답변은 한국어로 작성합니다."
            };

            string userContent = langCode switch
            {
                "ja-JP" => $"[コンテキストテキスト]\n{context}\n\n[ユーザーの指示]\n{prompt}",
                "en-US" => $"[Context Text]\n{context}\n\n[User Instructions]\n{prompt}",
                _ => $"[컨텍스트 텍스트]\n{context}\n\n[사용자 지시사항]\n{prompt}"
            };

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

            string langCode = GetActiveLanguage();
            if (requiresApiKey && string.IsNullOrEmpty(apiKey))
            {
                return langCode switch
                {
                    "ja-JP" => "エラー: 該当する LLM API Key が資格情報マネージャーに登録されていません。設定を開いて先に API Key を保存してください。",
                    "en-US" => "Error: The corresponding LLM API Key is not registered in the Credential Manager. Please open Settings and save your API Key first.",
                    _ => "에러: 해당 LLM API Key가 자격 증명 관리자에 등록되어 있지 않습니다. 설정을 열어 API Key를 먼저 저장해 주십시오."
                };
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
                string errorPrefix = langCode switch
                {
                    "ja-JP" => "AI通信エラーが発生しました: ",
                    "en-US" => "An AI communication error occurred: ",
                    _ => "AI 통신 오류가 발생했습니다: "
                };
                return $"{errorPrefix}{ex.Message}";
            }
        }
    }
}
