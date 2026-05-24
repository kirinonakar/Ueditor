using System;
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
            languageCombo.Items.Add("한국어 (Korean)");
            languageCombo.Items.Add("English");
            languageCombo.Items.Add("日本語 (Japanese)");

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
            var autoSaveCheck = new CheckBox { Content = getString("SettingsAutoSave", "Autosave 사용"), IsChecked = settings.AutoSave };
            var defaultMarkdownCheck = new CheckBox { Content = getString("SettingsLivePreview", "실시간 미리보기 기본 활성화"), IsChecked = settings.DefaultMarkdownEnabled };
            var defaultMarkdownToolbarCheck = new CheckBox { Content = getString("SettingsMarkdownToolbar", "기본 마크다운 툴바 활성화"), IsChecked = settings.DefaultMarkdownToolbarEnabled };
            var tabSizeBox = new TextBox { PlaceholderText = "예: 4", Text = settings.TabSize.ToString(), HorizontalAlignment = HorizontalAlignment.Stretch };
            var largeThresholdBox = new TextBox { PlaceholderText = "예: 50", Text = settings.LargeFileThresholdMB.ToString(), HorizontalAlignment = HorizontalAlignment.Stretch };

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
            editorSection.Children.Add(autoSaveCheck);
            editorSection.Children.Add(defaultMarkdownCheck);
            editorSection.Children.Add(defaultMarkdownToolbarCheck);
            AddLabel(editorSection, getString("SettingsTabSize", "Tab size"));
            editorSection.Children.Add(tabSizeBox);
            AddLabel(editorSection, getString("SettingsLargeFileThreshold", "Large File Mode 제안 기준 (MB)"));
            editorSection.Children.Add(largeThresholdBox);

            var llmSection = CreateSection();
            AddLabel(llmSection, getString("SettingsLlmProvider", "LLM 공급자"));
            llmSection.Children.Add(llmProviderCombo);
            AddLabel(llmSection, getString("SettingsLlmEndpoint", "LLM API Endpoint"));
            llmSection.Children.Add(llmEndpointBox);
            AddLabel(llmSection, getString("SettingsLlmModel", "LLM 모델명"));
            llmSection.Children.Add(llmModelCombo);
            llmSection.Children.Add(refreshLmStudioModelsButton);
            llmSection.Children.Add(llmModelStatusText);
            AddLabel(llmSection, getString("SettingsLlmApiKey", "LLM API Key"));
            llmSection.Children.Add(llmApiKeyBox);
            llmSection.Children.Add(new TextBlock
            {
                Text = getString("SettingsLlmApiKeyInfo", "API Key는 설정 파일에 저장하지 않고 Windows 자격 증명 관리자에 저장합니다. 비워두고 저장하면 기존 Key를 유지합니다. LM Studio는 기본 로컬 서버 설정에서 API Key 없이 사용할 수 있습니다."),
                TextWrapping = TextWrapping.Wrap
            });

            var settingsPivot = new Pivot { Width = 500, Height = 440 };
            var toolbarSection = CreateSection();
            var showLabelsCheck = new CheckBox { Content = getString("SettingsToolbarShowLabels", "툴바 버튼 글자 표시"), IsChecked = settings.ToolbarShowLabels };
            toolbarSection.Children.Add(showLabelsCheck);
            var reorderDesc = new TextBlock
            {
                Text = getString("SettingsToolbarOrderHint", "드래그하여 버튼 순서 변경 (설정 버튼은 고정)"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };
            toolbarSection.Children.Add(reorderDesc);
            var buttonNames = new[] { "터미널", "Markdown", "테마", "고정", "스티커", "WordWrap", "비교", "인쇄" };
            var defaultOrder = settings.ToolbarButtonOrder?.Count > 0 ? settings.ToolbarButtonOrder : new List<string>(buttonNames);
            var orderList = new ListView
            {
                Height = 200,
                SelectionMode = ListViewSelectionMode.None,
                AllowDrop = true,
                CanReorderItems = true,
                ItemsSource = new List<string>(defaultOrder)
            };
            toolbarSection.Children.Add(orderList);

            settingsPivot.Items.Add(new PivotItem { Header = getString("SettingsAppearance", "모양"), Content = new ScrollViewer { Content = appearanceSection } });
            settingsPivot.Items.Add(new PivotItem { Header = getString("SettingsEditing", "편집"), Content = new ScrollViewer { Content = editorSection } });
            settingsPivot.Items.Add(new PivotItem { Header = getString("SettingsToolbar", "툴바"), Content = new ScrollViewer { Content = toolbarSection } });
            settingsPivot.Items.Add(new PivotItem { Header = getString("SettingsLLM", "LLM"), Content = new ScrollViewer { Content = llmSection } });

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
            settings.AutoSave = autoSaveCheck.IsChecked == true;
            if (int.TryParse(tabSizeBox.Text.Trim(), out int tabSize))
            {
                settings.TabSize = Math.Clamp(tabSize, 1, 16);
            }
            if (long.TryParse(largeThresholdBox.Text.Trim(), out long thresholdMb))
            {
                settings.LargeFileThresholdMB = Math.Clamp(thresholdMb, 1, 1024);
            }

            settings.LlmProvider = GetSelectedProviderName();
            settings.LlmEndpoint = llmEndpointBox.Text.Trim();
            settings.LlmModel = (llmModelCombo.SelectedItem as string ?? settings.LlmModel).Trim();

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
            settings.ToolbarButtonOrder = (orderList.ItemsSource as List<string>)?.ToList() ?? new List<string>();

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
            return new StackPanel { Spacing = 10, Width = 460, Padding = new Thickness(2, 8, 2, 2) };
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
                Height = 22,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(120, 128, 128, 128)),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(initialColor)
            };

            var picker = new ColorPicker
            {
                Color = initialColor,
                IsAlphaEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            colorPicker = picker;

            var flyoutContent = new StackPanel
            {
                Width = 320,
                Spacing = 8,
                Padding = new Thickness(8)
            };
            flyoutContent.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            flyoutContent.Children.Add(picker);

            picker.ColorChanged += (_, __) =>
            {
                swatch.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(picker.Color);
            };

            return new DropDownButton
            {
                Content = swatch,
                Flyout = new Flyout { Content = flyoutContent },
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
    }
}
