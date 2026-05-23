using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Ueditor.Core.Interfaces;
using Ueditor.Core.Services;
using Ueditor.Core.Models;
using Ueditor.Editor;

namespace Ueditor
{
    public sealed partial class MainWindow : Window
    {
        private readonly IFileService _fileService;
        private readonly ISettingsService _settingsService;
        private readonly ICredentialService _credentialService;
        private readonly ILLMService _llmService;
        private readonly IGitService _gitService;
        private readonly ISnippetService _snippetService;
        private readonly ObservableCollection<FavoriteItem> _favoritesList = new ObservableCollection<FavoriteItem>();
        private readonly ObservableCollection<SnippetItem> _snippetsList = new ObservableCollection<SnippetItem>();
        private readonly ObservableCollection<GitFileItem> _gitFilesList = new ObservableCollection<GitFileItem>();
        private readonly ObservableCollection<SearchResultItem> _searchResultsList = new ObservableCollection<SearchResultItem>();
        private string _lastSelectionText = string.Empty;
        private string _currentRepoPath = string.Empty;
        
        // Dynamic tabs collection
        private readonly ObservableCollection<OpenedTab> _tabs = new ObservableCollection<OpenedTab>();
        private readonly Dictionary<string, (WebView2 WebView, MonacoBridge Bridge)> _tabBridges = 
            new Dictionary<string, (WebView2 WebView, MonacoBridge Bridge)>();

        // Timer for debouncing live preview renders
        private readonly DispatcherTimer _previewDebounceTimer;
        private OpenedTab? _activeTabForPreview = null;

        // Custom Splitter state variables
        private bool _isDraggingLeftSplitter = false;
        private double _leftSplitterStartExplorerWidth = 0;
        private double _leftSplitterStartPointerX = 0;

        private bool _isDraggingRightSplitter = false;
        private double _rightSplitterStartPreviewWidth = 0;
        private double _rightSplitterStartPointerX = 0;

        public MainWindow()
        {
            this.InitializeComponent();

            _fileService = new FileService();
            _settingsService = new SettingsService();
            _credentialService = new CredentialService();
            _llmService = new LLMService(_settingsService, _credentialService);
            _gitService = new GitService();
            _snippetService = new SnippetService();

            // Bind Left Sidebar Tab items
            FavoritesListView.ItemsSource = _favoritesList;
            SnippetsListView.ItemsSource = _snippetsList;
            GitChangedFilesList.ItemsSource = _gitFilesList;
            SearchResultsList.ItemsSource = _searchResultsList;

            // Initialize Preview Debounce Timer (300ms)
            _previewDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _previewDebounceTimer.Tick += OnPreviewDebounceTimerTick;

            // Load local configurations and boot initial states
            this.Activated += OnWindowActivated;
        }

        private async void OnWindowActivated(object sender, WindowActivatedEventArgs e)
        {
            this.Activated -= OnWindowActivated;
            
            // 1. Load settings JSON
            await _settingsService.LoadSettingsAsync();
            WordWrapToggle.IsChecked = _settingsService.CurrentSettings.WordWrap;
            ApplyUiPersonalization(_settingsService.CurrentSettings);

            // 2. Initialize Preview Panel WebView2
            await InitializePreviewWebViewAsync();

            // 3. Open a default blank tab on startup
            OpenNewTab();

            // 4. Load Snippets and Favorites
            await _snippetService.LoadSnippetsAsync();
            RefreshSnippetsUI();
            RefreshFavoritesUI();
        }

        #region WebView2 Host Resource Mapping & Preview Init

        private async Task InitializePreviewWebViewAsync()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string cacheFolder = Path.Combine(localAppData, "Ueditor", "WebView2Cache");
                var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, cacheFolder, null);
                await PreviewWebView.EnsureCoreWebView2Async(env);
                
                // Configure Virtual Host Mapping to access local files under WebResources folder via simulated URL http://ueditor.local/
                string webResourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebResources");
                
                PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "ueditor.local", 
                    webResourcesPath, 
                    CoreWebView2HostResourceAccessKind.Allow
                );

                PreviewWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                PreviewWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                PreviewWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                // Load preview renderer page
                PreviewWebView.Source = new Uri("http://ueditor.local/preview.html");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to init preview webview: {ex.Message}");
            }
        }

        #endregion

        #region Tab Operations (탭 비즈니스 로직)

        private void OpenNewTab(string? filePath = null, string content = "", bool isLargeFileMode = false)
        {
            var tab = new OpenedTab();
            tab.IsLargeFileMode = isLargeFileMode;

            if (filePath != null)
            {
                tab.FilePath = filePath;
                tab.Title = Path.GetFileName(filePath);
                tab.Content = content;
                tab.Language = GetMonacoLanguageName(filePath);
            }
            else
            {
                tab.Title = "제목 없음";
                tab.Content = "";
            }

            _tabs.Add(tab);

            // Create host layout grid for standard WebView2 editor
            var grid = new Grid();
            var editorWebView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            grid.Children.Add(editorWebView);

            // Instantiate TabViewItem XAML element
            var tabItem = new TabViewItem
            {
                Header = tab.DisplayTitle,
                Content = grid,
                Tag = tab.Id
            };

            if (isLargeFileMode)
            {
                _tabBridges[tab.Id] = (editorWebView, null!);
                // Large File Mode WebView2 initialization and event loop
                InitializeLargeFileWebView(editorWebView, tab, tabItem);
            }
            else
            {
                var bridge = new MonacoBridge(editorWebView);
                _tabBridges[tab.Id] = (editorWebView, bridge);

                // Register Bridge Initialization & IPC Events
                bridge.EditorReady += async () =>
                {
                    await bridge.SetTextAsync(tab.Content);
                    await bridge.SetLanguageAsync(filePath ?? "file.txt");
                    await bridge.UpdateOptionsAsync(_settingsService.CurrentSettings);
                };

                bridge.ContentChanged += (newText) =>
                {
                    tab.Content = newText;
                    if (!tab.IsDirty)
                    {
                        tab.IsDirty = true;
                        tabItem.Header = tab.DisplayTitle;
                    }

                    // Trigger Live Preview Update with Debounce
                    _activeTabForPreview = tab;
                    _previewDebounceTimer.Stop();
                    _previewDebounceTimer.Start();
                };

                bridge.CursorChanged += (line, col) =>
                {
                    if (EditorTabView.SelectedItem as TabViewItem == tabItem)
                    {
                        StatusLine.Text = line.ToString();
                        StatusCol.Text = col.ToString();
                        _ = bridge.RequestSelectionAsync(); // Auto sync selection on cursor move
                    }
                };

                bridge.SelectionReceived += (selectedText) =>
                {
                    _lastSelectionText = selectedText;
                    if (EditorTabView.SelectedItem as TabViewItem == tabItem)
                    {
                        if (string.IsNullOrEmpty(selectedText))
                        {
                            SelectionStatsText.Text = "선택 영역: 없음 (전체 전송 차단 활성화)";
                        }
                        else
                        {
                            SelectionStatsText.Text = $"선택 영역: {selectedText.Length:N0} 글자 수 (약 {selectedText.Length / 4} 토큰)";
                        }
                    }
                };

                // Initialize editor inside WebView2 using virtual host mappings
                InitializeEditorWebView(editorWebView, bridge);
            }

            EditorTabView.TabItems.Add(tabItem);
            EditorTabView.SelectedItem = tabItem;

            UpdateStatusFileStats(tab);
        }

        private async void InitializeLargeFileWebView(WebView2 wv, OpenedTab tab, TabViewItem tabItem)
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string cacheFolder = Path.Combine(localAppData, "Ueditor", "WebView2Cache");
                var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, cacheFolder, null);
                
                await wv.EnsureCoreWebView2Async(env);

                string webResourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebResources");
                wv.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "ueditor.local", 
                    webResourcesPath, 
                    CoreWebView2HostResourceAccessKind.Allow
                );

                wv.CoreWebView2.Settings.IsWebMessageEnabled = true;
                wv.CoreWebView2.Settings.IsScriptEnabled = true;
                wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                wv.WebMessageReceived += async (sender, args) =>
                {
                    try
                    {
                        string json = args.WebMessageAsJson;
                        using (var doc = System.Text.Json.JsonDocument.Parse(json))
                        {
                            var root = doc.RootElement;
                            if (!root.TryGetProperty("type", out var typeProp)) return;

                            string type = typeProp.GetString() ?? string.Empty;

                            if (type == "requestLines")
                            {
                                int start = root.GetProperty("startLine").GetInt32();
                                int count = root.GetProperty("count").GetInt32();
                                string path = root.GetProperty("filePath").GetString() ?? string.Empty;

                                // Seek and fetch actual line array from file stream in C#
                                var lines = await _fileService.GetLargeFileLinesAsync(path, start, count);
                                
                                // Dynamic overlay of memory edits on top of stream chunks
                                for (int i = 0; i < lines.Count; i++)
                                {
                                    int currentLineNum = start + i;
                                    if (tab.LargeFilePatches.TryGetValue(currentLineNum, out string? patchText))
                                    {
                                        lines[i] = patchText;
                                    }
                                }
                                
                                var reply = new { action = "receiveLines", startLine = start, lines = lines };
                                string replyJson = System.Text.Json.JsonSerializer.Serialize(reply);
                                wv.CoreWebView2.PostWebMessageAsJson(replyJson);
                            }
                            else if (type == "updateLine")
                            {
                                int lineNum = root.GetProperty("lineNumber").GetInt32();
                                string text = root.GetProperty("text").GetString() ?? string.Empty;
                                
                                this.DispatcherQueue.TryEnqueue(() =>
                                {
                                    tab.LargeFilePatches[lineNum] = text;
                                    if (!tab.IsDirty)
                                    {
                                        tab.IsDirty = true;
                                        tabItem.Header = tab.DisplayTitle;
                                    }
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in large file web message: {ex.Message}");
                    }
                };

                // Trigger document load
                wv.Source = new Uri("http://ueditor.local/large-viewer.html");

                wv.NavigationCompleted += async (s, e) =>
                {
                    if (e.IsSuccess && !string.IsNullOrEmpty(tab.FilePath))
                    {
                        // 1. Process line break offsets indexing in background thread
                        await _fileService.InitializeLargeFileAsync(tab.FilePath);
                        int count = await _fileService.GetLargeFileLineCountAsync(tab.FilePath);

                        // 2. Dispatch init signal with total line height
                        var initMsg = new
                        {
                            action = "init",
                            filePath = tab.FilePath,
                            lineCount = count,
                            theme = _settingsService.CurrentSettings.Theme,
                            fontSize = _settingsService.CurrentSettings.FontSize
                        };
                        string initJson = System.Text.Json.JsonSerializer.Serialize(initMsg);
                        wv.CoreWebView2.PostWebMessageAsJson(initJson);
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed initialization of large file webview: {ex.Message}");
            }
        }

        private async void InitializeEditorWebView(WebView2 wv, MonacoBridge bridge)
        {
            try
            {
                await bridge.InitializeAsync();
                
                string webResourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebResources");
                wv.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "ueditor.local", 
                    webResourcesPath, 
                    CoreWebView2HostResourceAccessKind.Allow
                );

                bridge.LoadEditor("http://ueditor.local/editor.html");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed initialization of editor: {ex.Message}");
            }
        }

        private string GetMonacoLanguageName(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            return ext switch
            {
                ".md" => "markdown",
                ".markdown" => "markdown",
                ".html" => "html",
                ".htm" => "html",
                ".tex" => "latex",
                _ => "plaintext"
            };
        }

        #endregion

        #region Live Preview Debouncing & Sync

        private void OnPreviewDebounceTimerTick(object? sender, object e)
        {
            _previewDebounceTimer.Stop();
            if (_activeTabForPreview != null)
            {
                UpdateLivePreview(_activeTabForPreview);
            }
        }

        private void UpdateLivePreview(OpenedTab tab)
        {
            try
            {
                if (PreviewWebView.CoreWebView2 == null) return;

                // Sync current combo mode selection or choose intelligent defaults
                string mode = "markdown";
                if (PreviewModeCombo.SelectedItem is ComboBoxItem item)
                {
                    mode = item.Content.ToString() switch
                    {
                        "HTML Source" => "html",
                        "LaTeX Block" => "latex",
                        _ => "markdown"
                    };
                }

                var renderMsg = new
                {
                    action = "render",
                    text = tab.Content,
                    mode = mode,
                    theme = _settingsService.CurrentSettings.Theme
                };

                string json = System.Text.Json.JsonSerializer.Serialize(renderMsg);
                PreviewWebView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed sending live preview rendering data: {ex.Message}");
            }
        }

        #endregion

        #region XAML Interactive Handlers

        private async void OnOpenFileClick(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            InitializePickerWindow(picker);
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".markdown");
            picker.FileTypeFilter.Add(".html");
            picker.FileTypeFilter.Add(".css");
            picker.FileTypeFilter.Add(".js");
            picker.FileTypeFilter.Add(".cs");
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".tex");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await LoadFileIntoTabAsync(file.Path);
            }
        }

        private async Task LoadFileIntoTabAsync(string filePath)
        {
            try
            {
                var largeInfo = await _fileService.GetLargeFileInfoAsync(filePath);
                if (largeInfo.IsLargeFile)
                {
                    // Large file handling warn
                    var dialog = new ContentDialog
                    {
                        Title = "대용량 파일 경고",
                        Content = $"선택한 파일의 크기가 {largeInfo.FileSize / (1024 * 1024.0):F2}MB 입니다.\nMonaco Editor 대신 Large File Mode(읽기 전용 청킹 엔진)로 여시겠습니까?",
                        PrimaryButtonText = "대용량 모드로 열기",
                        SecondaryButtonText = "일반 Monaco로 강제 열기",
                        CloseButtonText = "취소",
                        XamlRoot = this.Content.XamlRoot
                    };

                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        StatusMode.Text = "대용량 모드";
                        OpenNewTab(filePath, "", isLargeFileMode: true);
                        return;
                    }
                    else if (result == ContentDialogResult.None)
                    {
                        return; // Cancel
                    }
                }

                StatusMode.Text = "일반 모드";
                string content = await _fileService.ReadTextFileAsync(filePath);
                OpenNewTab(filePath, content);
            }
            catch (Exception ex)
            {
                ShowErrorMessage("파일 로드 에러", ex.Message);
            }
        }

        private async void OnSaveFileClick(object sender, RoutedEventArgs e)
        {
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId)
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab == null) return;

                if (tab.IsLargeFileMode)
                {
                    if (string.IsNullOrEmpty(tab.FilePath)) return;

                    try
                    {
                        await _fileService.SaveLargeFileWithPatchesAsync(tab.FilePath, tab.LargeFilePatches);
                        tab.LargeFilePatches.Clear();
                        tab.IsDirty = false;
                        activeTabItem.Header = tab.DisplayTitle;

                        // Find WebView2 in grid hierarchy to post init and refresh
                        WebView2? wv = null;
                        if (activeTabItem.Content is Grid grid)
                        {
                            wv = grid.Children.FirstOrDefault(c => c is WebView2) as WebView2;
                        }

                        if (wv != null && wv.CoreWebView2 != null)
                        {
                            int count = await _fileService.GetLargeFileLineCountAsync(tab.FilePath);
                            var initMsg = new
                            {
                                action = "init",
                                filePath = tab.FilePath,
                                lineCount = count,
                                theme = _settingsService.CurrentSettings.Theme,
                                fontSize = _settingsService.CurrentSettings.FontSize
                            };
                            string initJson = System.Text.Json.JsonSerializer.Serialize(initMsg);
                            wv.CoreWebView2.PostWebMessageAsJson(initJson);
                        }

                        UpdateStatusFileStats(tab);
                        await RefreshGitStatusUIAsync();
                    }
                    catch (Exception ex)
                    {
                        ShowErrorMessage("대용량 파일 저장 실패", ex.Message);
                    }
                    return;
                }

                if (string.IsNullOrEmpty(tab.FilePath))
                {
                    // Open Save File Picker
                    var picker = new FileSavePicker();
                    InitializePickerWindow(picker);
                    picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                    picker.FileTypeChoices.Add("텍스트 파일", new List<string>() { ".txt" });
                    picker.FileTypeChoices.Add("마크다운 파일", new List<string>() { ".md", ".markdown" });
                    picker.FileTypeChoices.Add("HTML 파일", new List<string>() { ".html" });
                    picker.FileTypeChoices.Add("LaTeX 파일", new List<string>() { ".tex" });
                    picker.SuggestedFileName = tab.Title;

                    var file = await picker.PickSaveFileAsync();
                    if (file != null)
                    {
                        tab.FilePath = file.Path;
                        tab.Title = file.Name;
                    }
                    else
                    {
                        return; // Canceled
                    }
                }

                try
                {
                    await _fileService.SaveTextFileAsync(tab.FilePath, tab.Content);
                    tab.IsDirty = false;
                    activeTabItem.Header = tab.DisplayTitle;
                    UpdateStatusFileStats(tab);
                }
                catch (Exception ex)
                {
                    ShowErrorMessage("저장 실패", ex.Message);
                }
            }
        }

        private async void OnWordWrapToggleClick(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.CurrentSettings;
            settings.WordWrap = WordWrapToggle.IsChecked == true;
            await _settingsService.SaveSettingsAsync(settings);

            // Propagate options to currently focused editor tab
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId &&
                _tabBridges.TryGetValue(tabId, out var bridgeGroup))
            {
                await bridgeGroup.Bridge.UpdateOptionsAsync(settings);
            }
        }

        private async void OnFindClick(object sender, RoutedEventArgs e)
        {
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId &&
                _tabBridges.TryGetValue(tabId, out var bridgeGroup))
            {
                await bridgeGroup.Bridge.TriggerFindAsync();
            }
        }

        private void OnTogglePreviewClick(object sender, RoutedEventArgs e)
        {
            // Toggle Visibility of Preview Panel by adjusting Column Grid Width
            if (PreviewColumn.Width.Value > 0)
            {
                PreviewColumn.Width = new GridLength(0);
            }
            else
            {
                PreviewColumn.Width = new GridLength(400);
                if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                    activeTabItem.Tag is string tabId)
                {
                    var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                    if (tab != null) UpdateLivePreview(tab);
                }
            }
        }

        private async void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            // Open a beautiful modal dialog for customizations
            var settings = _settingsService.CurrentSettings;

            // XAML-based quick options injection for Dialog
            var themeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = settings.Theme == "Dark" ? 0 : 1 };
            themeCombo.Items.Add("Dark Theme (vs-dark)");
            themeCombo.Items.Add("Light Theme (vs)");

            var sizeSlider = new Slider { Minimum = 10, Maximum = 24, Value = settings.FontSize, StepFrequency = 1 };
            
            var customBgBox = new TextBox { PlaceholderText = "예: #1e1e1e (기본은 비워둠)", Text = settings.CustomBackgroundColor, HorizontalAlignment = HorizontalAlignment.Stretch };
            var customFgBox = new TextBox { PlaceholderText = "예: #d4d4d4 (기본은 비워둠)", Text = settings.CustomForegroundColor, HorizontalAlignment = HorizontalAlignment.Stretch };
            var fontFamilyBox = new TextBox { PlaceholderText = "예: Consolas, 'Courier New'", Text = settings.FontFamily, HorizontalAlignment = HorizontalAlignment.Stretch };
            var uiFontFamilyBox = new TextBox { PlaceholderText = "예: Segoe UI, Malgun Gothic", Text = settings.UiFontFamily, HorizontalAlignment = HorizontalAlignment.Stretch };

            var stack = new StackPanel { Spacing = 10, Width = 320 };
            stack.Children.Add(new TextBlock { Text = "에디터 테마 설정", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            stack.Children.Add(themeCombo);
            stack.Children.Add(new TextBlock { Text = $"글자 크기 (현재: {settings.FontSize}pt)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            stack.Children.Add(sizeSlider);
            stack.Children.Add(new TextBlock { Text = "커스텀 에디터 배경색 (Hex)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            stack.Children.Add(customBgBox);
            stack.Children.Add(new TextBlock { Text = "커스텀 에디터 전경색 (Hex)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            stack.Children.Add(customFgBox);
            stack.Children.Add(new TextBlock { Text = "에디터 폰트 (FontFamily)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            stack.Children.Add(fontFamilyBox);
            stack.Children.Add(new TextBlock { Text = "UI 쉘 폰트 (UiFontFamily)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            stack.Children.Add(uiFontFamilyBox);

            var dialog = new ContentDialog
            {
                Title = "Ueditor 기본 환경 설정",
                Content = new ScrollViewer { Content = stack, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                PrimaryButtonText = "적용 및 저장",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                settings.Theme = themeCombo.SelectedIndex == 0 ? "Dark" : "Light";
                settings.FontSize = sizeSlider.Value;
                settings.CustomBackgroundColor = customBgBox.Text.Trim();
                settings.CustomForegroundColor = customFgBox.Text.Trim();
                settings.FontFamily = fontFamilyBox.Text.Trim();
                settings.UiFontFamily = uiFontFamilyBox.Text.Trim();

                await _settingsService.SaveSettingsAsync(settings);
                ApplyUiPersonalization(settings);

                // Update settings for all active Monaco editors
                foreach (var grp in _tabBridges.Values)
                {
                    if (grp.Bridge != null)
                    {
                        await grp.Bridge.UpdateOptionsAsync(settings);
                    }
                }

                // Update current preview panel render if applicable
                if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                    activeTabItem.Tag is string tabId)
                {
                    var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                    if (tab != null) UpdateLivePreview(tab);
                }
            }
        }

        #endregion

        #region Custom Splitters Event Handlers

        private void OnLeftSplitterPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is UIElement splitter)
            {
                _isDraggingLeftSplitter = true;
                _leftSplitterStartExplorerWidth = ExplorerColumn.Width.Value;
                var pt = e.GetCurrentPoint(MainWorkGrid).Position;
                _leftSplitterStartPointerX = pt.X;
                splitter.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnLeftSplitterPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isDraggingLeftSplitter && sender is UIElement splitter)
            {
                var pt = e.GetCurrentPoint(MainWorkGrid).Position;
                double deltaX = pt.X - _leftSplitterStartPointerX;
                double newWidth = _leftSplitterStartExplorerWidth + deltaX;
                
                // Clamp between MinWidth and MaxWidth
                newWidth = Math.Clamp(newWidth, ExplorerColumn.MinWidth, ExplorerColumn.MaxWidth);
                ExplorerColumn.Width = new GridLength(newWidth);
                e.Handled = true;
            }
        }

        private void OnLeftSplitterPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isDraggingLeftSplitter && sender is UIElement splitter)
            {
                _isDraggingLeftSplitter = false;
                splitter.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnRightSplitterPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is UIElement splitter)
            {
                _isDraggingRightSplitter = true;
                _rightSplitterStartPreviewWidth = PreviewColumn.Width.Value;
                var pt = e.GetCurrentPoint(MainWorkGrid).Position;
                _rightSplitterStartPointerX = pt.X;
                splitter.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnRightSplitterPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isDraggingRightSplitter && sender is UIElement splitter)
            {
                var pt = e.GetCurrentPoint(MainWorkGrid).Position;
                double deltaX = pt.X - _rightSplitterStartPointerX;
                double newWidth = _rightSplitterStartPreviewWidth - deltaX;
                
                // Clamp between MinWidth and MaxWidth
                newWidth = Math.Clamp(newWidth, PreviewColumn.MinWidth, PreviewColumn.MaxWidth);
                PreviewColumn.Width = new GridLength(newWidth);
                e.Handled = true;
            }
        }

        private void OnRightSplitterPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isDraggingRightSplitter && sender is UIElement splitter)
            {
                _isDraggingRightSplitter = false;
                splitter.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
        }

        #endregion

        #region Explorer Side Panel & Folder Picker

        private async void OnSelectFolderClick(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            InitializePickerWindow(picker);
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                _currentRepoPath = folder.Path;
                FileTreeView.RootNodes.Clear();
                var rootNode = new TreeViewNode
                {
                    Content = new ExplorerItem
                    {
                        Name = folder.Name,
                        Path = folder.Path,
                        IsFolder = true
                    },
                    IsExpanded = true
                };

                // Populate initial children
                LoadDirectoryChildren(folder.Path, rootNode);
                FileTreeView.RootNodes.Add(rootNode);

                // Trigger Git branch detection & status update
                await RefreshGitStatusUIAsync();
            }
        }

        private void LoadDirectoryChildren(string parentPath, TreeViewNode parentNode)
        {
            try
            {
                parentNode.Children.Clear();
                var dirInfo = new DirectoryInfo(parentPath);
                
                // 1. Folders first
                foreach (var dir in dirInfo.GetDirectories())
                {
                    // Ignore hidden directories like .git
                    if (dir.Attributes.HasFlag(FileAttributes.Hidden) || dir.Name.StartsWith("."))
                        continue;

                    var item = new ExplorerItem { Name = dir.Name, Path = dir.FullName, IsFolder = true };
                    var node = new TreeViewNode { Content = item, HasUnrealizedChildren = true };
                    parentNode.Children.Add(node);
                }

                // 2. Files next
                foreach (var file in dirInfo.GetFiles())
                {
                    if (file.Attributes.HasFlag(FileAttributes.Hidden))
                        continue;

                    var item = new ExplorerItem { Name = file.Name, Path = file.FullName, IsFolder = false };
                    var node = new TreeViewNode { Content = item, HasUnrealizedChildren = false };
                    parentNode.Children.Add(node);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed reading folder hierarchy: {ex.Message}");
            }
        }

        private void OnFileTreeViewItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            var node = args.InvokedItem as TreeViewNode;
            if (node == null) return;

            var item = node.Content as ExplorerItem;
            if (item == null) return;

            if (item.IsFolder)
            {
                // Lazy Expansion
                if (node.HasUnrealizedChildren)
                {
                    LoadDirectoryChildren(item.Path, node);
                    node.HasUnrealizedChildren = false;
                }
                node.IsExpanded = !node.IsExpanded;
            }
            else
            {
                // Open file in new Tab
                _ = LoadFileIntoTabAsync(item.Path);
            }
        }

        #endregion

        #region TabView Structural Interops

        private void OnEditorTabViewAddTabClick(TabView sender, object args)
        {
            OpenNewTab();
        }

        private void OnEditorTabViewTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            if (args.Item is TabViewItem tabItem && tabItem.Tag is string tabId)
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null)
                {
                    if (tab.IsDirty)
                    {
                        // Unsaved warning
                        WarnUnsavedAndClose(tab, tabItem);
                        return;
                    }

                    CloseTabAndCleanup(tab, tabItem);
                }
            }
        }

        private async void WarnUnsavedAndClose(OpenedTab tab, TabViewItem tabItem)
        {
            var dialog = new ContentDialog
            {
                Title = "변경 내용 저장",
                Content = $"파일 '{tab.Title}'의 변경 내용이 저장되지 않았습니다. 닫으시겠습니까?",
                PrimaryButtonText = "저장하지 않고 닫기",
                SecondaryButtonText = "저장",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                CloseTabAndCleanup(tab, tabItem);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                OnSaveFileClick(this, new RoutedEventArgs());
                if (!tab.IsDirty)
                {
                    CloseTabAndCleanup(tab, tabItem);
                }
            }
        }

        private void CloseTabAndCleanup(OpenedTab tab, TabViewItem tabItem)
        {
            _tabs.Remove(tab);
            EditorTabView.TabItems.Remove(tabItem);

            if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup))
            {
                bridgeGroup.WebView.Close(); // Dispose webview resource
                _tabBridges.Remove(tab.Id);
            }

            if (_tabs.Count == 0)
            {
                OpenNewTab();
            }
        }

        private void OnEditorTabViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId)
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null)
                {
                    UpdateStatusFileStats(tab);
                    UpdateLivePreview(tab);
                }
            }
        }

        #endregion

        #region Preview Header Syncs

        private void OnRefreshPreviewClick(object sender, RoutedEventArgs e)
        {
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId)
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null) UpdateLivePreview(tab);
            }
        }

        private void OnPreviewModeComboSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PreviewWebView != null && PreviewWebView.CoreWebView2 != null)
            {
                OnRefreshPreviewClick(this, new RoutedEventArgs());
            }
        }

        #endregion

        #region Helpers & UI Triggers

        private void UpdateStatusFileStats(OpenedTab tab)
        {
            if (!string.IsNullOrEmpty(tab.FilePath) && File.Exists(tab.FilePath))
            {
                long bytes = new FileInfo(tab.FilePath).Length;
                StatusFileStats.Text = $"크기: {bytes:N0} bytes";
            }
            else
            {
                StatusFileStats.Text = "크기: 0 bytes";
            }
        }

        private void InitializePickerWindow(object picker)
        {
            // WinUI 3 Window association wrapper for file pickers (required in WinAppSDK)
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        private async void ShowErrorMessage(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "확인",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async void OnSaveApiKeyClick(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.CurrentSettings;
            string apiKey = LlmApiKeyInput.Password;
            if (!string.IsNullOrEmpty(apiKey))
            {
                await _llmService.SaveApiKeyAsync(settings.LlmProvider, apiKey);
                LlmApiKeyInput.Password = string.Empty;
                LlmOutputText.Text = $"{settings.LlmProvider} API Key가 Windows 자격 증명 저장소에 성공적으로 암호화 저장되었습니다.";
            }
        }

        private async void OnLlmExplainClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText))
            {
                ShowErrorMessage("AI 오류", "선택된 텍스트가 없습니다. 에디터에서 분석할 범위를 드래그한 후 실행하십시오.");
                return;
            }
            await PreflightCheckAndRunAsync("선택 영역 설명 (Explain)", _lastSelectionText, 
                () => _llmService.ExplainCodeAsync(_lastSelectionText, "csharp"));
        }

        private async void OnLlmSummarizeClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText))
            {
                ShowErrorMessage("AI 오류", "선택된 텍스트가 없습니다. 요약할 범위를 드래그하십시오.");
                return;
            }
            await PreflightCheckAndRunAsync("선택 영역 요약 (Summarize)", _lastSelectionText, 
                () => _llmService.SummarizeTextAsync(_lastSelectionText));
        }

        private async void OnLlmImproveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText))
            {
                ShowErrorMessage("AI 오류", "선택된 텍스트가 없습니다. 개선할 범위를 드래그하십시오.");
                return;
            }
            await PreflightCheckAndRunAsync("수식 및 마크다운 개선", _lastSelectionText, 
                () => _llmService.CustomPromptAsync("제공된 텍스트의 가독성, 마크다운 형식, 또는 LaTeX 수학 공식을 표준 문법에 맞게 개선하여 한글로 정제해 주십시오.", _lastSelectionText));
        }

        private async void OnLlmCustomClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText))
            {
                ShowErrorMessage("AI 오류", "선택된 컨텍스트가 없습니다. 지시사항의 기반이 될 텍스트 범위를 드래그하십시오.");
                return;
            }
            string prompt = LlmCustomPromptInput.Text;
            if (string.IsNullOrEmpty(prompt))
            {
                ShowErrorMessage("AI 오류", "커스텀 지시사항 입력란이 비어 있습니다.");
                return;
            }
            await PreflightCheckAndRunAsync("커스텀 지시사항 실행", _lastSelectionText, 
                () => _llmService.CustomPromptAsync(prompt, _lastSelectionText));
        }

        private async Task PreflightCheckAndRunAsync(string actionName, string contentText, Func<Task<string>> llmCall)
        {
            var textPreview = contentText.Length > 200 ? contentText.Substring(0, 200) + "..." : contentText;
            var dialog = new ContentDialog
            {
                Title = "AI 전송 사전 확인 (Pre-flight Check)",
                Content = $"액션: {actionName}\n\n전송될 AI 공급자: {_settingsService.CurrentSettings.LlmProvider} ({_settingsService.CurrentSettings.LlmModel})\n전송 텍스트 크기: {contentText.Length:N0} 자 (약 {contentText.Length / 4:N0} 토큰 소모)\n\n[전송 내용 미리보기]\n{textPreview}\n\n보안상의 문제나 의도하지 않은 토큰 대량 유실이 없는지 확인 후 전송해 주십시오.",
                PrimaryButtonText = "API 전송 승인",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                LlmOutputText.Text = "AI 분석 및 응답 생성이 비동기 구동 중입니다. 잠시만 대기해 주십시오...";
                RightTabView.SelectedIndex = 1; // Focus to AI Tab
                
                try
                {
                    string aiResponse = await llmCall();
                    LlmOutputText.Text = aiResponse;
                }
                catch (Exception ex)
                {
                    LlmOutputText.Text = $"AI 실행 도중 예외가 터졌습니다: {ex.Message}";
                }
            }
        }

        private async Task UpdateGitBranchStatusAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string branch = await _gitService.GetCurrentBranchAsync(path);
            StatusGitBranch.Text = branch;
        }

        #region Favorites Handlers

        private async void OnAddFileToFavoritesClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.DataContext is TreeViewNode node && node.Content is ExplorerItem explorerItem)
            {
                if (explorerItem.IsFolder) return; // File only for simplicity

                var settings = _settingsService.CurrentSettings;
                if (!settings.FavoritePaths.Contains(explorerItem.Path))
                {
                    settings.FavoritePaths.Add(explorerItem.Path);
                    await _settingsService.SaveSettingsAsync(settings);
                    RefreshFavoritesUI();
                }
            }
        }

        private void RefreshFavoritesUI()
        {
            _favoritesList.Clear();
            var settings = _settingsService.CurrentSettings;
            foreach (var path in settings.FavoritePaths)
            {
                if (File.Exists(path))
                {
                    _favoritesList.Add(new FavoriteItem
                    {
                        Name = Path.GetFileName(path),
                        Path = path
                    });
                }
            }
        }

        private async void OnRemoveFavoriteClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                var settings = _settingsService.CurrentSettings;
                settings.FavoritePaths.Remove(path);
                await _settingsService.SaveSettingsAsync(settings);
                RefreshFavoritesUI();
            }
        }

        private async void OnFavoriteItemDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (FavoritesListView.SelectedItem is FavoriteItem item)
            {
                await LoadFileIntoTabAsync(item.Path);
            }
        }

        #endregion

        #region Snippets Handlers

        private void RefreshSnippetsUI()
        {
            _snippetsList.Clear();
            var list = _snippetService.GetSnippets();
            foreach (var item in list)
            {
                _snippetsList.Add(item);
            }
        }

        private async void OnSnippetItemDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (SnippetsListView.SelectedItem is SnippetItem item)
            {
                if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                    activeTabItem.Tag is string tabId &&
                    _tabBridges.TryGetValue(tabId, out var bridgeGroup))
                {
                    await bridgeGroup.Bridge.InsertTextAsync(item.Content);
                }
                else
                {
                    ShowErrorMessage("스니펫 삽입 오류", "현재 텍스트 에디터 창이 활성화되어 있지 않거나 대용량 모드(읽기 전용)입니다.");
                }
            }
        }

        private async void OnDeleteSnippetClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string title)
            {
                await _snippetService.DeleteSnippetAsync(title);
                RefreshSnippetsUI();
            }
        }

        private async void OnAddSnippetClick(object sender, RoutedEventArgs e)
        {
            var titleBox = new TextBox { PlaceholderText = "스니펫 이름 (예: C# Loop)", Width = 300 };
            var descBox = new TextBox { PlaceholderText = "간단한 설명", Width = 300 };
            var contentBox = new TextBox { PlaceholderText = "코드 본문 입력...", AcceptsReturn = true, Height = 150, Width = 300, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };

            var stack = new StackPanel { Spacing = 10 };
            stack.Children.Add(new TextBlock { Text = "스니펫 이름" });
            stack.Children.Add(titleBox);
            stack.Children.Add(new TextBlock { Text = "설명" });
            stack.Children.Add(descBox);
            stack.Children.Add(new TextBlock { Text = "템플릿 내용" });
            stack.Children.Add(contentBox);

            var dialog = new ContentDialog
            {
                Title = "새 코드/수식 스니펫 추가",
                Content = stack,
                PrimaryButtonText = "추가",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrEmpty(titleBox.Text))
            {
                var newSnippet = new SnippetItem
                {
                    Title = titleBox.Text,
                    Description = descBox.Text,
                    Content = contentBox.Text
                };
                await _snippetService.AddSnippetAsync(newSnippet);
                RefreshSnippetsUI();
            }
        }

        #endregion

        #endregion

        #region UI Personalization Helper
        private void ApplyUiPersonalization(EditorSettings settings)
        {
            if (this.Content is FrameworkElement rootElement)
            {
                try
                {
                    var fontFamily = new Microsoft.UI.Xaml.Media.FontFamily(settings.UiFontFamily);
                    ApplyFontFamilyRecursively(rootElement, fontFamily);
                }
                catch { }

                if (!string.IsNullOrEmpty(settings.CustomBackgroundColor))
                {
                    try
                    {
                        string hex = settings.CustomBackgroundColor.Trim().Replace("#", "");
                        if (hex.Length == 6)
                        {
                            byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                            byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                            byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                            
                            var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
                            if (rootElement is Grid rootGrid)
                            {
                                rootGrid.Background = brush;
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    if (rootElement is Grid rootGrid)
                    {
                        rootGrid.Background = null; // Revert default (Transparent to keep Mica Backdrop)
                    }
                }
            }
        }

        private void ApplyFontFamilyRecursively(DependencyObject parent, Microsoft.UI.Xaml.Media.FontFamily fontFamily)
        {
            if (parent == null) return;
            
            if (parent is Control ctrl)
            {
                ctrl.FontFamily = fontFamily;
            }
            else if (parent is TextBlock tb)
            {
                tb.FontFamily = fontFamily;
            }

            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                ApplyFontFamilyRecursively(child, fontFamily);
            }
        }
        #endregion

        #region Advanced Git Handlers

        private async Task RefreshGitStatusUIAsync()
        {
            if (string.IsNullOrEmpty(_currentRepoPath))
            {
                GitPanelBranchText.Text = "Git: 감지 안됨";
                _gitFilesList.Clear();
                return;
            }

            string branch = await _gitService.GetCurrentBranchAsync(_currentRepoPath);
            GitPanelBranchText.Text = branch;
            StatusGitBranch.Text = branch;

            _gitFilesList.Clear();
            var fileStatuses = await _gitService.GetFileStatusesAsync(_currentRepoPath);
            foreach (var kvp in fileStatuses)
            {
                string fullPath = kvp.Key;
                string status = kvp.Value; // e.g. "M ", " M", "A ", "??", "D "

                bool isStaged = status.StartsWith("M") || status.StartsWith("A") || status.StartsWith("D");
                string statusDesc = isStaged ? "Staged" : "Unstaged";
                if (status == "??") statusDesc = "Untracked";

                string actionGlyph = isStaged ? "\xE108" : "\xE109"; // Minus (Unstage) or Plus (Stage) in Segoe MDL2

                _gitFilesList.Add(new GitFileItem
                {
                    Name = Path.GetFileName(fullPath),
                    Path = fullPath,
                    StatusText = $"{statusDesc} ({status.Trim()})",
                    ActionGlyph = actionGlyph,
                    IsStaged = isStaged
                });
            }
        }

        private async void OnGitStageToggleClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath)
            {
                var item = _gitFilesList.FirstOrDefault(f => f.Path == filePath);
                if (item == null) return;

                bool success;
                if (item.IsStaged)
                {
                    success = await _gitService.UnstageFileAsync(_currentRepoPath, filePath);
                }
                else
                {
                    success = await _gitService.StageFileAsync(_currentRepoPath, filePath);
                }

                if (success)
                {
                    await RefreshGitStatusUIAsync();
                }
                else
                {
                    ShowErrorMessage("Git Stage 변경 실패", "Git CLI 명령 처리에 실패했습니다.");
                }
            }
        }

        private async void OnGitFileDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (GitChangedFilesList.SelectedItem is GitFileItem item)
            {
                string diff = await _gitService.GetFileDiffAsync(_currentRepoPath, item.Path);
                OpenNewTab(filePath: item.Path + ".diff", content: diff);
            }
        }

        private async void OnGitCommitClick(object sender, RoutedEventArgs e)
        {
            string msg = GitCommitMessageInput.Text;
            if (string.IsNullOrEmpty(msg))
            {
                ShowErrorMessage("Git 커밋", "커밋 메시지를 채워주십시오.");
                return;
            }

            bool success = await _gitService.CommitAsync(_currentRepoPath, msg);
            if (success)
            {
                GitCommitMessageInput.Text = string.Empty;
                await RefreshGitStatusUIAsync();
                ShowErrorMessage("Git 커밋", "성공적으로 커밋 완료되었습니다!");
            }
            else
            {
                ShowErrorMessage("Git 커밋 실패", "커밋 도중 에러가 났습니다. 변경 조각(Staged)이 등록되었는지 확인하십시오.");
            }
        }

        private async void OnGitRefreshClick(object sender, RoutedEventArgs e)
        {
            await RefreshGitStatusUIAsync();
        }

        #endregion

        #region Advanced Search & Replace Handlers

        private async void OnSearchAllFilesClick(object sender, RoutedEventArgs e)
        {
            string query = SearchQueryInput.Text;
            if (string.IsNullOrEmpty(query)) return;

            if (string.IsNullOrEmpty(_currentRepoPath))
            {
                ShowErrorMessage("검색 실패", "먼저 탐색기에서 작업할 폴더를 선택하십시오.");
                return;
            }

            _searchResultsList.Clear();
            bool isRegex = SearchRegexToggle.IsChecked == true;
            bool isMatchCase = SearchMatchCaseToggle.IsChecked == true;
            bool isWholeWord = SearchWholeWordToggle.IsChecked == true;

            await Task.Run(async () =>
            {
                try
                {
                    var files = Directory.GetFiles(_currentRepoPath, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        // Filter system paths
                        if (file.Contains("\\.git\\") || file.Contains("\\bin\\") || file.Contains("\\obj\\") || file.Contains("\\.vs\\"))
                            continue;

                        var info = new FileInfo(file);
                        if (info.Length > 50 * 1024 * 1024)
                        {
                            var largeResults = await _fileService.SearchLargeFileAsync(file, query, isRegex);
                            foreach (var lr in largeResults)
                            {
                                this.DispatcherQueue.TryEnqueue(() =>
                                {
                                    _searchResultsList.Add(new SearchResultItem
                                    {
                                        Path = file,
                                        LineNumber = lr.LineNumber,
                                        LineContent = lr.LineContent,
                                        IndexOfMatch = lr.IndexOfMatch,
                                        MatchLength = lr.MatchLength
                                    });
                                });
                            }
                        }
                        else
                        {
                            int lineNum = 1;
                            var lines = File.ReadLines(file);
                            var options = isMatchCase ? System.Text.RegularExpressions.RegexOptions.None : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                            
                            string pattern = isRegex ? query : System.Text.RegularExpressions.Regex.Escape(query);
                            if (isWholeWord)
                            {
                                pattern = $"\\b{pattern}\\b";
                            }

                            var regex = new System.Text.RegularExpressions.Regex(pattern, options);
                            foreach (var line in lines)
                            {
                                var match = regex.Match(line);
                                if (match.Success)
                                {
                                    var currentLine = line;
                                    var currentLineNum = lineNum;
                                    var currentFile = file;
                                    var currentIdx = match.Index;
                                    var currentLen = match.Length;

                                    this.DispatcherQueue.TryEnqueue(() =>
                                    {
                                        _searchResultsList.Add(new SearchResultItem
                                        {
                                            Path = currentFile,
                                            LineNumber = currentLineNum,
                                            LineContent = currentLine,
                                            IndexOfMatch = currentIdx,
                                            MatchLength = currentLen
                                        });
                                    });
                                }
                                lineNum++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error scanning folder search: {ex.Message}");
                }
            });
        }

        private async void OnReplaceAllClick(object sender, RoutedEventArgs e)
        {
            string query = SearchQueryInput.Text;
            string replace = ReplaceQueryInput.Text;
            if (string.IsNullOrEmpty(query) || _searchResultsList.Count == 0) return;

            var dialog = new ContentDialog
            {
                Title = "전체 치환 경고",
                Content = $"{_searchResultsList.Count}개의 일치 항목을 '{replace}'(으)로 일괄 치환하시겠습니까?",
                PrimaryButtonText = "치환 실행",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var grouped = _searchResultsList.GroupBy(r => r.Path);
                foreach (var group in grouped)
                {
                    string filePath = group.Key;
                    try
                    {
                        var info = new FileInfo(filePath);
                        if (info.Length > 50 * 1024 * 1024)
                        {
                            await ReplaceInLargeFileAsync(filePath, group.ToList(), query, replace);
                        }
                        else
                        {
                            var lines = File.ReadAllLines(filePath).ToList();
                            var sorted = group.OrderByDescending(r => r.LineNumber).ToList();
                            foreach (var res in sorted)
                            {
                                if (res.LineNumber - 1 < lines.Count)
                                {
                                    string original = lines[res.LineNumber - 1];
                                    string updated;
                                    if (SearchRegexToggle.IsChecked == true)
                                    {
                                        var options = SearchMatchCaseToggle.IsChecked == true ? System.Text.RegularExpressions.RegexOptions.None : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                                        updated = System.Text.RegularExpressions.Regex.Replace(original, query, replace, options);
                                    }
                                    else
                                    {
                                        updated = original.Replace(query, replace, StringComparison.OrdinalIgnoreCase);
                                    }
                                    lines[res.LineNumber - 1] = updated;
                                }
                            }
                            await File.WriteAllLinesAsync(filePath, lines);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed replace in {filePath}: {ex.Message}");
                    }
                }

                _searchResultsList.Clear();
                ShowErrorMessage("치환 완료", "모든 매칭 항목의 치환 처리가 완료되었습니다.");
                await RefreshGitStatusUIAsync();
            }
        }

        private async Task ReplaceInLargeFileAsync(string filePath, List<SearchResultItem> results, string query, string replace)
        {
            string tempPath = Path.Combine(Path.GetDirectoryName(filePath) ?? Path.GetTempPath(), $"._{Path.GetFileName(filePath)}.tmp");
            string backupPath = filePath + ".bak";

            try
            {
                var sorted = results.OrderBy(r => r.LineNumber).ToList();
                int idx = 0;

                using (var reader = new StreamReader(filePath))
                using (var writer = new StreamWriter(tempPath, false, Encoding.UTF8))
                {
                    string? line;
                    int lineNum = 1;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (idx < sorted.Count && sorted[idx].LineNumber == lineNum)
                        {
                            string updated;
                            if (SearchRegexToggle.IsChecked == true)
                            {
                                var options = SearchMatchCaseToggle.IsChecked == true ? System.Text.RegularExpressions.RegexOptions.None : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                                updated = System.Text.RegularExpressions.Regex.Replace(line, query, replace, options);
                            }
                            else
                            {
                                updated = line.Replace(query, replace, StringComparison.OrdinalIgnoreCase);
                            }
                            await writer.WriteLineAsync(updated);
                            idx++;
                        }
                        else
                        {
                            await writer.WriteLineAsync(line);
                        }
                        lineNum++;
                    }
                }

                File.Replace(tempPath, filePath, backupPath);
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw new IOException($"대용량 치환 중 실패: {ex.Message}", ex);
            }
        }

        private async void OnSearchResultDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (SearchResultsList.SelectedItem is SearchResultItem item)
            {
                // Open file
                await LoadFileIntoTabAsync(item.Path);

                // Wait editor loading and reveal line
                if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                    activeTabItem.Tag is string tabId &&
                    _tabBridges.TryGetValue(tabId, out var bridgeGroup) &&
                    bridgeGroup.Bridge != null)
                {
                    await bridgeGroup.Bridge.RevealLineAsync(item.LineNumber);
                }
            }
        }

        #endregion
    }

    public class FavoriteItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public class GitFileItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string ActionGlyph { get; set; } = string.Empty;
        public bool IsStaged { get; set; }
    }

    public class SearchResultItem
    {
        public string HeaderText => $"{System.IO.Path.GetFileName(Path)}:L{LineNumber}";
        public string DisplayPath => Path;
        public string Path { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string LineContent { get; set; } = string.Empty;
        public int IndexOfMatch { get; set; }
        public int MatchLength { get; set; }
    }
}
