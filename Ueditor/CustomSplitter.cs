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
        private double _normalHeight;
        private bool _isHorizontalSplitter;

        public CustomSplitter()
        {
            this.PointerEntered += CustomSplitter_PointerEntered;
            this.PointerExited += CustomSplitter_PointerExited;
        }

        private void CustomSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isHorizontalSplitter = ActualWidth > ActualHeight * 4;
            this.ProtectedCursor = InputSystemCursor.Create(_isHorizontalSplitter
                ? InputSystemCursorShape.SizeNorthSouth
                : InputSystemCursorShape.SizeWestEast);
            _normalWidth = this.Width;
            _normalHeight = this.Height;

            if (_isHorizontalSplitter)
            {
                this.Height = 6;
            }
            else
            {
                this.Width = 6;
            }

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
            if (_isHorizontalSplitter)
            {
                if (_normalHeight > 0)
                {
                    this.Height = _normalHeight;
                }
            }
            else if (_normalWidth > 0)
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
