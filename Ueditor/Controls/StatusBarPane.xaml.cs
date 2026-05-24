using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Ueditor.Controls
{
    public sealed partial class StatusBarPane : UserControl
    {
        public StatusBarPane()
        {
            InitializeComponent();
        }

        public event RoutedEventHandler? LeftPanelToggleClick;
        public event RoutedEventHandler? RightPanelToggleClick;
        public event SelectionChangedEventHandler? EncodingSelectionChanged;
        public event RoutedEventHandler? LineNumberClick;
        public event RoutedEventHandler? LineEndingClick;

        public ToggleButton LeftPanelToggleButton => LeftPanelToggle;
        public ToggleButton RightPanelToggleButton => RightPanelToggle;
        public TextBlock LineText => StatusLine;
        public TextBlock ColumnText => StatusCol;
        public TextBlock FileStatsText => StatusFileStats;
        public TextBlock GitBranchText => StatusGitBranch;
        public TextBlock ModeText => StatusMode;
        public TextBlock LanguageText => StatusLanguage;
        public ComboBox EncodingCombo => StatusEncodingCombo;
        public TextBlock LineEndingText => StatusLineEnding;
        public Button LineNumberButtonControl => LineNumberButton;
        public Button LineEndingButtonControl => LineEndingButton;

        private void HandleLeftPanelToggleClick(object sender, RoutedEventArgs e)
        {
            LeftPanelToggleClick?.Invoke(sender, e);
        }

        private void HandleRightPanelToggleClick(object sender, RoutedEventArgs e)
        {
            RightPanelToggleClick?.Invoke(sender, e);
        }

        private void HandleEncodingSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EncodingSelectionChanged?.Invoke(sender, e);
        }

        private void HandleLineNumberClick(object sender, RoutedEventArgs e)
        {
            LineNumberClick?.Invoke(sender, e);
        }

        private void HandleLineEndingClick(object sender, RoutedEventArgs e)
        {
            LineEndingClick?.Invoke(sender, e);
        }
    }
}
