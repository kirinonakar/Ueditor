using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ueditor.Core.Interfaces;
using Ueditor.Core.Models;

namespace Ueditor.Core.Services
{
    public sealed class SettingsDialogService : ISettingsDialogService
    {
        private sealed record ToolbarOrderItem(string Id, string Label);

        private static IReadOnlyList<string>? _installedFontFamiliesCache;
        private readonly ILLMService _llmService;

        public SettingsDialogService(ILLMService llmService)
        {
            _llmService = llmService;
        }

        public async Task<SettingsDialogResult> ShowAsync(
            EditorSettings settings,
            XamlRoot xamlRoot,
            Func<string, string, string> getString)
        {
            var languageCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            languageCombo.Items.Add(getString("LanguageDefault", "Default (OS Language)"));
            languageCombo.Items.Add(getString("LanguageKorean", "한국어"));
            languageCombo.Items.Add(getString("LanguageEnglish", "English"));
            languageCombo.Items.Add(getString("LanguageJapanese", "日本語"));

            languageCombo.SelectedIndex = settings.Language switch
            {
                "ko-KR" => 1,
                "en-US" => 2,
                "ja-JP" => 3,
                _ => 0
            };

            var themeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = settings.Theme == "Dark" ? 0 : 1 };
            themeCombo.Items.Add("Dark Theme (vs-dark)");
            themeCombo.Items.Add("Light Theme (vs)");

            var sizeSlider = new Slider { Minimum = 10, Maximum = 24, Value = settings.FontSize, StepFrequency = 1 };

            var fontFamilies = GetInstalledFontFamilies();
            var fontFamilyCombo = CreateFontComboBox(settings.FontFamily, fontFamilies);
            var uiFontFamilyCombo = CreateFontComboBox(settings.UiFontFamily, fontFamilies);
            var customBgCheck = new CheckBox { Content = getString("SettingsUseCustomBg", "커스텀 에디터 배경색 사용"), IsChecked = !string.IsNullOrWhiteSpace(settings.CustomBackgroundColor) };
            var customFgCheck = new CheckBox { Content = getString("SettingsUseCustomFg", "커스텀 에디터 글자색 사용"), IsChecked = !string.IsNullOrWhiteSpace(settings.CustomForegroundColor) };
            var customBgDropdown = CreateColorDropdown(getString("SettingsUseCustomBg", "에디터 배경색"), ResolvePickerColor(settings.CustomBackgroundColor, settings.Theme == "Light" ? "#ffffff" : "#1e1e1e"), out var customBgPicker);
            var customFgDropdown = CreateColorDropdown(getString("SettingsUseCustomFg", "에디터 글자색"), ResolvePickerColor(settings.CustomForegroundColor, settings.Theme == "Light" ? "#111111" : "#d4d4d4"), out var customFgPicker);
            customBgDropdown.IsEnabled = customBgCheck.IsChecked == true;
            customFgDropdown.IsEnabled = customFgCheck.IsChecked == true;
            customBgCheck.Checked += (_, __) => customBgDropdown.IsEnabled = true;
            customBgCheck.Unchecked += (_, __) => customBgDropdown.IsEnabled = false;
            customFgCheck.Checked += (_, __) => customFgDropdown.IsEnabled = true;
            customFgCheck.Unchecked += (_, __) => customFgDropdown.IsEnabled = false;

            var wordWrapCheck = new CheckBox { Content = getString("SettingsWordWrap", "기본 Word Wrap 켜기"), IsChecked = settings.WordWrap };
            var minimapCheck = new CheckBox { Content = getString("SettingsMinimap", "미니맵 표시 (로컬 Monaco 번들 사용 시)"), IsChecked = settings.MinimapEnabled };
            var bracketPairCheck = new CheckBox { Content = getString("SettingsBracketPair", "Bracket pair colorization (로컬 Monaco 번들 사용 시)"), IsChecked = settings.BracketPairColorizationEnabled };
            var autocompleteCheck = new CheckBox { Content = getString("SettingsAutocomplete", "자동완성 기능 사용"), IsChecked = settings.AutocompleteEnabled };
            var autoSaveCheck = new CheckBox { Content = getString("SettingsAutoSave", "Autosave 사용"), IsChecked = settings.AutoSave };
            var defaultMarkdownCheck = new CheckBox { Content = getString("SettingsLivePreview", "실시간 미리보기 기본 활성화"), IsChecked = settings.DefaultMarkdownEnabled };
            var defaultMarkdownToolbarCheck = new CheckBox { Content = getString("SettingsMarkdownToolbar", "기본 마크다운 툴바 활성화"), IsChecked = settings.DefaultMarkdownToolbarEnabled };
            var tabSizeBox = new TextBox { PlaceholderText = "예: 4", Text = settings.TabSize.ToString(), HorizontalAlignment = HorizontalAlignment.Stretch };

            string[] providerNames = { "Gemini", "OpenAI", "LM Studio" };
            int providerIndex = Array.FindIndex(providerNames, p => p.Equals(settings.LlmProvider, StringComparison.OrdinalIgnoreCase));
            if (providerIndex < 0) providerIndex = 1;

            var llmProviderCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var providerName in providerNames)
            {
                llmProviderCombo.Items.Add(providerName);
            }
            llmProviderCombo.SelectedIndex = providerIndex;

            var llmEndpointBox = new TextBox { PlaceholderText = "예: http://localhost:1234/v1", Text = settings.LlmEndpoint, HorizontalAlignment = HorizontalAlignment.Stretch };
            var llmModelCombo = new ComboBox { PlaceholderText = "모델 선택", HorizontalAlignment = HorizontalAlignment.Stretch };
            var llmApiKeyBox = new PasswordBox { PasswordChar = "●", PlaceholderText = "API Key 입력 (비워두면 저장된 Key 삭제)", HorizontalAlignment = HorizontalAlignment.Stretch };
            llmApiKeyBox.Password = await _llmService.GetApiKeyAsync(providerNames[providerIndex]);

            var sourceLangCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            sourceLangCombo.Items.Add(getString("LlmLangAutoDetect", "자동 감지 (Auto Detect)"));
            sourceLangCombo.Items.Add(getString("LlmLangKorean", "한국어 (Korean)"));
            sourceLangCombo.Items.Add(getString("LlmLangEnglish", "영어 (English)"));
            sourceLangCombo.Items.Add(getString("LlmLangJapanese", "일본어 (Japanese)"));
            sourceLangCombo.Items.Add(getString("LlmLangChinese", "중국어 (Chinese)"));
            sourceLangCombo.Items.Add(getString("LlmLangFrench", "프랑스어 (French)"));
            sourceLangCombo.Items.Add(getString("LlmLangSpanish", "스페인어 (Spanish)"));
            sourceLangCombo.Items.Add(getString("LlmLangGerman", "독일어 (German)"));

            sourceLangCombo.SelectedIndex = settings.LlmSourceLanguage switch
            {
                "Korean" => 1,
                "English" => 2,
                "Japanese" => 3,
                "Chinese" => 4,
                "French" => 5,
                "Spanish" => 6,
                "German" => 7,
                _ => 0
            };

            var targetLangCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            targetLangCombo.Items.Add(getString("LlmLangKorean", "한국어 (Korean)"));
            targetLangCombo.Items.Add(getString("LlmLangEnglish", "영어 (English)"));
            targetLangCombo.Items.Add(getString("LlmLangJapanese", "일본어 (Japanese)"));
            targetLangCombo.Items.Add(getString("LlmLangChinese", "중국어 (Chinese)"));
            targetLangCombo.Items.Add(getString("LlmLangFrench", "프랑스어 (French)"));
            targetLangCombo.Items.Add(getString("LlmLangSpanish", "스페인어 (Spanish)"));
            targetLangCombo.Items.Add(getString("LlmLangGerman", "독일어 (German)"));

            targetLangCombo.SelectedIndex = settings.LlmTargetLanguage switch
            {
                "English" => 1,
                "Japanese" => 2,
                "Chinese" => 3,
                "French" => 4,
                "Spanish" => 5,
                "German" => 6,
                _ => 0
            };

            var refreshLmStudioModelsButton = new Button { Content = getString("SettingsLlmLoadModels", "LM Studio 모델 불러오기"), HorizontalAlignment = HorizontalAlignment.Stretch };
            var llmModelStatusText = new TextBlock
            {
                Text = getString("SettingsLlmInfo", "LM Studio는 서버가 켜져 있을 때 http://localhost:1234/v1/models 에서 모델 목록을 불러옵니다."),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11
            };

            string GetSelectedProviderName() => llmProviderCombo.SelectedItem as string ?? "OpenAI";

            void AddModelChoice(string model)
            {
                if (!string.IsNullOrWhiteSpace(model) && !llmModelCombo.Items.Contains(model))
                {
                    llmModelCombo.Items.Add(model);
                }
            }

            void SelectModelChoice(string model)
            {
                AddModelChoice(model);
                if (!string.IsNullOrWhiteSpace(model))
                {
                    llmModelCombo.SelectedItem = model;
                }
                else if (llmModelCombo.Items.Count > 0)
                {
                    llmModelCombo.SelectedIndex = 0;
                }
            }

            void PopulateModelChoices(string provider, string selectedModel)
            {
                llmModelCombo.Items.Clear();

                if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                {
                    AddModelChoice("gemini-flash-lite-latest");
                    AddModelChoice("gemini-flash-latest");
                    AddModelChoice("gemini-pro-latest");
                    AddModelChoice("gemma-4-26b-a4b-it");
                    AddModelChoice("gemma-4-31b-it");

                    string target = !string.IsNullOrEmpty(settings.LlmModelGemini) ? settings.LlmModelGemini : selectedModel;
                    if (string.IsNullOrEmpty(target) ||
                        (!target.Equals("gemini-flash-lite-latest", StringComparison.OrdinalIgnoreCase) &&
                         !target.Equals("gemini-flash-latest", StringComparison.OrdinalIgnoreCase) &&
                         !target.Equals("gemini-pro-latest", StringComparison.OrdinalIgnoreCase) &&
                         !target.Equals("gemma-4-26b-a4b-it", StringComparison.OrdinalIgnoreCase) &&
                         !target.Equals("gemma-4-31b-it", StringComparison.OrdinalIgnoreCase)))
                    {
                        target = "gemini-flash-lite-latest";
                    }
                    SelectModelChoice(target);
                }
                else if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                {
                    AddModelChoice("gpt-5.5");
                    string target = !string.IsNullOrEmpty(settings.LlmModelOpenAI) ? settings.LlmModelOpenAI : selectedModel;
                    if (string.IsNullOrEmpty(target) || !target.Equals("gpt-5.5", StringComparison.OrdinalIgnoreCase))
                    {
                        target = "gpt-5.5";
                    }
                    SelectModelChoice(target);
                }
                else if (provider.Equals("LM Studio", StringComparison.OrdinalIgnoreCase))
                {
                    string target = !string.IsNullOrEmpty(settings.LlmModelLmStudio) ? settings.LlmModelLmStudio : selectedModel;
                    SelectModelChoice(target);
                }
            }

            bool IsKnownDefaultEndpoint(string endpoint)
            {
                return string.IsNullOrWhiteSpace(endpoint) ||
                       endpoint.Equals("https://api.openai.com/v1", StringComparison.OrdinalIgnoreCase) ||
                       endpoint.Equals("http://localhost:1234/v1", StringComparison.OrdinalIgnoreCase) ||
                       endpoint.Equals("https://generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);
            }

            void ApplyProviderDefaults(string provider)
            {
                if (!IsKnownDefaultEndpoint(llmEndpointBox.Text.Trim()))
                {
                    return;
                }

                llmEndpointBox.Text = provider switch
                {
                    "LM Studio" => "http://localhost:1234/v1",
                    "OpenAI" => "https://api.openai.com/v1",
                    "Gemini" => "https://generativelanguage.googleapis.com",
                    _ => llmEndpointBox.Text
                };
            }

            async Task RefreshLmStudioModelsAsync()
            {
                if (!GetSelectedProviderName().Equals("LM Studio", StringComparison.OrdinalIgnoreCase))
                {
                    llmModelStatusText.Text = "LM Studio 공급자를 선택하면 로컬 모델 목록을 불러올 수 있습니다.";
                    return;
                }

                try
                {
                    refreshLmStudioModelsButton.IsEnabled = false;
                    llmModelStatusText.Text = "LM Studio 모델 목록을 불러오는 중입니다...";
                    var models = await FetchLmStudioModelsAsync(llmEndpointBox.Text.Trim());

                    llmModelCombo.Items.Clear();
                    foreach (var model in models)
                    {
                        AddModelChoice(model);
                    }

                    string targetModel = !string.IsNullOrEmpty(settings.LlmModelLmStudio) ? settings.LlmModelLmStudio : settings.LlmModel;
                    SelectModelChoice(models.Contains(targetModel) ? targetModel : models.FirstOrDefault() ?? targetModel);
                    llmModelStatusText.Text = models.Count > 0
                        ? $"{models.Count}개 모델을 불러왔습니다."
                        : "LM Studio에서 사용 가능한 모델을 찾지 못했습니다.";
                }
                catch (Exception ex)
                {
                    string targetModel = !string.IsNullOrEmpty(settings.LlmModelLmStudio) ? settings.LlmModelLmStudio : settings.LlmModel;
                    SelectModelChoice(targetModel);
                    llmModelStatusText.Text = $"LM Studio 모델 목록을 불러오지 못했습니다: {ex.Message}";
                }
                finally
                {
                    refreshLmStudioModelsButton.IsEnabled = true;
                }
            }

            PopulateModelChoices(GetSelectedProviderName(), settings.LlmModel);

            llmProviderCombo.SelectionChanged += async (_, __) =>
            {
                string provider = GetSelectedProviderName();
                ApplyProviderDefaults(provider);

                string targetModel = settings.LlmModel;
                if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                {
                    targetModel = !string.IsNullOrEmpty(settings.LlmModelGemini) ? settings.LlmModelGemini : "gemini-flash-lite-latest";
                }
                else if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                {
                    targetModel = !string.IsNullOrEmpty(settings.LlmModelOpenAI) ? settings.LlmModelOpenAI : "gpt-5.5";
                }
                else if (provider.Equals("LM Studio", StringComparison.OrdinalIgnoreCase))
                {
                    targetModel = !string.IsNullOrEmpty(settings.LlmModelLmStudio) ? settings.LlmModelLmStudio : "";
                }

                PopulateModelChoices(provider, targetModel);

                if (provider.Equals("LM Studio", StringComparison.OrdinalIgnoreCase))
                {
                    llmModelStatusText.Text = "LM Studio 모델 목록을 불러오는 중...";
                    _ = RefreshLmStudioModelsAsync();
                }

                llmApiKeyBox.Password = await _llmService.GetApiKeyAsync(provider);
            };

            refreshLmStudioModelsButton.Click += async (_, __) => await RefreshLmStudioModelsAsync();

            var appearanceSection = CreateSection();
            AddLabel(appearanceSection, getString("SettingsLanguage", "애플리케이션 언어 (Language)"));
            appearanceSection.Children.Add(languageCombo);
            AddLabel(appearanceSection, getString("SettingsTheme", "앱/에디터 테마"));
            appearanceSection.Children.Add(themeCombo);
            AddLabel(appearanceSection, getString("SettingsFontSize", "에디터 글자 크기") + $" ({settings.FontSize:0}pt)");
            appearanceSection.Children.Add(sizeSlider);
            AddLabel(appearanceSection, getString("SettingsFontFamily", "에디터 폰트"));
            appearanceSection.Children.Add(fontFamilyCombo);
            AddLabel(appearanceSection, getString("SettingsUiFontFamily", "UI 쉘 폰트"));
            appearanceSection.Children.Add(uiFontFamilyCombo);
            appearanceSection.Children.Add(customBgCheck);
            appearanceSection.Children.Add(customBgDropdown);
            appearanceSection.Children.Add(customFgCheck);
            appearanceSection.Children.Add(customFgDropdown);

            var editorSection = CreateSection();
            editorSection.Children.Add(wordWrapCheck);
            editorSection.Children.Add(minimapCheck);
            editorSection.Children.Add(bracketPairCheck);
            editorSection.Children.Add(autocompleteCheck);
            editorSection.Children.Add(autoSaveCheck);
            editorSection.Children.Add(defaultMarkdownCheck);
            editorSection.Children.Add(defaultMarkdownToolbarCheck);
            AddLabel(editorSection, getString("SettingsTabSize", "Tab size"));
            editorSection.Children.Add(tabSizeBox);

            var llmSection = CreateSection();
            AddLabel(llmSection, getString("SettingsLlmProvider", "LLM 공급자"));
            llmSection.Children.Add(llmProviderCombo);
            AddLabel(llmSection, getString("SettingsLlmEndpoint", "LLM API Endpoint"));
            llmSection.Children.Add(llmEndpointBox);

            AddLabel(llmSection, getString("SettingsLlmApiKey", "LLM API Key"));
            llmSection.Children.Add(llmApiKeyBox);
            llmSection.Children.Add(new TextBlock
            {
                Text = getString("SettingsLlmApiKeyInfo", "API Key는 설정 파일에 저장하지 않고 Windows 자격 증명 관리자에 저장합니다. 비워두고 저장하면 기존 Key를 유지합니다. LM Studio는 기본 로컬 서버 설정에서 API Key 없이 사용할 수 있습니다."),
                TextWrapping = TextWrapping.Wrap
            });

            AddLabel(llmSection, getString("SettingsLlmModel", "LLM 모델명"));
            llmSection.Children.Add(llmModelCombo);
            llmSection.Children.Add(refreshLmStudioModelsButton);
            llmSection.Children.Add(llmModelStatusText);

            AddLabel(llmSection, getString("SettingsLlmSourceLanguage", "번역 원본 언어 (Source Language)"));
            llmSection.Children.Add(sourceLangCombo);
            AddLabel(llmSection, getString("SettingsLlmTargetLanguage", "번역 대상 언어 (Target Language)"));
            llmSection.Children.Add(targetLangCombo);

            var settingsPivot = new Pivot { Width = 500, Height = 440, FontSize = 12 };
            var toolbarSection = CreateSection();
            var showLabelsCheck = new CheckBox { Content = getString("SettingsToolbarShowLabels", "툴바 버튼 글자 표시"), IsChecked = settings.ToolbarShowLabels };
            toolbarSection.Children.Add(showLabelsCheck);

            var visibilityHeader = new TextBlock
            {
                Text = getString("SettingsToolbarButtonVisibility", "툴바 버튼 표시/숨기기"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 2)
            };
            toolbarSection.Children.Add(visibilityHeader);

            var toolbarOptions = ToolbarButtonCatalog.All.ToList();
            var hiddenSet = new HashSet<string>(
                (settings.ToolbarHiddenButtons ?? new List<string>())
                    .Select(ToolbarButtonCatalog.NormalizeId),
                StringComparer.OrdinalIgnoreCase);
            var visibilityChecks = new List<CheckBox>();
            foreach (var option in toolbarOptions.Where(option => !option.IsRequired))
            {
                string label = getString(option.ResourceKey, option.Id);
                var chk = new CheckBox
                {
                    Content = label,
                    Tag = option.Id,
                    IsChecked = !hiddenSet.Contains(option.Id),
                    Margin = new Thickness(12, 0, 0, 0)
                };
                visibilityChecks.Add(chk);
                toolbarSection.Children.Add(chk);
            }
            var settingsNote = new TextBlock
            {
                Text = getString("SettingsToolbarSettingsPinned", "설정 버튼은 항상 표시됩니다."),
                FontSize = 11,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                Margin = new Thickness(12, 0, 0, 4)
            };
            toolbarSection.Children.Add(settingsNote);

            var reorderDesc = new TextBlock
            {
                Text = getString("SettingsToolbarDragHint", "드래그하여 버튼 순서 변경 (설정 버튼은 고정)"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 4)
            };
            toolbarSection.Children.Add(reorderDesc);
            var defaultOrder = NormalizeToolbarOrder(settings.ToolbarButtonOrder);
            var orderItems = new ObservableCollection<ToolbarOrderItem>(defaultOrder
                .Select(id => toolbarOptions.First(option => option.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                .Select(option => new ToolbarOrderItem(option.Id, getString(option.ResourceKey, option.Id))));
            var orderList = new ListView
            {
                Height = 240,
                SelectionMode = ListViewSelectionMode.None,
                AllowDrop = true,
                CanReorderItems = true,
                ItemsSource = orderItems
            };

            orderList.ItemTemplate = (Microsoft.UI.Xaml.DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                @"<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                    <TextBlock Text=""{Binding Label}"" FontSize=""11"" Height=""18"" VerticalAlignment=""Center""/>
                  </DataTemplate>"
            );

            orderList.ItemContainerStyle = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                @"<Style TargetType=""ListViewItem"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                    <Setter Property=""MinHeight"" Value=""22""/>
                    <Setter Property=""Height"" Value=""22""/>
                    <Setter Property=""Padding"" Value=""8,1,8,1""/>
                  </Style>"
            );

            toolbarSection.Children.Add(orderList);

            var aboutSection = CreateSection();
            aboutSection.HorizontalAlignment = HorizontalAlignment.Stretch;
            aboutSection.Spacing = 12;
            aboutSection.Padding = new Thickness(16, 20, 16, 16);

            var iconImage = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Ueditor.png")),
                Width = 80,
                Height = 80,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            aboutSection.Children.Add(iconImage);

            string appVersion = GetAppVersion();
            var titleText = new TextBlock
            {
                Text = $"Ueditor (v{appVersion})",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            aboutSection.Children.Add(titleText);

            var descText = new TextBlock
            {
                Text = getString("SettingsAboutDescription", "강력하고 가벼운 텍스트 및 마크다운 에디터입니다.\n실시간 미리보기, 코드 및 수식 템플릿, 터미널 인터페이스, Git 통합, AI Assistant 등을 지원합니다."),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                Margin = new Thickness(0, 4, 0, 16)
            };
            aboutSection.Children.Add(descText);

            var separator = new Border
            {
                Height = 1,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(40, 128, 128, 128)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(20, 0, 20, 12)
            };
            aboutSection.Children.Add(separator);

            var githubHeader = new TextBlock
            {
                Text = getString("SettingsAboutProjectGitHub", "Project GitHub"),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            aboutSection.Children.Add(githubHeader);

            var githubLink = new HyperlinkButton
            {
                Content = "https://github.com/kirinonakar/Ueditor",
                NavigateUri = new Uri("https://github.com/kirinonakar/Ueditor"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 0, 12)
            };
            aboutSection.Children.Add(githubLink);

            var copyrightText = new TextBlock
            {
                Text = "Copyright © 2026 kirinonakar. All rights reserved.",
                FontSize = 10,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            aboutSection.Children.Add(copyrightText);

            settingsPivot.Items.Add(new PivotItem { Header = new TextBlock { Text = getString("SettingsAppearance", "모양"), FontSize = 13 }, Content = new ScrollViewer { Content = appearanceSection } });
            settingsPivot.Items.Add(new PivotItem { Header = new TextBlock { Text = getString("SettingsEditing", "편집"), FontSize = 13 }, Content = new ScrollViewer { Content = editorSection } });
            settingsPivot.Items.Add(new PivotItem { Header = new TextBlock { Text = getString("SettingsToolbarCustomization", "툴바"), FontSize = 13 }, Content = new ScrollViewer { Content = toolbarSection } });
            settingsPivot.Items.Add(new PivotItem { Header = new TextBlock { Text = getString("SettingsLLM", "LLM"), FontSize = 13 }, Content = new ScrollViewer { Content = llmSection } });
            settingsPivot.Items.Add(new PivotItem { Header = new TextBlock { Text = getString("SettingsAbout", "정보"), FontSize = 13 }, Content = new ScrollViewer { Content = aboutSection } });

            ApplyCompactStyleToLogicalTree(settingsPivot);

            // Re-apply custom font sizes for About tab to prevent them being flattened by compact style
            titleText.FontSize = 18;
            descText.FontSize = 11;
            copyrightText.FontSize = 9.5;

            var dialog = new ContentDialog
            {
                Title = getString("SettingsTitle", "Ueditor 설정"),
                Content = settingsPivot,
                PrimaryButtonText = getString("SettingsSave", "적용 및 저장"),
                CloseButtonText = getString("SettingsCancel", "취소"),
                XamlRoot = xamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return new SettingsDialogResult { Saved = false };
            }

            settings.Language = languageCombo.SelectedIndex switch
            {
                1 => "ko-KR",
                2 => "en-US",
                3 => "ja-JP",
                _ => "Default"
            };

            settings.Theme = themeCombo.SelectedIndex == 0 ? "Dark" : "Light";
            settings.FontSize = sizeSlider.Value;
            settings.CustomBackgroundColor = customBgCheck.IsChecked == true ? ColorToHex(customBgPicker.Color) : string.Empty;
            settings.CustomForegroundColor = customFgCheck.IsChecked == true ? ColorToHex(customFgPicker.Color) : string.Empty;
            settings.FontFamily = GetSelectedComboText(fontFamilyCombo, settings.FontFamily);
            settings.UiFontFamily = GetSelectedComboText(uiFontFamilyCombo, settings.UiFontFamily);
            settings.WordWrap = wordWrapCheck.IsChecked == true;
            settings.MinimapEnabled = minimapCheck.IsChecked == true;
            settings.BracketPairColorizationEnabled = bracketPairCheck.IsChecked == true;
            settings.AutocompleteEnabled = autocompleteCheck.IsChecked == true;
            settings.AutoSave = autoSaveCheck.IsChecked == true;
            if (int.TryParse(tabSizeBox.Text.Trim(), out int tabSize))
            {
                settings.TabSize = Math.Clamp(tabSize, 1, 16);
            }

            settings.LlmProvider = GetSelectedProviderName();
            settings.LlmEndpoint = llmEndpointBox.Text.Trim();
            settings.LlmModel = (llmModelCombo.SelectedItem as string ?? settings.LlmModel).Trim();

            settings.LlmSourceLanguage = sourceLangCombo.SelectedIndex switch
            {
                1 => "Korean",
                2 => "English",
                3 => "Japanese",
                4 => "Chinese",
                5 => "French",
                6 => "Spanish",
                7 => "German",
                _ => "Auto"
            };

            settings.LlmTargetLanguage = targetLangCombo.SelectedIndex switch
            {
                1 => "English",
                2 => "Japanese",
                3 => "Chinese",
                4 => "French",
                5 => "Spanish",
                6 => "German",
                _ => "Korean"
            };

            if (settings.LlmProvider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                settings.LlmModelGemini = settings.LlmModel;
            }
            else if (settings.LlmProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                settings.LlmModelOpenAI = settings.LlmModel;
            }
            else if (settings.LlmProvider.Equals("LM Studio", StringComparison.OrdinalIgnoreCase))
            {
                settings.LlmModelLmStudio = settings.LlmModel;
            }

            settings.DefaultMarkdownEnabled = defaultMarkdownCheck.IsChecked == true;
            settings.RightSidebarVisible = settings.DefaultMarkdownEnabled;
            settings.DefaultMarkdownToolbarEnabled = defaultMarkdownToolbarCheck.IsChecked == true;

            settings.ToolbarShowLabels = showLabelsCheck.IsChecked == true;
            settings.ToolbarButtonOrder = (orderList.ItemsSource as ObservableCollection<ToolbarOrderItem>)?
                .Select(item => item.Id)
                .ToList()
                ?? ToolbarButtonCatalog.DefaultOrder.ToList();
            settings.ToolbarHiddenButtons = visibilityChecks
                .Where(check => check.IsChecked == false)
                .Select(check => check.Tag as string ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            string newApiKey = llmApiKeyBox.Password.Trim();
            await _llmService.SaveApiKeyAsync(settings.LlmProvider, newApiKey);
            string apiKeyStatus = string.IsNullOrEmpty(newApiKey)
                ? $"{settings.LlmProvider} API Key가 Windows 자격 증명 저장소에서 삭제되었습니다."
                : $"{settings.LlmProvider} API Key가 Windows 자격 증명 저장소에 저장되었습니다.";

            return new SettingsDialogResult
            {
                Saved = true,
                ApiKeyStatusMessage = apiKeyStatus
            };
        }

        private static StackPanel CreateSection()
        {
            return new StackPanel { Spacing = 6, Width = 460, Padding = new Thickness(2, 6, 2, 2) };
        }

        private static List<string> NormalizeToolbarOrder(IReadOnlyList<string>? savedOrder)
        {
            var validIds = new HashSet<string>(
                ToolbarButtonCatalog.DefaultOrder,
                StringComparer.OrdinalIgnoreCase);
            var orderedIds = new List<string>();

            foreach (string rawId in savedOrder ?? Array.Empty<string>())
            {
                string id = ToolbarButtonCatalog.NormalizeId(rawId);
                if (validIds.Contains(id) && !orderedIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    orderedIds.Add(id);
                }
            }

            foreach (string id in ToolbarButtonCatalog.DefaultOrder)
            {
                if (!orderedIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    orderedIds.Add(id);
                }
            }

            return orderedIds;
        }

        private static void AddLabel(StackPanel target, string text)
        {
            target.Children.Add(new TextBlock { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        }

        private static ComboBox CreateFontComboBox(string currentFontFamily, IReadOnlyList<string> fontFamilies)
        {
            var comboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = "폰트 선택"
            };

            string current = string.IsNullOrWhiteSpace(currentFontFamily)
                ? "Consolas"
                : currentFontFamily.Trim();

            if (!fontFamilies.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                comboBox.Items.Add(current);
            }

            foreach (string family in fontFamilies)
            {
                comboBox.Items.Add(family);
            }

            comboBox.SelectedItem = comboBox.Items
                .OfType<string>()
                .FirstOrDefault(item => item.Equals(current, StringComparison.OrdinalIgnoreCase))
                ?? comboBox.Items.OfType<string>().FirstOrDefault();

            return comboBox;
        }

        private static string GetSelectedComboText(ComboBox comboBox, string fallback)
        {
            return (comboBox.SelectedItem as string)?.Trim() ?? fallback.Trim();
        }

        private static DropDownButton CreateColorDropdown(string title, Windows.UI.Color initialColor, out ColorPicker colorPicker)
        {
            var swatch = new Border
            {
                Width = 120,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(120, 128, 128, 128)),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(initialColor)
            };

            var picker = new ColorPicker
            {
                Color = initialColor,
                IsAlphaEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsMoreButtonVisible = false
            };
            colorPicker = picker;

            var flyoutContent = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Spacing = 6,
                Padding = new Thickness(6)
            };
            flyoutContent.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
            flyoutContent.Children.Add(picker);

            ApplyCompactStyleToLogicalTree(flyoutContent);

            picker.Loaded += (s, e) =>
            {
                ApplyCompactStyleToVisualTree(picker);
            };

            picker.ColorChanged += (_, __) =>
            {
                swatch.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(picker.Color);
            };

            var flyoutStyle = new Microsoft.UI.Xaml.Style(typeof(Microsoft.UI.Xaml.Controls.FlyoutPresenter));
            flyoutStyle.Setters.Add(new Microsoft.UI.Xaml.Setter(Microsoft.UI.Xaml.Controls.Control.PaddingProperty, new Thickness(8)));
            flyoutStyle.Setters.Add(new Microsoft.UI.Xaml.Setter(Microsoft.UI.Xaml.Controls.Control.MinWidthProperty, 360.0));
            flyoutStyle.Setters.Add(new Microsoft.UI.Xaml.Setter(Microsoft.UI.Xaml.Controls.Control.MaxWidthProperty, 400.0));

            return new DropDownButton
            {
                Content = swatch,
                Flyout = new Flyout 
                { 
                    Content = flyoutContent,
                    FlyoutPresenterStyle = flyoutStyle
                },
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        private static IReadOnlyList<string> GetInstalledFontFamilies()
        {
            if (_installedFontFamiliesCache != null)
            {
                return _installedFontFamiliesCache;
            }

            var fonts = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase)
            {
                "Consolas",
                "Courier New",
                "Segoe UI",
                "Malgun Gothic"
            };

            AddFontsFromRegistry(fonts, Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"));
            AddFontsFromRegistry(fonts, Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"));

            _installedFontFamiliesCache = fonts.ToList();
            return _installedFontFamiliesCache;
        }

        private static void AddFontsFromRegistry(ISet<string> fonts, Microsoft.Win32.RegistryKey? key)
        {
            if (key == null)
            {
                return;
            }

            using (key)
            {
                foreach (string valueName in key.GetValueNames())
                {
                    string family = NormalizeFontRegistryName(valueName);
                    if (!string.IsNullOrWhiteSpace(family))
                    {
                        fonts.Add(family);
                    }
                }
            }
        }

        private static string NormalizeFontRegistryName(string valueName)
        {
            string family = Regex.Replace(valueName, @"\s*\([^)]+\)\s*$", string.Empty).Trim();
            family = Regex.Replace(family, @"\s+(Regular|Normal|Bold|Italic|Oblique|Light|Medium|SemiBold|Semibold|ExtraLight|ExtraBold|Black|Thin|Condensed|Narrow)$", string.Empty, RegexOptions.IgnoreCase).Trim();
            return family;
        }

        private static Windows.UI.Color ResolvePickerColor(string? colorValue, string fallbackHex)
        {
            if (TryParseHexColor(colorValue, out var color) || TryParseHexColor(fallbackHex, out color))
            {
                return color;
            }

            return Windows.UI.Color.FromArgb(255, 0, 0, 0);
        }

        private static string ColorToHex(Windows.UI.Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static bool TryParseHexColor(string? value, out Windows.UI.Color color)
        {
            color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
            string hex = (value ?? string.Empty).Trim().TrimStart('#');
            if (hex.Length != 6)
            {
                return false;
            }

            try
            {
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                color = Windows.UI.Color.FromArgb(255, r, g, b);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<IReadOnlyList<string>> FetchLmStudioModelsAsync(string endpoint)
        {
            string baseEndpoint = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:1234/v1" : endpoint.Trim();
            string requestUrl = baseEndpoint.TrimEnd('/') + "/models";

            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
            using (var response = await client.GetAsync(requestUrl))
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"모델 목록 요청 실패 ({response.StatusCode}): {responseBody}");
                }

                using (var doc = JsonDocument.Parse(responseBody))
                {
                    var models = new List<string>();
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in data.EnumerateArray())
                        {
                            if (item.TryGetProperty("id", out var idElement))
                            {
                                string? id = idElement.GetString();
                                if (!string.IsNullOrWhiteSpace(id))
                                {
                                    models.Add(id);
                                }
                            }
                        }
                    }

                    return models;
                }
            }
        }

        private static void ApplyCompactStyleToLogicalTree(object element)
        {
            if (element == null) return;

            if (element is Control ctrl)
            {
                if (ctrl is not Pivot && ctrl is not PivotItem)
                {
                    if (ctrl is ListView)
                    {
                        ctrl.FontSize = 11;
                    }
                    else
                    {
                        ctrl.FontSize = 11.5;
                    }
                }

                if (ctrl is DropDownButton ddb)
                {
                    ddb.MinHeight = 26;
                    ddb.Height = Double.NaN; // Allow automatic height so swatch is not clipped
                    ddb.Padding = new Thickness(6, 2, 6, 2);
                    ddb.VerticalAlignment = VerticalAlignment.Center;
                }
                else if (ctrl is ComboBox || ctrl is TextBox || ctrl is PasswordBox || ctrl is Button)
                {
                    ctrl.MinHeight = 26;
                    ctrl.Height = 26;
                    ctrl.Padding = new Thickness(8, 2, 8, 2);
                    ctrl.VerticalAlignment = VerticalAlignment.Center;
                }
                else if (ctrl is CheckBox chk)
                {
                    chk.MinHeight = 22;
                    chk.Padding = new Thickness(8, 2, 0, 2);
                    chk.Margin = new Thickness(chk.Margin.Left, 1, chk.Margin.Right, 1);
                }
            }
            else if (element is TextBlock tb)
            {
                if (tb.FontSize != 11 && tb.FontSize != 12)
                {
                    tb.FontSize = 11.5;
                }
            }

            if (element is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    ApplyCompactStyleToLogicalTree(child);
                }
            }
            else if (element is ContentControl cc)
            {
                ApplyCompactStyleToLogicalTree(cc.Content);
            }
            else if (element is ScrollViewer sv)
            {
                ApplyCompactStyleToLogicalTree(sv.Content);
            }
            else if (element is Pivot pivot)
            {
                foreach (var item in pivot.Items)
                {
                    ApplyCompactStyleToLogicalTree(item);
                }
            }
        }

        private static void ApplyCompactStyleToVisualTree(Microsoft.UI.Xaml.DependencyObject element)
        {
            if (element == null) return;

            int childrenCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i);
                if (child is Control ctrl)
                {
                    ctrl.FontSize = 10.5;

                    if (ctrl is TextBox || ctrl is ComboBox || ctrl is Button)
                    {
                        ctrl.MinHeight = 22;
                        ctrl.Height = 22;
                        ctrl.Padding = new Thickness(4, 1, 4, 1);
                    }
                }
                else if (child is TextBlock tb)
                {
                    tb.FontSize = 10.5;
                }

                ApplyCompactStyleToVisualTree(child);
            }
        }

        private static string GetAppVersion()
        {
            try
            {
                // Try getting it from the packaged identity if running under package context
                try
                {
                    var package = Windows.ApplicationModel.Package.Current;
                    var version = package.Id.Version;
                    return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                }
                catch (System.InvalidOperationException)
                {
                    // Not running in a packaged context
                }

                // Search for Package.appxmanifest in the directory tree starting from AppContext.BaseDirectory
                string dir = AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(dir))
                {
                    string manifestPath = System.IO.Path.Combine(dir, "Package.appxmanifest");
                    if (System.IO.File.Exists(manifestPath))
                    {
                        var doc = new System.Xml.XmlDocument();
                        doc.Load(manifestPath);
                        var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
                        nsmgr.AddNamespace("f", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");
                        var identityNode = doc.SelectSingleNode("//f:Identity", nsmgr) ?? doc.SelectSingleNode("//Identity");
                        if (identityNode is System.Xml.XmlElement element)
                        {
                            string version = element.GetAttribute("Version");
                            if (!string.IsNullOrEmpty(version))
                            {
                                return version;
                            }
                        }
                    }
                    string? parent = System.IO.Path.GetDirectoryName(dir);
                    if (parent == dir || string.IsNullOrEmpty(parent))
                    {
                        break;
                    }
                    dir = parent;
                }
            }
            catch
            {
                // Fallback
            }
            return "1.0.0.0";
        }
    }
}
