using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;

namespace Ueditor
{
    public class CustomSplitter : Grid
    {
        private Brush? _originalBackground;
        private double _normalWidth;

        public CustomSplitter()
        {
            this.PointerEntered += CustomSplitter_PointerEntered;
            this.PointerExited += CustomSplitter_PointerExited;
        }

        private void CustomSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            this.ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
            _normalWidth = this.Width;
            this.Width = 6;

            if (this.Background != null && _originalBackground == null)
            {
                _originalBackground = this.Background;
            }
            
            // Premium accent highlight when hovered
            if (Application.Current.Resources.TryGetValue("SystemControlBackgroundAccentBrush", out var accentBrush) && accentBrush is Brush brush)
            {
                this.Background = brush;
            }
        }

        private void CustomSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            this.ProtectedCursor = null;
            if (_normalWidth > 0)
            {
                this.Width = _normalWidth;
            }

            if (_originalBackground != null)
            {
                this.Background = _originalBackground;
            }
        }
    }
}
