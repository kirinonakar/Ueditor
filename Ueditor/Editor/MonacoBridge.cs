using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Ueditor.Core.Models;

namespace Ueditor.Editor
{
    public class MonacoBridge
    {
        private readonly WebView2 _webView;
        private bool _isReady = false;
        private string? _pendingText = null;

        public event Action<string>? ContentChanged;
        public event Action<string>? SelectionReceived;
        public event Action<int, int>? CursorChanged;
        public event Action? EditorReady;
        public event Action<string>? ShortcutPressed;

        public MonacoBridge(WebView2 webView)
        {
            _webView = webView;
            _webView.WebMessageReceived += OnWebMessageReceived;
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Ensure WebView2 is initialized in a secure local AppData cache directory to prevent write access crashes
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string cacheFolder = System.IO.Path.Combine(localAppData, "Ueditor", "WebView2Cache");
                var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, cacheFolder, null);
                await _webView.EnsureCoreWebView2Async(env);
                
                // Map local WebResources folder if we want to host locally
                // For MVP 1, we can either use local folder mapping or Ms-Appx-Web.
                // We'll set ms-appx-web based path from UI, but mapping a local folder is also extremely stable.
                _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                _webView.CoreWebView2.Settings.IsScriptEnabled = true;
                
                // Disable browser context menu to keep it premium
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2 initialization failed: {ex.Message}");
            }
        }

        public void LoadEditor(string hostUrl)
        {
            _webView.Source = new Uri(hostUrl);
        }

        public async Task SetTextAsync(string text)
        {
            if (!_isReady)
            {
                _pendingText = text;
                return;
            }

            var msg = new { action = "setText", text = text };
            await SendMessageAsync(msg);
        }

        public async Task SetLanguageAsync(string filePath)
        {
            string ext = System.IO.Path.GetExtension(filePath).ToLower();
            string lang = ext switch
            {
                ".cs" => "csharp",
                ".js" => "javascript",
                ".ts" => "typescript",
                ".html" => "html",
                ".css" => "css",
                ".json" => "json",
                ".md" => "markdown",
                ".markdown" => "markdown",
                ".py" => "python",
                ".cpp" => "cpp",
                ".h" => "cpp",
                ".xml" => "xml",
                ".xaml" => "xml",
                ".sql" => "sql",
                ".sh" => "shell",
                ".tex" => "latex",
                _ => "plaintext"
            };

            var msg = new { action = "setLanguage", language = lang };
            await SendMessageAsync(msg);
        }

        public async Task UpdateOptionsAsync(EditorSettings settings, bool isLargeFile = false, bool isReadOnly = false)
        {
            var msg = new
            {
                action = "updateOptions",
                theme = settings.Theme,
                wordWrap = settings.WordWrap,
                minimap = settings.MinimapEnabled,
                bracketPairColorization = settings.BracketPairColorizationEnabled,
                fontSize = settings.FontSize,
                fontFamily = settings.FontFamily,
                tabSize = settings.TabSize,
                customBackgroundColor = settings.CustomBackgroundColor,
                customForegroundColor = settings.CustomForegroundColor,
                isLargeFile = isLargeFile,
                readOnly = isReadOnly
            };
            await SendMessageAsync(msg);
        }

        public async Task TriggerFindAsync()
        {
            var msg = new { action = "triggerFind" };
            await SendMessageAsync(msg);
        }

        public async Task FocusAsync()
        {
            var msg = new { action = "focus" };
            await SendMessageAsync(msg);
        }

        public async Task RequestSelectionAsync()
        {
            var msg = new { action = "getSelection" };
            await SendMessageAsync(msg);
        }

        public async Task RevealLineAsync(int lineNum)
        {
            var msg = new { action = "revealLine", lineNumber = lineNum };
            await SendMessageAsync(msg);
        }

        public async Task InsertTextAsync(string text)
        {
            var msg = new { action = "insertText", text = text };
            await SendMessageAsync(msg);
        }

        public async Task ApplyMarkdownCommandAsync(string command, string? color = null)
        {
            object msg = color != null
                ? (object)new { action = "markdownCommand", command = command, color = color }
                : (object)new { action = "markdownCommand", command = command };
            await SendMessageAsync(msg);
        }

        private async Task SendMessageAsync(object obj)
        {
            if (!_isReady) return;

            string json = JsonSerializer.Serialize(obj);
            try
            {
                await _webView.ExecuteScriptAsync($"handleCsharpMessage({json});");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to execute script on WebView2: {ex.Message}");
            }
        }

        private void OnWebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string json = NormalizeWebMessageJson(args);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (!root.TryGetProperty("type", out JsonElement typeProp)) return;

                    string type = typeProp.GetString() ?? string.Empty;

                    switch (type)
                    {
                        case "ready":
                            _isReady = true;
                            EditorReady?.Invoke();
                            if (_pendingText != null)
                            {
                                _ = SetTextAsync(_pendingText);
                                _pendingText = null;
                            }
                            break;

                        case "contentChanged":
                            if (root.TryGetProperty("content", out JsonElement contentProp))
                            {
                                ContentChanged?.Invoke(contentProp.GetString() ?? string.Empty);
                            }
                            break;

                        case "cursorChanged":
                            if (root.TryGetProperty("line", out JsonElement lineProp) &&
                                root.TryGetProperty("column", out JsonElement colProp))
                            {
                                CursorChanged?.Invoke(lineProp.GetInt32(), colProp.GetInt32());
                            }
                            break;

                        case "selectionResult":
                            if (root.TryGetProperty("text", out JsonElement selectionProp))
                            {
                                SelectionReceived?.Invoke(selectionProp.GetString() ?? string.Empty);
                            }
                            break;

                        case "shortcut":
                            if (root.TryGetProperty("name", out JsonElement nameProp))
                            {
                                ShortcutPressed?.Invoke(nameProp.GetString() ?? string.Empty);
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error receiving web message: {ex.Message}");
            }
        }

        private static string NormalizeWebMessageJson(CoreWebView2WebMessageReceivedEventArgs args)
        {
            string json = args.WebMessageAsJson;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    return doc.RootElement.GetString() ?? "{}";
                }
            }
            catch
            {
                string? asString = args.TryGetWebMessageAsString();
                if (!string.IsNullOrWhiteSpace(asString))
                {
                    return asString;
                }
            }

            return json;
        }
    }
}
