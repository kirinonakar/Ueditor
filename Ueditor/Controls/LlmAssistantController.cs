using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ueditor.Core.Interfaces;
using Ueditor.Core.Models;

namespace Ueditor.Controls
{
    public sealed class LlmAssistantController
    {
        private readonly ILLMService _llmService;
        private readonly ISettingsService _settingsService;
        private readonly ILanguageDetectionService _languageDetectionService;
        private readonly RightSidebarPane _rightSidebar;
        private readonly Func<XamlRoot> _xamlRootProvider;
        private readonly Func<OpenedTab?> _activeTabProvider;
        private readonly Func<OpenedTab, int, string> _getTabText;
        private readonly Func<string, Task<bool>> _insertIntoActiveEditorAsync;
        private readonly Action<string, string> _showError;
        private readonly Func<string, string, string> _getString;

        private string _lastSelectionText = string.Empty;
        private string _fileContextText = string.Empty;
        private string _fileContextDisplay = string.Empty;

        private class CustomInstruction
        {
            public string Name { get; set; } = "";
            public string Prompt { get; set; } = "";
            public string FileContext { get; set; } = "";
            public string FileContextDisplay { get; set; } = "";
        }

        private List<CustomInstruction> _instructions = new();
        private int _activeInstructionIndex = -1;
        private int _instructionNameCounter = 0;

        public LlmAssistantController(
            ILLMService llmService,
            ISettingsService settingsService,
            ILanguageDetectionService languageDetectionService,
            RightSidebarPane rightSidebar,
            Func<XamlRoot> xamlRootProvider,
            Func<OpenedTab?> activeTabProvider,
            Func<OpenedTab, int, string> getTabText,
            Func<string, Task<bool>> insertIntoActiveEditorAsync,
            Action<string, string> showError,
            Func<string, string, string> getString)
        {
            _llmService = llmService;
            _settingsService = settingsService;
            _languageDetectionService = languageDetectionService;
            _rightSidebar = rightSidebar;
            _xamlRootProvider = xamlRootProvider;
            _activeTabProvider = activeTabProvider;
            _getTabText = getTabText;
            _insertIntoActiveEditorAsync = insertIntoActiveEditorAsync;
            _showError = showError;
            _getString = getString;

            WireEvents();

            var initialTargetLang = _settingsService.CurrentSettings?.LlmTargetLanguage ?? "Korean";
            _rightSidebar.UpdateTranslateLanguage(initialTargetLang);
        }

        public void SetSelectionText(string selectedText)
        {
            _lastSelectionText = selectedText ?? string.Empty;
        }

        public void ClearSelection()
        {
            _lastSelectionText = string.Empty;
        }

        public void SetOutput(string message)
        {
            _rightSidebar.LlmOutput.Text = message;
        }

        private void WireEvents()
        {
            _rightSidebar.LlmAddFileContextClick += OnLlmAddFileContextClick;
            _rightSidebar.LlmRemoveFileContextClick += OnLlmRemoveFileContextClick;
            _rightSidebar.LlmExplainClick += OnLlmExplainClick;
            _rightSidebar.LlmSummarizeClick += OnLlmSummarizeClick;
            _rightSidebar.LlmTranslateClick += OnLlmTranslateClick;
            _rightSidebar.LlmImproveClick += OnLlmImproveClick;
            _rightSidebar.LlmCustomClick += OnLlmCustomClick;
            _rightSidebar.LlmInsertOutputClick += OnLlmInsertOutputClick;
            _rightSidebar.LlmTargetLanguageSelected += OnLlmTargetLanguageSelected;
            _rightSidebar.LlmAddInstructionClick += OnLlmAddInstructionClick;
        }

        private string GetActiveSelectionLanguage()
        {
            var activeTab = _activeTabProvider();
            if (activeTab == null)
            {
                return "plaintext";
            }

            if (!string.IsNullOrWhiteSpace(activeTab.Language))
            {
                return activeTab.Language;
            }

            if (!string.IsNullOrWhiteSpace(activeTab.FilePath))
            {
                return _languageDetectionService.GetMonacoLanguageName(activeTab.FilePath);
            }

            return "plaintext";
        }

        private async void OnLlmExplainClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText) && string.IsNullOrEmpty(GetActiveFileContext()))
            {
                _showError(_getString("LlmErrorTitle", "AI 오류"), _getString("LlmNoSelectionCustom", "선택 영역이나 파일 맥락이 없습니다. 텍스트를 선택하거나 파일 맥락을 추가하십시오."));
                return;
            }

            string language = GetActiveSelectionLanguage();
            string context = BuildLlmContext(_lastSelectionText);
            await PreflightCheckAndRunAsync(_getString("LlmActionExplain", "선택 영역 설명 (Explain)"), context,
                () => _llmService.ExplainCodeAsync(context, language));
        }

        private async void OnLlmSummarizeClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText) && string.IsNullOrEmpty(GetActiveFileContext()))
            {
                _showError(_getString("LlmErrorTitle", "AI 오류"), _getString("LlmNoSelectionCustom", "선택 영역이나 파일 맥락이 없습니다. 텍스트를 선택하거나 파일 맥락을 추가하십시오."));
                return;
            }

            string context = BuildLlmContext(_lastSelectionText);
            await PreflightCheckAndRunAsync(_getString("LlmActionSummarize", "선택 영역 요약 (Summarize)"), context,
                () => _llmService.SummarizeTextAsync(context));
        }

        private async void OnLlmTranslateClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText) && string.IsNullOrEmpty(GetActiveFileContext()))
            {
                _showError(_getString("LlmErrorTitle", "AI 오류"), _getString("LlmNoSelectionCustom", "선택 영역이나 파일 맥락이 없습니다. 텍스트를 선택하거나 파일 맥락을 추가하십시오."));
                return;
            }

            string context = BuildLlmContext(_lastSelectionText);
            await PreflightCheckAndRunAsync(_getString("LlmActionTranslate", "선택 영역 번역 (Translate)"), context,
                () => _llmService.TranslateTextAsync(context));
        }

        private async void OnLlmImproveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText) && string.IsNullOrEmpty(GetActiveFileContext()))
            {
                _showError(_getString("LlmErrorTitle", "AI 오류"), _getString("LlmNoSelectionCustom", "선택 영역이나 파일 맥락이 없습니다. 텍스트를 선택하거나 파일 맥락을 추가하십시오."));
                return;
            }

            string context = BuildLlmContext(_lastSelectionText);
            await PreflightCheckAndRunAsync(_getString("LlmActionImprove", "수식 및 마크다운 개선"), context,
                () => _llmService.ImproveTextAsync(context));
        }

        private async void OnLlmCustomClick(object sender, RoutedEventArgs e)
        {
            SaveActivePrompt();

            string prompt = GetActivePrompt();

            if (_activeInstructionIndex < 0 && string.IsNullOrEmpty(prompt))
            {
                _showError(_getString("LlmErrorTitle", "AI 오류"), _getString("LlmEmptyCustomPrompt", "커스텀 지시사항 입력란이 비어 있습니다."));
                return;
            }

            string context = BuildLlmContext(_lastSelectionText);
            await PreflightCheckAndRunAsync(_getString("LlmActionCustom", "커스텀 지시사항 실행"), context,
                () => _llmService.CustomPromptAsync(prompt, context));
        }

        private void OnLlmAddFileContextClick(object sender, RoutedEventArgs e)
        {
            var tab = _activeTabProvider();
            if (tab == null)
            {
                _showError(_getString("LlmFileContextTitle", "AI 파일 맥락"), _getString("LlmNoActiveTabForFileContext", "파일 맥락으로 추가할 활성 탭이 없습니다."));
                return;
            }

            string title = string.IsNullOrWhiteSpace(tab.FilePath) ? tab.Title : tab.FilePath;
            const int maxChars = 120_000;
            string content = _getTabText(tab, maxChars);
            if (content.Length > maxChars)
            {
                content = content.Substring(0, maxChars) + "\n\n[파일 맥락이 길어 앞부분만 포함됨]";
            }

            string fileContext = $"[파일 맥락: {title}]\n{content}";
            string display = $"{Path.GetFileName(title)} · {fileContext.Length:N0} 글자";

            if (_activeInstructionIndex >= 0 && _activeInstructionIndex < _instructions.Count)
            {
                _instructions[_activeInstructionIndex].FileContext = fileContext;
                _instructions[_activeInstructionIndex].FileContextDisplay = display;
            }
            else
            {
                _fileContextText = fileContext;
                _fileContextDisplay = display;
            }

            _rightSidebar.LlmFileContext.Text = display;
        }

        private void OnLlmRemoveFileContextClick(object sender, RoutedEventArgs e)
        {
            if (_activeInstructionIndex >= 0 && _activeInstructionIndex < _instructions.Count)
            {
                _instructions[_activeInstructionIndex].FileContext = string.Empty;
                _instructions[_activeInstructionIndex].FileContextDisplay = string.Empty;
            }
            else
            {
                _fileContextText = string.Empty;
                _fileContextDisplay = string.Empty;
            }

            _rightSidebar.LlmFileContext.Text = string.Empty;
        }

        private async void OnLlmInsertOutputClick(object sender, RoutedEventArgs e)
        {
            string output = _rightSidebar.LlmOutput.SelectedText;
            if (string.IsNullOrEmpty(output))
            {
                output = _rightSidebar.LlmOutput.Text;
            }

            if (string.IsNullOrWhiteSpace(output) || output.StartsWith("대기 중", StringComparison.Ordinal) || output.StartsWith("Waiting...", StringComparison.Ordinal) || output.StartsWith("待機中...", StringComparison.Ordinal))
            {
                _showError(_getString("LlmInsertTitle", "AI 응답 입력"), _getString("LlmNoOutputToInsert", "입력할 AI 응답이 없습니다."));
                return;
            }

            await _insertIntoActiveEditorAsync(output);
        }

        private void OnLlmAddInstructionClick(object sender, RoutedEventArgs e)
        {
            if (_instructions.Count >= 4) return;

            SaveActivePrompt();

            _instructionNameCounter++;
            string defaultPrefix = _getString("LlmInstructionDefaultName", "지시문");
            var name = $"{defaultPrefix} {_instructionNameCounter}";

            _instructions.Add(new CustomInstruction
            {
                Name = name,
                Prompt = string.Empty,
                FileContext = string.Empty,
                FileContextDisplay = string.Empty
            });
            _activeInstructionIndex = _instructions.Count - 1;

            LoadActiveInstruction();
            RebuildInstructionTabsUI();
        }

        private void OnInstructionTabClick(int index)
        {
            if (index < 0 || index >= _instructions.Count) return;

            SaveActivePrompt();
            _activeInstructionIndex = index;
            LoadActiveInstruction();
            RebuildInstructionTabsUI();
        }

        private void OnInstructionTabDelete(int index)
        {
            if (index < 0 || index >= _instructions.Count) return;

            _instructions.RemoveAt(index);

            if (_activeInstructionIndex >= _instructions.Count)
            {
                _activeInstructionIndex = _instructions.Count - 1;
            }
            else if (_activeInstructionIndex == index)
            {
                _activeInstructionIndex = Math.Min(index, _instructions.Count - 1);
            }
            else if (_activeInstructionIndex > index)
            {
                _activeInstructionIndex--;
            }

            if (_instructions.Count == 0)
            {
                _activeInstructionIndex = -1;
                _instructionNameCounter = 0;
            }

            LoadActiveInstruction();
            RebuildInstructionTabsUI();
        }

        private void SaveActivePrompt()
        {
            if (_activeInstructionIndex >= 0 && _activeInstructionIndex < _instructions.Count)
            {
                _instructions[_activeInstructionIndex].Prompt = _rightSidebar.LlmCustomPrompt.Text;
            }
        }

        private void LoadActiveInstruction()
        {
            if (_activeInstructionIndex >= 0 && _activeInstructionIndex < _instructions.Count)
            {
                var inst = _instructions[_activeInstructionIndex];
                _rightSidebar.LlmCustomPrompt.Text = inst.Prompt;
                _rightSidebar.LlmFileContext.Text = inst.FileContextDisplay;
            }
            else
            {
                _rightSidebar.LlmFileContext.Text = _fileContextDisplay;
            }
        }

        private void RebuildInstructionTabsUI()
        {
            var tabs = _instructions.Select((inst, idx) => (inst.Name, IsActive: idx == _activeInstructionIndex)).ToList();
            _rightSidebar.UpdateInstructionTabs(tabs, OnInstructionTabClick, OnInstructionTabDelete);

            var hasTabs = _instructions.Count > 0;
            _rightSidebar.InstructionTabScroller.Visibility = hasTabs ? Visibility.Visible : Visibility.Collapsed;
        }

        private string GetActivePrompt()
        {
            if (_activeInstructionIndex >= 0 && _activeInstructionIndex < _instructions.Count)
            {
                return _instructions[_activeInstructionIndex].Prompt;
            }
            return _rightSidebar.LlmCustomPrompt.Text;
        }

        private string GetActiveFileContext()
        {
            if (_activeInstructionIndex >= 0 && _activeInstructionIndex < _instructions.Count)
            {
                return _instructions[_activeInstructionIndex].FileContext;
            }
            return _fileContextText;
        }

        private string BuildLlmContext(string selectedText)
        {
            string fileContext = GetActiveFileContext();

            if (string.IsNullOrEmpty(fileContext))
            {
                return selectedText;
            }

            if (string.IsNullOrEmpty(selectedText))
            {
                return fileContext;
            }

            return $"{fileContext}\n\n[선택 영역]\n{selectedText}";
        }

        private async Task PreflightCheckAndRunAsync(string actionName, string contentText, Func<Task<string>> llmCall)
        {
            if (_settingsService.CurrentSettings.LlmConfirmBeforeSending)
            {
                var textPreview = contentText.Length > 200 ? contentText.Substring(0, 200) + "..." : contentText;

                string format = _getString("LlmPreflightContentFormat", "액션: {0}\n\n전송될 AI 공급자: {1} ({2})\n전송 텍스트 크기: {3:N0} 자 (약 {4:N0} 토큰 소모)\n\n[전송 내용 미리보기]\n{5}\n\n보안상의 문제나 의도하지 않은 토큰 대량 유실이 없는지 확인 후 전송해 주십시오.");
                string dialogContent = string.Format(format,
                    actionName,
                    _settingsService.CurrentSettings.LlmProvider,
                    _settingsService.CurrentSettings.LlmModel,
                    contentText.Length,
                    contentText.Length / 4,
                    textPreview);

                var dialog = new ContentDialog
                {
                    Title = _getString("LlmPreflightTitle", "AI 전송 사전 확인 (Pre-flight Check)"),
                    Content = dialogContent,
                    PrimaryButtonText = _getString("LlmPreflightApprove", "API 전송 승인"),
                    CloseButtonText = _getString("LlmPreflightCancel", "취소"),
                    XamlRoot = _xamlRootProvider()
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    return;
                }
            }

            _rightSidebar.LlmOutput.Text = _getString("LlmRunningMessage", "AI 분석 및 응답 생성이 비동기 구동 중입니다. 잠시만 대기해 주십시오...");
            _rightSidebar.RightTabs.SelectedIndex = 1;

            try
            {
                _rightSidebar.LlmOutput.Text = await llmCall();
            }
            catch (Exception ex)
            {
                string exceptionFormat = _getString("LlmExceptionFormat", "AI 실행 도중 예외가 터졌습니다: {0}");
                _rightSidebar.LlmOutput.Text = string.Format(exceptionFormat, ex.Message);
            }
        }

        private async void OnLlmTargetLanguageSelected(string targetLanguage)
        {
            var settings = _settingsService.CurrentSettings;
            if (settings.LlmTargetLanguage == targetLanguage)
            {
                return;
            }

            settings.LlmTargetLanguage = targetLanguage;
            await _settingsService.SaveSettingsAsync(settings);

            _rightSidebar.UpdateTranslateLanguage(targetLanguage);
        }
    }
}
