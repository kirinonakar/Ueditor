using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
        public event RoutedEventHandler? LlmRemoveFileContextClick;
        public event RoutedEventHandler? LlmExplainClick;
        public event RoutedEventHandler? LlmSummarizeClick;
        public event RoutedEventHandler? LlmTranslateClick;
        public event RoutedEventHandler? LlmImproveClick;
        public event Action<string>? LlmTargetLanguageSelected;
        private Func<string, string, string>? _getString;
        private string _currentTargetLanguage = "Korean";
        public event RoutedEventHandler? LlmCustomClick;
        public event RoutedEventHandler? LlmInsertOutputClick;
        public event RoutedEventHandler? LlmAddInstructionClick;

        public TabView RightTabs => RightTabView;
        public ComboBox PreviewMode => PreviewModeCombo;
        public WebView2 PreviewWebViewControl => PreviewWebView;
        public TextBox LlmOutput => LlmOutputText;
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
        public Button LlmRemoveFileCtxButton => LlmRemoveFileContextButton;
        public Button LlmExplainBtn => LlmExplainButton;
        public Button LlmSummarizeBtn => LlmSummarizeButton;
        public Button LlmTranslateBtn => LlmTranslateButton;
        public Button LlmImproveBtn => LlmImproveButton;
        public Button LlmCustomRunBtn => LlmCustomRunButton;
        public Button LlmInsertOutputBtn => LlmInsertOutputButton;
        public Button LlmAddInstructionBtn => LlmAddInstructionButton;
        public ScrollViewer InstructionTabScroller => InstructionTabScrollViewer;

        public void Localize(Func<string, string, string> getString)
        {
            LivePreviewTab.Header = getString("LivePreviewTabHeader", "실시간 프리뷰");
            ComboItemMarkdown.Content = getString("ComboItemMarkdown", "Markdown");
            ComboItemHtml.Content = getString("ComboItemHtml", "HTML Preview");
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
            LlmRemoveFileContextButton.Content = getString("LlmRemoveFileContextButtonText", "삭제");
            LlmExplainButton.Content = getString("LlmExplainButtonText", "설명");
            LlmSummarizeButton.Content = getString("LlmSummarizeButtonText", "요약");
            _getString = getString;
            UpdateTranslateButtonText();
            LlmTargetLangKorean.Text = getString("LlmLangKorean", "한국어 (Korean)");
            LlmTargetLangEnglish.Text = getString("LlmLangEnglish", "영어 (English)");
            LlmTargetLangJapanese.Text = getString("LlmLangJapanese", "일본어 (Japanese)");
            LlmTargetLangChinese.Text = getString("LlmLangChinese", "중국어 (Chinese)");
            LlmTargetLangFrench.Text = getString("LlmLangFrench", "프랑스어 (French)");
            LlmTargetLangSpanish.Text = getString("LlmLangSpanish", "스페인어 (Spanish)");
            LlmTargetLangGerman.Text = getString("LlmLangGerman", "독일어 (German)");
            LlmImproveButton.Content = getString("LlmImproveButtonText", "개선");
            LlmCustomPromptInput.PlaceholderText = getString("LlmCustomPromptPlaceholder", "질문이나 커스텀 지시사항 입력...");
            LlmCustomRunButton.Content = getString("LlmCustomRunButtonText", "전송");
            LlmInsertOutputButton.Content = getString("LlmInsertOutputButtonText", "입력");
            ToolTipService.SetToolTip(LlmInsertOutputButton, getString("LlmInsertOutputTooltip", "AI 응답을 현재 커서에 입력 (선택한 경우 선택부위만)"));
            ToolTipService.SetToolTip(LlmAddInstructionButton, getString("LlmAddInstructionTooltip", "새 커스텀 지시문 추가"));
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

        private void OnLlmRemoveFileContextClick(object sender, RoutedEventArgs e)
        {
            LlmRemoveFileContextClick?.Invoke(sender, e);
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

        private void OnLlmCustomPromptInputKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
                if (ctrl)
                {
                    e.Handled = true;
                    LlmCustomClick?.Invoke(sender, e);
                }
            }
        }

        private void OnLlmInsertOutputClick(object sender, RoutedEventArgs e)
        {
            LlmInsertOutputClick?.Invoke(sender, e);
        }

        private void OnLlmAddInstructionClick(object sender, RoutedEventArgs e)
        {
            LlmAddInstructionClick?.Invoke(sender, e);
        }

        private void OnLlmTargetLangClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string lang)
            {
                LlmTargetLanguageSelected?.Invoke(lang);
            }
        }

        public void UpdateTranslateLanguage(string targetLanguage)
        {
            _currentTargetLanguage = targetLanguage;
            UpdateTranslateButtonText();
        }

        private void UpdateTranslateButtonText()
        {
            if (_getString == null) return;

            string baseText = _getString("LlmTranslateButtonText", "번역");
            
            string shortCode = _currentTargetLanguage switch
            {
                "Korean" => "KO",
                "English" => "EN",
                "Japanese" => "JP",
                "Chinese" => "ZH",
                "French" => "FR",
                "Spanish" => "ES",
                "German" => "DE",
                _ => "KO"
            };

            LlmTranslateButton.Content = $"{baseText} ({shortCode})";
        }

        public void UpdateInstructionTabs(IReadOnlyList<(string Name, bool IsActive)> tabs, Action<int> onTabClick, Action<int> onTabDelete)
        {
            InstructionTabPanel.Children.Clear();
            for (int i = 0; i < tabs.Count; i++)
            {
                var (name, isActive) = tabs[i];
                int index = i;

                var tabBorder = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 2, 4, 2),
                    BorderThickness = new Thickness(1),
                };

                if (isActive)
                {
                    tabBorder.Background = (Brush)Application.Current.Resources["AccentButtonBackground"];
                    tabBorder.BorderBrush = (Brush)Application.Current.Resources["AccentButtonBackground"];
                }
                else
                {
                    tabBorder.Background = (Brush)Application.Current.Resources["ButtonBackground"];
                    tabBorder.BorderBrush = (Brush)Application.Current.Resources["ButtonBorderBrush"];
                }

                var innerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

                var nameBlock = new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                if (isActive)
                {
                    nameBlock.Foreground = (Brush)Application.Current.Resources["SystemControlForegroundChromeWhiteBrush"];
                }
                else
                {
                    nameBlock.Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"];
                }

                innerStack.Children.Add(nameBlock);

                var closeBtn = new Button
                {
                    Content = "×",
                    FontSize = 10,
                    Width = 18,
                    Height = 18,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                    BorderThickness = new Thickness(0),
                };
                closeBtn.Click += (s, args) => onTabDelete(index);
                innerStack.Children.Add(closeBtn);

                tabBorder.Child = innerStack;

                tabBorder.Tapped += (s, args) => onTabClick(index);

                InstructionTabPanel.Children.Add(tabBorder);
            }
        }
    }
}
