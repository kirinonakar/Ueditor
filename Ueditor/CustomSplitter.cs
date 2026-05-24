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

            if (this.Background != null && _originalBackground == null)
            {
                _originalBackground = this.Background;
            }

            if (Application.Current.Resources.TryGetValue("SplitterHoverBackgroundBrush", out var hoverBrush) && hoverBrush is Brush brush)
            {
                this.Background = brush;
            }
        }

        private void CustomSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            this.ProtectedCursor = null;

            if (_originalBackground != null)
            {
                this.Background = _originalBackground;
            }
        }
    }
}
