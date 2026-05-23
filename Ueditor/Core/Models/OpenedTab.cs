using System;

namespace Ueditor.Core.Models
{
    public class OpenedTab
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string? FilePath { get; set; } // Null if it's an unsaved new document
        public string Title { get; set; } = "제목 없음";
        public string Content { get; set; } = string.Empty;
        public bool IsDirty { get; set; } = false;
        public bool IsLargeFileMode { get; set; } = false;
        public string Language { get; set; } = "plaintext";

        public string DisplayTitle => IsDirty ? $"{Title} *" : Title;
    }
}
