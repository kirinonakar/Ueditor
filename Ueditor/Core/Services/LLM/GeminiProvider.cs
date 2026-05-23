using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ueditor.Core.Services.LLM
{
    public class GeminiProvider : ILLMProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<string> GenerateCompletionAsync(string endpoint, string apiKey, string model, string systemPrompt, string userContent)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentException("API Key가 유효하지 않습니다. 설정을 먼저 확인해 주십시오.");

            string baseUrl = string.IsNullOrWhiteSpace(endpoint) ? "https://generativelanguage.googleapis.com" : endpoint.TrimEnd('/');
            string requestUrl;
            if (baseUrl.Contains("/v1beta/models") || baseUrl.Contains("/v1/models"))
            {
                requestUrl = $"{baseUrl}/{model}:generateContent";
            }
            else if (baseUrl.Contains("/v1beta") || baseUrl.Contains("/v1"))
            {
                requestUrl = $"{baseUrl}/models/{model}:generateContent";
            }
            else
            {
                requestUrl = $"{baseUrl}/v1beta/models/{model}:generateContent";
            }

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = userContent }
                        }
                    }
                },
                systemInstruction = new
                {
                    parts = new[]
                    {
                        new { text = systemPrompt }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.5
                }
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
            {
                request.Headers.Add("x-goog-api-key", apiKey);
                string jsonPayload = JsonSerializer.Serialize(payload);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using (var response = await _httpClient.SendAsync(request))
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Google Gemini API 호출 실패 ({response.StatusCode}): {responseBody}");
                    }

                    using (var doc = JsonDocument.Parse(responseBody))
                    {
                        var root = doc.RootElement;
                        // Extract candidates[0].content.parts[0].text
                        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                        {
                            var firstCandidate = candidates[0];
                            if (firstCandidate.TryGetProperty("content", out var candidateContent) &&
                                candidateContent.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                            {
                                var firstPart = parts[0];
                                if (firstPart.TryGetProperty("text", out var text))
                                {
                                    return text.GetString() ?? string.Empty;
                                }
                            }
                        }
                    }
                    
                    return "Gemini AI로부터 빈 응답을 수신했습니다.";
                }
            }
        }
    }
}
