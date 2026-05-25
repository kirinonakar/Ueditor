using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;

namespace Ueditor
{
    public class CustomSplitter : Grid
    {
        private const string SplitterBackgroundBrushKey = "SplitterBackgroundBrush";
        private const string SplitterHoverBackgroundBrushKey = "SplitterHoverBackgroundBrush";

        private bool _isHorizontalSplitter;
        private bool _isPointerOver;

        public CustomSplitter()
        {
            this.PointerEntered += CustomSplitter_PointerEntered;
            this.PointerExited += CustomSplitter_PointerExited;
            this.Loaded += CustomSplitter_Loaded;
        }

        public void RefreshTheme()
        {
            ApplyBackground(_isPointerOver ? SplitterHoverBackgroundBrushKey : SplitterBackgroundBrushKey);
        }

        private void CustomSplitter_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyBackground(SplitterBackgroundBrushKey);
        }

        private void CustomSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isPointerOver = true;
            _isHorizontalSplitter = ActualWidth > ActualHeight * 4;
            this.ProtectedCursor = InputSystemCursor.Create(_isHorizontalSplitter
                ? InputSystemCursorShape.SizeNorthSouth
                : InputSystemCursorShape.SizeWestEast);

            ApplyBackground(SplitterHoverBackgroundBrushKey);
        }

        private void CustomSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isPointerOver = false;
            this.ProtectedCursor = null;
            ApplyBackground(SplitterBackgroundBrushKey);
        }

        private void ApplyBackground(string resourceKey)
        {
            if (Application.Current.Resources.TryGetValue(resourceKey, out var value) && value is Brush brush)
            {
                this.Background = brush;
            }
        }
    }
}
