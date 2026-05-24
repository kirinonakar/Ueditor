using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ueditor.Controls
{
    public sealed partial class RightSidebarPane : UserControl
    {
        public RightSidebarPane()
        {
            InitializeComponent();
        }

        public event SelectionChangedEventHandler? PreviewModeSelectionChanged;
        public event RoutedEventHandler? OpenPreviewInBrowserClick;
        public event RoutedEventHandler? LlmAddFileContextClick;
        public event RoutedEventHandler? LlmExplainClick;
        public event RoutedEventHandler? LlmSummarizeClick;
        public event RoutedEventHandler? LlmTranslateClick;
        public event RoutedEventHandler? LlmImproveClick;
        public event RoutedEventHandler? LlmCustomClick;
        public event RoutedEventHandler? LlmInsertOutputClick;

        public TabView RightTabs => RightTabView;
        public ComboBox PreviewMode => PreviewModeCombo;
        public WebView2 PreviewWebViewControl => PreviewWebView;
        public TextBlock LlmOutput => LlmOutputText;
        public TextBlock SelectionStats => SelectionStatsText;
        public TextBox LlmFileContext => LlmFileContextInput;
        public TextBox LlmCustomPrompt => LlmCustomPromptInput;

        // Named controls exposed for localization
        public TabViewItem LivePreviewTabItem => LivePreviewTab;
        public ComboBoxItem ComboMarkdown => ComboItemMarkdown;
        public ComboBoxItem ComboHtml => ComboItemHtml;
        public ComboBoxItem ComboLatex => ComboItemLatex;
        public ComboBoxItem ComboAozora => ComboItemAozora;
        public Button OpenBrowserBtn => OpenBrowserButton;
        public TextBlock OpenBrowserBtnText => OpenBrowserButtonText;
        public TabViewItem AiAssistantTabItem => AiAssistantTab;
        public Button LlmAddFileCtxButton => LlmAddFileContextButton;
        public Button LlmExplainBtn => LlmExplainButton;
        public Button LlmSummarizeBtn => LlmSummarizeButton;
        public Button LlmTranslateBtn => LlmTranslateButton;
        public Button LlmImproveBtn => LlmImproveButton;
        public Button LlmCustomRunBtn => LlmCustomRunButton;
        public Button LlmInsertOutputBtn => LlmInsertOutputButton;

        private void OnPreviewModeComboSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PreviewModeSelectionChanged?.Invoke(sender, e);
        }

        private void OnOpenPreviewInBrowserClick(object sender, RoutedEventArgs e)
        {
            OpenPreviewInBrowserClick?.Invoke(sender, e);
        }

        private void OnLlmAddFileContextClick(object sender, RoutedEventArgs e)
        {
            LlmAddFileContextClick?.Invoke(sender, e);
        }

        private void OnLlmExplainClick(object sender, RoutedEventArgs e)
        {
            LlmExplainClick?.Invoke(sender, e);
        }

        private void OnLlmSummarizeClick(object sender, RoutedEventArgs e)
        {
            LlmSummarizeClick?.Invoke(sender, e);
        }

        private void OnLlmTranslateClick(object sender, RoutedEventArgs e)
        {
            LlmTranslateClick?.Invoke(sender, e);
        }

        private void OnLlmImproveClick(object sender, RoutedEventArgs e)
        {
            LlmImproveClick?.Invoke(sender, e);
        }

        private void OnLlmCustomClick(object sender, RoutedEventArgs e)
        {
            LlmCustomClick?.Invoke(sender, e);
        }

        private void OnLlmInsertOutputClick(object sender, RoutedEventArgs e)
        {
            LlmInsertOutputClick?.Invoke(sender, e);
        }
    }
}
