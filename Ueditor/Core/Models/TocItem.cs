using Microsoft.UI.Xaml;

namespace Ueditor.Core.Models
{
    public sealed class TocItem
    {
        public string DisplayText { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string LineDisplay => $"L{LineNumber}";
        public string IconGlyph { get; set; } = "\uE9D2"; // Default document outline glyph
        public Thickness Margin { get; set; } = new Thickness(0, 2, 0, 2);
    }
}
