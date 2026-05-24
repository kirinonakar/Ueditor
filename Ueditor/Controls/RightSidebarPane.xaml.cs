using System;
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

        public void Localize(Func<string, string, string> getString)
        {
            LivePreviewTab.Header = getString("LivePreviewTabHeader", "실시간 프리뷰");
            ComboItemMarkdown.Content = getString("ComboItemMarkdown", "Markdown");
            ComboItemHtml.Content = getString("ComboItemHtml", "HTML Source");
            ComboItemLatex.Content = getString("ComboItemLatex", "LaTeX Block");
            ComboItemAozora.Content = getString("ComboItemAozora", "Aozora");
            OpenBrowserButtonText.Text = getString("OpenInBrowserButtonText", "브라우저");
            ToolTipService.SetToolTip(OpenBrowserButton, getString("OpenInBrowserTooltip", "HTML 미리보기를 브라우저로 열기"));

            AiAssistantTab.Header = getString("AiAssistantTabHeader", "AI Assistant");

            string currentLlmText = LlmOutputText.Text;
            if (currentLlmText.Contains("대기 중...") || currentLlmText.Contains("待機中...") || currentLlmText.Contains("Waiting..."))
            {
                LlmOutputText.Text = getString("LlmOutputPlaceholder", "대기 중... 에디터에서 영역을 선택한 후 하단의 AI 분석 도구를 사용해 보세요.");
            }

            string currentStatsText = SelectionStatsText.Text;
            if (currentStatsText.Contains("선택 영역: 없음") || currentStatsText.Contains("選択範囲: なし") || currentStatsText.Contains("Selection: None"))
            {
                SelectionStatsText.Text = getString("SelectionStatsPlaceholder", "선택 영역: 없음 (전체 전송 차단 활성화)");
            }

            LlmFileContextInput.PlaceholderText = getString("LlmFileContextPlaceholder", "파일 맥락 없음");
            LlmAddFileContextButton.Content = getString("LlmAddFileContextButtonText", "파일 맥락 추가");
            LlmExplainButton.Content = getString("LlmExplainButtonText", "선택 영역 설명 (Explain)");
            LlmSummarizeButton.Content = getString("LlmSummarizeButtonText", "선택 영역 요약");
            LlmTranslateButton.Content = getString("LlmTranslateButtonText", "선택 영역 번역");
            LlmImproveButton.Content = getString("LlmImproveButtonText", "수식/마크다운 개선");
            LlmCustomPromptInput.PlaceholderText = getString("LlmCustomPromptPlaceholder", "질문이나 커스텀 지시사항 입력...");
            LlmCustomRunButton.Content = getString("LlmCustomRunButtonText", "실행");
            LlmInsertOutputButton.Content = getString("LlmInsertOutputButtonText", "입력");
            ToolTipService.SetToolTip(LlmInsertOutputButton, getString("LlmInsertOutputTooltip", "AI 응답을 현재 커서에 입력"));
        }

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
