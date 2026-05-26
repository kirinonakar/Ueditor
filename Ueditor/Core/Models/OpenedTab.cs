using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace Ueditor.Core.Models
{
    public class OpenedTab : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; } = Guid.NewGuid().ToString();
        public string? FilePath { get; set; }
        public string Title { get; set; } = "제목 없음";
        public string Content { get; set; } = string.Empty;

        private bool _isDirty = false;
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public string Language { get; set; } = "plaintext";
        public string EncodingName { get; set; } = "UTF-8";
        public bool EncodingWasAutoDetected { get; set; } = true;

        public string DisplayTitle => IsDirty ? $"{Title} *" : Title;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
