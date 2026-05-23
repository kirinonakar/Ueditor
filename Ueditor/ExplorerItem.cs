using System;
using System.Collections.ObjectModel;
using System.IO;

namespace Ueditor
{
    public class ExplorerItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsFolder { get; set; } = false;

        public string IconGlyph => IsFolder ? "\uED41" : GetFileIconGlyph(Name);

        public ObservableCollection<ExplorerItem> Children { get; } = new ObservableCollection<ExplorerItem>();

        public bool HasUnrealizedChildren
        {
            get => IsFolder && Children.Count == 0;
            set
            {
                // Unused but needed for XAML binding syntax
            }
        }

        private static string GetFileIconGlyph(string fileName)
        {
            string ext = System.IO.Path.GetExtension(fileName).ToLower();
            return ext switch
            {
                ".txt" => "\uE8A5", // Document icon
                ".md" => "\uE8A5",
                ".markdown" => "\uE8A5",
                ".html" => "\uE743", // Web icon
                ".htm" => "\uE743",
                ".css" => "\uE743",
                ".js" => "\uE94A",  // Code icon
                ".ts" => "\uE94A",
                ".cs" => "\uE74C",  // C# Developer icon or custom code glyph
                ".xaml" => "\uF158",
                ".xml" => "\uF158",
                ".json" => "\uE94A",
                ".png" => "\uEB9F", // Picture icon
                ".jpg" => "\uEB9F",
                ".jpeg" => "\uEB9F",
                ".gif" => "\uEB9F",
                ".pdf" => "\uE72A",
                _ => "\uE160"       // Generic file icon
            };
        }
    }
}
