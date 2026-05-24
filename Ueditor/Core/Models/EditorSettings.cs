using System;

namespace Ueditor.Core.Models
{
    public class EditorSettings
    {
        public string Theme { get; set; } = "Dark"; // "Light" or "Dark"
        public string FontFamily { get; set; } = "Consolas, 'Courier New', monospace";
        public double FontSize { get; set; } = 14.0;
        public bool WordWrap { get; set; } = true;
        public int TabSize { get; set; } = 4;
        public bool MinimapEnabled { get; set; } = true;
        public bool BracketPairColorizationEnabled { get; set; } = true;
        public long LargeFileThresholdMB { get; set; } = 50;
        
        // Personalization Settings
        public string CustomBackgroundColor { get; set; } = string.Empty; // Hex color string e.g., "#1e1e1e"
        public string CustomForegroundColor { get; set; } = string.Empty; // Hex color string e.g., "#d4d4d4"
        public string UiFontFamily { get; set; } = "Segoe UI, Malgun Gothic";
        public string MarkdownToolbarBackgroundColor { get; set; } = string.Empty;
        public bool AutoSave { get; set; } = false;
        public string PreviewMode { get; set; } = "Markdown"; // "Markdown", "HTML", "LaTeX"
        public bool LeftSidebarVisible { get; set; } = true;
        public bool RightSidebarVisible { get; set; } = true;
        public bool DefaultMarkdownEnabled { get; set; } = true;
        public bool DefaultMarkdownToolbarEnabled { get; set; } = false;
        public int WindowX { get; set; } = -1;
        public int WindowY { get; set; } = -1;
        public int WindowWidth { get; set; } = 1200;
        public int WindowHeight { get; set; } = 800;
        public double TerminalPanelHeight { get; set; } = 220;

        // LLM Config
        public string LlmProvider { get; set; } = "OpenAI"; // "OpenAI" or "Gemini"
        public string LlmEndpoint { get; set; } = "https://api.openai.com/v1";
        public string LlmModel { get; set; } = "gpt-5.5";
        public string LlmModelGemini { get; set; } = "gemini-flash-lite-latest";
        public string LlmModelOpenAI { get; set; } = "gpt-5.5";
        public string LlmModelLmStudio { get; set; } = "";

        // Git Config
        public bool AutoGitDetect { get; set; } = true;

        // Favorites Config
        public System.Collections.Generic.List<string> FavoritePaths { get; set; } = new System.Collections.Generic.List<string>();

        // Language Localization
        public string Language { get; set; } = "Default"; // "Default", "ko-KR", "en-US", "ja-JP"
    }
}
