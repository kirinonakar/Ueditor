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

        // LLM Config
        public string LlmProvider { get; set; } = "OpenAI"; // "OpenAI" or "Gemini"
        public string LlmEndpoint { get; set; } = "https://api.openai.com/v1";
        public string LlmModel { get; set; } = "gpt-4";

        // Git Config
        public bool AutoGitDetect { get; set; } = true;

        // Favorites Config
        public System.Collections.Generic.List<string> FavoritePaths { get; set; } = new System.Collections.Generic.List<string>();
    }
}
