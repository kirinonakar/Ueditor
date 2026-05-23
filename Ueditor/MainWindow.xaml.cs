using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
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
        private readonly ObservableCollection<RecentFileItem> _recentFilesList = new ObservableCollection<RecentFileItem>();
        private readonly string _recentFilesFilePath;
        private readonly ObservableCollection<SnippetItem> _snippetsList = new ObservableCollection<SnippetItem>();
        private readonly ObservableCollection<GitFileItem> _gitFilesList = new ObservableCollection<GitFileItem>();
        private readonly ObservableCollection<SearchResultItem> _searchResultsList = new ObservableCollection<SearchResultItem>();
        private readonly ObservableCollection<ExplorerItem> _explorerItems = new ObservableCollection<ExplorerItem>();
        private string _lastSelectionText = string.Empty;
        private string _lastSearchQuery = string.Empty;
        private string _currentFolderPath = string.Empty;
        private string _currentRepoPath = string.Empty;
        private Process? _terminalProcess;
        private string _terminalWorkingDirectory = string.Empty;
        
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
        private double _lastExplorerWidth = 260;
        private double _lastPreviewWidth = 400;
        private string _lastTextColorHex = "#E53935";
        private const double ExplorerPanelMinWidth = 150;
        private const double PreviewPanelMinWidth = 150;
        private static IReadOnlyList<string>? _installedFontFamiliesCache;


        public MainWindow()
        {
            this.InitializeComponent();
            SetWindowIcon();

            _fileService = new FileService();
            _settingsService = new SettingsService();
            _credentialService = new CredentialService();
            _llmService = new LLMService(_settingsService, _credentialService);
            _gitService = new GitService();
            _snippetService = new SnippetService();

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string settingsDir = Path.Combine(userProfile, ".ueditor");
            _recentFilesFilePath = Path.Combine(settingsDir, "recent_files.json");

            // Bind Left Sidebar Tab items
            FileListView.ItemsSource = _explorerItems;
            FavoritesListView.ItemsSource = _favoritesList;
            RecentFilesListView.ItemsSource = _recentFilesList;
            SnippetsListView.ItemsSource = _snippetsList;
            GitChangedFilesList.ItemsSource = _gitFilesList;
            SearchResultsList.ItemsSource = _searchResultsList;

            // Initialize Preview Debounce Timer (50ms for near-real-time preview)
            _previewDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _previewDebounceTimer.Tick += OnPreviewDebounceTimerTick;

            // Load local configurations and boot initial states
            // Setup custom title bar
            SetupCustomTitleBar();

            this.Activated += OnWindowActivated;
            this.Closed += OnWindowClosed;
            this.AppWindow.Closing += OnAppWindowClosing;
        }

        private void SetupCustomTitleBar()
        {
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            StopEmbeddedTerminal();
        }

        private void SetWindowIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Ueditor.ico");
                if (File.Exists(iconPath))
                {
                    AppWindow.SetIcon(iconPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set window icon: {ex.Message}");
            }
        }

        private async void OnWindowActivated(object sender, WindowActivatedEventArgs e)
        {
            this.Activated -= OnWindowActivated;
            
            // 1. Handle command-line file opening or open a blank tab instantly
            string[] args = Environment.GetCommandLineArgs();
            var filesToOpen = new List<string>();

            if (args != null && args.Length > 1)
            {
                for (int i = 1; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg.StartsWith("-") || arg.StartsWith("/"))
                    {
                        continue;
                    }

                    try
                    {
                        string filePath = arg.Trim('"', '\'');
                        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                        {
                            filesToOpen.Add(filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to pre-check command-line file '{arg}': {ex.Message}");
                    }
                }
            }

            if (filesToOpen.Count > 0)
            {
                foreach (var filePath in filesToOpen)
                {
                    // Read file size synchronously to decide whether to open normally or in large file mode
                    long fileSizeBytes = 0;
                    try
                    {
                        var fi = new FileInfo(filePath);
                        fileSizeBytes = fi.Length;
                    }
                    catch { }

                    // We use standard threshold since settings aren't loaded yet (default is 50MB, but let's be conservative, e.g., 20MB)
                    long defaultThresholdBytes = 20 * 1024 * 1024; 
                    if (fileSizeBytes >= defaultThresholdBytes)
                    {
                        // For large files, fall back to the async LoadFileIntoTabAsync flow which shows the dialog
                        _ = LoadFileIntoTabAsync(filePath);
                    }
                    else
                    {
                        // Open the tab instantly with blank content so it is visible immediately as the window opens
                        OpenNewTab(filePath, "");

                        // Read the content asynchronously in the background and populate it once loaded
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                string content = await _fileService.ReadTextFileAsync(filePath);
                                this.DispatcherQueue.TryEnqueue(async () =>
                                {
                                    var tab = _tabs.FirstOrDefault(t => t.FilePath == filePath);
                                    if (tab != null)
                                    {
                                        tab.Content = content;
                                        if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                                        {
                                            await bridgeGroup.Bridge.SetTextAsync(content);
                                        }
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to load file '{filePath}' asynchronously: {ex.Message}");
                            }
                        });

                        // Set Git repository root if applicable
                        try
                        {
                            string? repoRoot = FindGitRepositoryRoot(Path.GetDirectoryName(filePath));
                            if (!string.IsNullOrEmpty(repoRoot))
                            {
                                _currentRepoPath = repoRoot;
                            }
                        }
                        catch { }
                    }
                }
            }
            else
            {
                // Open a blank tab instantly (so the tab and Monaco editor container are rendered immediately)
                OpenNewTab();
            }

            // 2. Load settings JSON and initialize preview panel WebView2 in the background
            await _settingsService.LoadSettingsAsync();
            WordWrapToggle.IsChecked = _settingsService.CurrentSettings.WordWrap;
            LeftPanelToggle.IsChecked = true;
            RightPanelToggle.IsChecked = _settingsService.CurrentSettings.DefaultMarkdownEnabled;
            ApplyPreviewVisibility(_settingsService.CurrentSettings.DefaultMarkdownEnabled);
            MarkdownToolbarToggle.IsChecked = _settingsService.CurrentSettings.DefaultMarkdownToolbarEnabled;
            MarkdownToolbar.Visibility = _settingsService.CurrentSettings.DefaultMarkdownToolbarEnabled ? Visibility.Visible : Visibility.Collapsed;
            PreviewModeCombo.SelectedIndex = _settingsService.CurrentSettings.PreviewMode switch
            {
                "HTML" => 1,
                "LaTeX" => 2,
                _ => 0
            };
            ApplyUiPersonalization(_settingsService.CurrentSettings);

            // If we have a Git repo path from a loaded file, refresh Git status UI
            if (!string.IsNullOrEmpty(_currentRepoPath))
            {
                _ = RefreshGitStatusUIAsync();
            }

            await InitializePreviewWebViewAsync();

            // 3. Load Snippets, Favorites and Recent Files
            await _snippetService.LoadSnippetsAsync();
            RefreshSnippetsUI();
            RefreshFavoritesUI();
            LoadRecentFiles();

            // 4. Initialize text color button with default color
            if (TryParseHexColor(_lastTextColorHex, out var defaultColor))
            {
                UpdateTextColorButtonVisual(defaultColor);
            }
        }

        #region WebView2 Host Resource Mapping & Preview Init

        private async Task InitializePreviewWebViewAsync()
        {
            try
            {
                PreviewWebView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
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

        private void OpenNewTab(string? filePath = null, string content = "", bool isLargeFileMode = false, bool isMonacoLimitedMode = false, bool isReadOnly = false)
        {
            var tab = new OpenedTab();
            tab.IsLargeFileMode = isLargeFileMode;

            // Auto-enforce read-only mode for .diff files
            if (filePath != null && filePath.EndsWith(".diff", StringComparison.OrdinalIgnoreCase))
            {
                isReadOnly = true;
            }

            if (filePath != null)
            {
                tab.FilePath = filePath;
                tab.Title = Path.GetFileName(filePath);
                tab.Content = content;
                tab.Language = GetMonacoLanguageName(filePath);
                AddRecentFile(filePath);
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
                VerticalAlignment = VerticalAlignment.Stretch,
                DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0)
            };
            grid.Children.Add(editorWebView);

            // Instantiate TabViewItem XAML element
            var tabItem = new TabViewItem
            {
                Header = tab.DisplayTitle,
                Content = grid,
                Tag = tab.Id
            };

            // Apply UI font directly to TabViewItem to guarantee visual style consistency
            try
            {
                if (!string.IsNullOrEmpty(_settingsService.CurrentSettings.UiFontFamily))
                {
                    tabItem.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(_settingsService.CurrentSettings.UiFontFamily);
                }
            }
            catch { }

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

                bridge.ShortcutPressed += (shortcutName) =>
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        switch (shortcutName)
                        {
                            case "save":
                                OnSaveFileClick(this, new RoutedEventArgs());
                                break;
                            case "open":
                                OnOpenFileClick(this, new RoutedEventArgs());
                                break;
                            case "terminal":
                                OnOpenTerminalClick(this, new RoutedEventArgs());
                                break;
                            case "closeTab":
                                OnCloseActiveTabShortcutInvoked(null!, null!);
                                break;
                            case "searchAll":
                                EnsureLeftPanelVisible();
                                ShowLeftSidebarPage(4);
                                this.DispatcherQueue.TryEnqueue(() =>
                                {
                                    SearchQueryInput.Focus(FocusState.Programmatic);
                                    SearchQueryInput.Focus(FocusState.Keyboard);
                                });
                                break;
                        }
                    });
                };

                // Register Bridge Initialization & IPC Events
                bridge.EditorReady += async () =>
                {
                    await bridge.SetTextAsync(tab.Content);
                    await bridge.SetLanguageAsync(filePath ?? "file.txt");
                    await bridge.UpdateOptionsAsync(_settingsService.CurrentSettings, isLargeFile: isMonacoLimitedMode, isReadOnly: isReadOnly);
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

                    // Real-time language detection
                    UpdateLanguageUI(tab);
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
                        string json = NormalizeWebMessageJson(args);
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
                            fontSize = _settingsService.CurrentSettings.FontSize,
                            fontFamily = _settingsService.CurrentSettings.FontFamily,
                            customBackgroundColor = _settingsService.CurrentSettings.CustomBackgroundColor,
                            customForegroundColor = _settingsService.CurrentSettings.CustomForegroundColor,
                            readOnly = true
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
                ".diff" => "diff",
                ".cs" => "csharp",
                ".js" => "javascript",
                ".ts" => "typescript",
                ".css" => "css",
                ".json" => "json",
                ".py" => "python",
                ".cpp" => "cpp",
                ".h" => "cpp",
                ".xml" => "xml",
                ".xaml" => "xml",
                ".sql" => "sql",
                ".sh" => "shell",
                ".rs" => "rust",
                ".go" => "go",
                ".yml" => "yaml",
                ".yaml" => "yaml",
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
                    theme = _settingsService.CurrentSettings.Theme,
                    customBackgroundColor = _settingsService.CurrentSettings.CustomBackgroundColor,
                    customForegroundColor = _settingsService.CurrentSettings.CustomForegroundColor,
                    uiFontFamily = _settingsService.CurrentSettings.UiFontFamily
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
            picker.FileTypeFilter.Add(".ts");
            picker.FileTypeFilter.Add(".cs");
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".tex");
            picker.FileTypeFilter.Add(".py");
            picker.FileTypeFilter.Add(".cpp");
            picker.FileTypeFilter.Add(".h");
            picker.FileTypeFilter.Add(".xml");
            picker.FileTypeFilter.Add(".xaml");
            picker.FileTypeFilter.Add(".sql");
            picker.FileTypeFilter.Add(".sh");
            picker.FileTypeFilter.Add(".diff");

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
                string? repoRoot = FindGitRepositoryRoot(Path.GetDirectoryName(filePath));
                if (!string.IsNullOrEmpty(repoRoot))
                {
                    _currentRepoPath = repoRoot;
                    await RefreshGitStatusUIAsync();
                }

                var largeInfo = await _fileService.GetLargeFileInfoAsync(filePath);
                var settings = _settingsService.CurrentSettings;
                long thresholdBytes = settings.LargeFileThresholdMB * 1024 * 1024;
                long fileSizeBytes = largeInfo.FileSize;

                if (fileSizeBytes >= 200 * 1024 * 1024)
                {
                    // 200MB 이상 초대용량 파일의 경우 강제로 Large File Mode 로드
                    var dialog = new ContentDialog
                    {
                        Title = "초대용량 파일 감지",
                        Content = $"선택한 파일의 크기가 {fileSizeBytes / (1024 * 1024.0):F2}MB 입니다.\n시스템 리소스 보호와 에디터 안정성을 위해 Large File Mode(가상 스크롤 뷰어)로 안전하게 열립니다.",
                        CloseButtonText = "확인",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await dialog.ShowAsync();

                    StatusMode.Text = "대용량 모드";
                    OpenNewTab(filePath, "", isLargeFileMode: true);
                    return;
                }
                else if (fileSizeBytes >= thresholdBytes)
                {
                    // 설정 임계값 이상인 경우 경고 창을 띄워 선택 유도
                    var dialog = new ContentDialog
                    {
                        Title = "대용량 파일 경고",
                        Content = $"선택한 파일의 크기가 {fileSizeBytes / (1024 * 1024.0):F2}MB 입니다.\nMonaco Editor 대신 Large File Mode(가상 스크롤 뷰어)로 여시겠습니까?",
                        PrimaryButtonText = "대용량 모드로 열기",
                        SecondaryButtonText = "일반 Monaco(제한 모드)로 강제 열기",
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
                    else if (result == ContentDialogResult.Secondary)
                    {
                        StatusMode.Text = "일반 모드 (제한)";
                        string content = await _fileService.ReadTextFileAsync(filePath);
                        OpenNewTab(filePath, content, isLargeFileMode: false, isMonacoLimitedMode: true);
                        return;
                    }
                    else
                    {
                        return; // Canceled
                    }
                }

                StatusMode.Text = "일반 모드";
                string contentNormal = await _fileService.ReadTextFileAsync(filePath);
                OpenNewTab(filePath, contentNormal);
            }
            catch (Exception ex)
            {
                ShowErrorMessage("파일 로드 에러", ex.Message);
            }
        }

        private void OnRootDragOver(object sender, DragEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "파일 열기";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsContentVisible = true;

                // Show DragOverlay Grid immediately to intercept drops away from WebView2
                if (DragOverlay != null)
                {
                    DragOverlay.Visibility = Visibility.Visible;
                }
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }
        }

        private void OnDragOverlayOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "파일 열기";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
        }

        private void OnDragOverlayDrop(object sender, DragEventArgs e)
        {
            if (DragOverlay != null)
            {
                DragOverlay.Visibility = Visibility.Collapsed;
            }
            OnRootDrop(sender, e);
        }

        private void OnDragOverlayLeave(object sender, DragEventArgs e)
        {
            if (DragOverlay != null)
            {
                DragOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void OnRootDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.AcceptedOperation = DataPackageOperation.Copy;

            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Path))
                    {
                        continue;
                    }

                    if (File.Exists(item.Path))
                    {
                        await LoadFileIntoTabAsync(item.Path);
                    }
                    else if (Directory.Exists(item.Path))
                    {
                        _currentRepoPath = FindGitRepositoryRoot(item.Path) ?? string.Empty;
                        LoadDirectoryRoot(item.Path);
                        await RefreshGitStatusUIAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage("드래그 앤 드롭 오류", ex.Message);
            }
        }

        private async void OnSaveFileClick(object sender, RoutedEventArgs e)
        {
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId)
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null)
                {
                    await SaveTabAsync(tab);
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
                if (bridgeGroup.Bridge != null)
                {
                    await bridgeGroup.Bridge.UpdateOptionsAsync(settings);
                }
                else if (bridgeGroup.WebView?.CoreWebView2 != null)
                {
                    var updateMsg = new
                    {
                        action = "updateOptions",
                        theme = settings.Theme,
                        fontSize = settings.FontSize,
                        fontFamily = settings.FontFamily,
                        customBackgroundColor = settings.CustomBackgroundColor,
                        customForegroundColor = settings.CustomForegroundColor,
                        readOnly = true
                    };
                    bridgeGroup.WebView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(updateMsg));
                }
            }
        }

        private async void OnFindClick(object sender, RoutedEventArgs e)
        {
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId &&
                _tabBridges.TryGetValue(tabId, out var bridgeGroup))
            {
                if (bridgeGroup.Bridge != null)
                {
                    bridgeGroup.WebView.Focus(FocusState.Programmatic);
                    await bridgeGroup.Bridge.TriggerFindAsync();
                    return;
                }
            }

            EnsureLeftPanelVisible();
            ShowLeftSidebarPage(4);
            SearchQueryInput.Focus(FocusState.Programmatic);
            SearchQueryInput.Focus(FocusState.Keyboard);
        }

        private void OnLeftActivityClick(object sender, RoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton button &&
                int.TryParse(button.Tag?.ToString(), out int index))
            {
                ShowLeftSidebarPage(index);
            }
        }

        private void ShowLeftSidebarPage(int index)
        {
            UIElement[] pages =
            {
                ExplorerSidebarPage,
                FavoritesSidebarPage,
                SnippetsSidebarPage,
                GitSidebarPage,
                SearchSidebarPage,
                RecentSidebarPage
            };

            Microsoft.UI.Xaml.Controls.Primitives.ToggleButton[] buttons =
            {
                ExplorerActivityButton,
                FavoritesActivityButton,
                SnippetsActivityButton,
                GitActivityButton,
                SearchActivityButton,
                RecentActivityButton
            };

            int safeIndex = Math.Clamp(index, 0, pages.Length - 1);
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visibility = i == safeIndex ? Visibility.Visible : Visibility.Collapsed;
                buttons[i].IsChecked = i == safeIndex;
            }

            if (safeIndex == 4)
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    SearchQueryInput.Focus(FocusState.Programmatic);
                    SearchQueryInput.Focus(FocusState.Keyboard);
                });
            }
        }

        private void EnsureLeftPanelVisible()
        {
            if (LeftPanelToggle.IsChecked == true && LeftSidebarTabView.Visibility == Visibility.Visible)
            {
                return;
            }

            LeftPanelToggle.IsChecked = true;
            ExplorerColumn.MinWidth = ExplorerPanelMinWidth;
            ExplorerColumn.Width = new GridLength(Math.Max(_lastExplorerWidth, ExplorerColumn.MinWidth));
            LeftSplitter.Visibility = Visibility.Visible;
            LeftSidebarTabView.Visibility = Visibility.Visible;
        }

        private void OnToggleLeftPanelClick(object sender, RoutedEventArgs e)
        {
            bool show = LeftPanelToggle.IsChecked == true;
            if (show)
            {
                ExplorerColumn.MinWidth = ExplorerPanelMinWidth;
                ExplorerColumn.Width = new GridLength(Math.Max(_lastExplorerWidth, ExplorerColumn.MinWidth));
                LeftSplitter.Visibility = Visibility.Visible;
                LeftSidebarTabView.Visibility = Visibility.Visible;
            }
            else
            {
                double currentWidth = LeftSidebarTabView.ActualWidth > 0 ? LeftSidebarTabView.ActualWidth : ExplorerColumn.Width.Value;
                if (currentWidth > 0)
                {
                    _lastExplorerWidth = currentWidth;
                }
                ExplorerColumn.MinWidth = 0;
                ExplorerColumn.Width = new GridLength(0);
                LeftSplitter.Visibility = Visibility.Collapsed;
                LeftSidebarTabView.Visibility = Visibility.Collapsed;
            }
        }

        private void OnTogglePreviewClick(object sender, RoutedEventArgs e)
        {
            ApplyPreviewVisibility(RightPanelToggle.IsChecked == true);
        }

        private async void OnToggleThemeClick(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.CurrentSettings;
            settings.Theme = settings.Theme == "Light" ? "Dark" : "Light";
            await _settingsService.SaveSettingsAsync(settings);
            ApplyUiPersonalization(settings);

            foreach (var grp in _tabBridges.Values)
            {
                if (grp.Bridge != null)
                {
                    await grp.Bridge.UpdateOptionsAsync(settings);
                }
                else if (grp.WebView?.CoreWebView2 != null)
                {
                    var updateMsg = new
                    {
                            action = "updateOptions",
                            theme = settings.Theme,
                            fontSize = settings.FontSize,
                            fontFamily = settings.FontFamily,
                            customBackgroundColor = settings.CustomBackgroundColor,
                            customForegroundColor = settings.CustomForegroundColor,
                            readOnly = true
                        };
                    grp.WebView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(updateMsg));
                }
            }

            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId)
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null) UpdateLivePreview(tab);
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
            
            var fontFamilies = GetInstalledFontFamilies();
            var fontFamilyCombo = CreateFontComboBox(settings.FontFamily, fontFamilies);
            var uiFontFamilyCombo = CreateFontComboBox(settings.UiFontFamily, fontFamilies);
            var customBgCheck = new CheckBox { Content = "커스텀 에디터 배경색 사용", IsChecked = !string.IsNullOrWhiteSpace(settings.CustomBackgroundColor) };
            var customFgCheck = new CheckBox { Content = "커스텀 에디터 글자색 사용", IsChecked = !string.IsNullOrWhiteSpace(settings.CustomForegroundColor) };
            var customBgPicker = new ColorPicker
            {
                Color = ResolvePickerColor(settings.CustomBackgroundColor, settings.Theme == "Light" ? "#ffffff" : "#1e1e1e"),
                IsAlphaEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var customFgPicker = new ColorPicker
            {
                Color = ResolvePickerColor(settings.CustomForegroundColor, settings.Theme == "Light" ? "#111111" : "#d4d4d4"),
                IsAlphaEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            customBgPicker.IsEnabled = customBgCheck.IsChecked == true;
            customFgPicker.IsEnabled = customFgCheck.IsChecked == true;
            customBgCheck.Checked += (_, __) => customBgPicker.IsEnabled = true;
            customBgCheck.Unchecked += (_, __) => customBgPicker.IsEnabled = false;
            customFgCheck.Checked += (_, __) => customFgPicker.IsEnabled = true;
            customFgCheck.Unchecked += (_, __) => customFgPicker.IsEnabled = false;
            var wordWrapCheck = new CheckBox { Content = "기본 Word Wrap 켜기", IsChecked = settings.WordWrap };
            var minimapCheck = new CheckBox { Content = "미니맵 표시 (로컬 Monaco 번들 사용 시)", IsChecked = settings.MinimapEnabled };
            var bracketPairCheck = new CheckBox { Content = "Bracket pair colorization (로컬 Monaco 번들 사용 시)", IsChecked = settings.BracketPairColorizationEnabled };
            var autoSaveCheck = new CheckBox { Content = "Autosave 사용", IsChecked = settings.AutoSave };
            var defaultMarkdownCheck = new CheckBox { Content = "실시간 미리보기 기본 활성화", IsChecked = settings.DefaultMarkdownEnabled };
            var defaultMarkdownToolbarCheck = new CheckBox { Content = "기본 마크다운 툴바 활성화", IsChecked = settings.DefaultMarkdownToolbarEnabled };
            var tabSizeBox = new TextBox { PlaceholderText = "예: 4", Text = settings.TabSize.ToString(), HorizontalAlignment = HorizontalAlignment.Stretch };
            var largeThresholdBox = new TextBox { PlaceholderText = "예: 50", Text = settings.LargeFileThresholdMB.ToString(), HorizontalAlignment = HorizontalAlignment.Stretch };

            // LLM Settings Injection
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
            
            // Async load the stored key for currently selected provider
            string initialKey = await _llmService.GetApiKeyAsync(providerNames[providerIndex]);
            llmApiKeyBox.Password = initialKey;

            var refreshLmStudioModelsButton = new Button { Content = "LM Studio 모델 불러오기", HorizontalAlignment = HorizontalAlignment.Stretch };
            var llmModelStatusText = new TextBlock
            {
                Text = "LM Studio는 서버가 켜져 있을 때 http://localhost:1234/v1/models 에서 모델 목록을 불러옵니다.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11
            };

            string GetSelectedProviderName()
            {
                return llmProviderCombo.SelectedItem as string ?? "OpenAI";
            }

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

                if (provider.Equals("LM Studio", StringComparison.OrdinalIgnoreCase))
                {
                    llmEndpointBox.Text = "http://localhost:1234/v1";
                }
                else if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                {
                    llmEndpointBox.Text = "https://api.openai.com/v1";
                }
                else if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                {
                    llmEndpointBox.Text = "https://generativelanguage.googleapis.com";
                }
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

                // Dynamic API Key Loading per provider
                string key = await _llmService.GetApiKeyAsync(provider);
                llmApiKeyBox.Password = key;
            };

            refreshLmStudioModelsButton.Click += async (_, __) => await RefreshLmStudioModelsAsync();

            StackPanel CreateSection()
            {
                return new StackPanel { Spacing = 10, Width = 460, Padding = new Thickness(2, 8, 2, 2) };
            }

            void AddLabel(StackPanel target, string text)
            {
                target.Children.Add(new TextBlock { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            }

            var appearanceSection = CreateSection();
            AddLabel(appearanceSection, "앱/에디터 테마");
            appearanceSection.Children.Add(themeCombo);
            AddLabel(appearanceSection, $"에디터 글자 크기 ({settings.FontSize:0}pt)");
            appearanceSection.Children.Add(sizeSlider);
            AddLabel(appearanceSection, "에디터 폰트");
            appearanceSection.Children.Add(fontFamilyCombo);
            AddLabel(appearanceSection, "UI 쉘 폰트");
            appearanceSection.Children.Add(uiFontFamilyCombo);
            appearanceSection.Children.Add(customBgCheck);
            appearanceSection.Children.Add(customBgPicker);
            appearanceSection.Children.Add(customFgCheck);
            appearanceSection.Children.Add(customFgPicker);

            var editorSection = CreateSection();
            editorSection.Children.Add(wordWrapCheck);
            editorSection.Children.Add(minimapCheck);
            editorSection.Children.Add(bracketPairCheck);
            editorSection.Children.Add(autoSaveCheck);
            editorSection.Children.Add(defaultMarkdownCheck);
            editorSection.Children.Add(defaultMarkdownToolbarCheck);
            AddLabel(editorSection, "Tab size");
            editorSection.Children.Add(tabSizeBox);
            AddLabel(editorSection, "Large File Mode 제안 기준 (MB)");
            editorSection.Children.Add(largeThresholdBox);

            var llmSection = CreateSection();
            AddLabel(llmSection, "LLM 공급자");
            llmSection.Children.Add(llmProviderCombo);
            AddLabel(llmSection, "LLM API Endpoint");
            llmSection.Children.Add(llmEndpointBox);
            AddLabel(llmSection, "LLM 모델명");
            llmSection.Children.Add(llmModelCombo);
            llmSection.Children.Add(refreshLmStudioModelsButton);
            llmSection.Children.Add(llmModelStatusText);
            AddLabel(llmSection, "LLM API Key");
            llmSection.Children.Add(llmApiKeyBox);
            llmSection.Children.Add(new TextBlock
            {
                Text = "API Key는 설정 파일에 저장하지 않고 Windows 자격 증명 관리자에 저장합니다. 비워두고 저장하면 기존 Key를 유지합니다. LM Studio는 기본 로컬 서버 설정에서 API Key 없이 사용할 수 있습니다.",
                TextWrapping = TextWrapping.Wrap
            });

            var settingsPivot = new Pivot { Width = 500, Height = 440 };
            settingsPivot.Items.Add(new PivotItem { Header = "모양", Content = new ScrollViewer { Content = appearanceSection } });
            settingsPivot.Items.Add(new PivotItem { Header = "편집", Content = new ScrollViewer { Content = editorSection } });
            settingsPivot.Items.Add(new PivotItem { Header = "LLM", Content = new ScrollViewer { Content = llmSection } });

            var dialog = new ContentDialog
            {
                Title = "Ueditor 설정",
                Content = settingsPivot,
                PrimaryButtonText = "적용 및 저장",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
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
                
                // Save to provider-specific field
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
                settings.DefaultMarkdownToolbarEnabled = defaultMarkdownToolbarCheck.IsChecked == true;
                
                string newApiKey = llmApiKeyBox.Password.Trim();
                await _llmService.SaveApiKeyAsync(settings.LlmProvider, newApiKey);
                if (string.IsNullOrEmpty(newApiKey))
                {
                    LlmOutputText.Text = $"{settings.LlmProvider} API Key가 Windows 자격 증명 저장소에서 삭제되었습니다.";
                }
                else
                {
                    LlmOutputText.Text = $"{settings.LlmProvider} API Key가 Windows 자격 증명 저장소에 저장되었습니다.";
                }

                await _settingsService.SaveSettingsAsync(settings);
                ApplyPreviewVisibility(settings.DefaultMarkdownEnabled);
                MarkdownToolbarToggle.IsChecked = settings.DefaultMarkdownToolbarEnabled;
                MarkdownToolbar.Visibility = settings.DefaultMarkdownToolbarEnabled ? Visibility.Visible : Visibility.Collapsed;
                WordWrapToggle.IsChecked = settings.WordWrap;
                ApplyUiPersonalization(settings);

                // Update settings for all active Monaco and Large File editors
                foreach (var grp in _tabBridges.Values)
                {
                    if (grp.Bridge != null)
                    {
                        await grp.Bridge.UpdateOptionsAsync(settings);
                    }
                    else if (grp.WebView != null && grp.WebView.CoreWebView2 != null)
                    {
                        var updateMsg = new
                        {
                            action = "updateOptions",
                            theme = settings.Theme,
                            fontSize = settings.FontSize,
                            fontFamily = settings.FontFamily,
                            customBackgroundColor = settings.CustomBackgroundColor,
                            customForegroundColor = settings.CustomForegroundColor,
                            readOnly = true
                        };
                        string updateJson = System.Text.Json.JsonSerializer.Serialize(updateMsg);
                        grp.WebView.CoreWebView2.PostWebMessageAsJson(updateJson);
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
                _currentFolderPath = folder.Path;
                _currentRepoPath = FindGitRepositoryRoot(folder.Path) ?? string.Empty;
                LoadDirectoryRoot(folder.Path);

                // Trigger Git branch detection & status update
                await RefreshGitStatusUIAsync();
            }
        }

        private void LoadDirectoryRoot(string folderPath)
        {
            _explorerItems.Clear();
            _currentFolderPath = folderPath;

            foreach (var item in CreateDirectoryItems(folderPath))
            {
                _explorerItems.Add(item);
            }

            ExplorerStatusText.Text = $"{folderPath}\n{_explorerItems.Count:N0}개 항목";
        }

        private async Task NavigateExplorerToFolderAsync(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

            _currentFolderPath = folderPath;
            _currentRepoPath = FindGitRepositoryRoot(folderPath) ?? string.Empty;
            LoadDirectoryRoot(folderPath);

            // Ensure the left panel is visible and switch to Explorer page (index 0)
            EnsureLeftPanelVisible();
            ShowLeftSidebarPage(0);

            await RefreshGitStatusUIAsync();
        }

        private void OnOpenTerminalClick(object sender, RoutedEventArgs e)
        {
            string workingDirectory = GetTerminalWorkingDirectory();
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                ShowErrorMessage("터미널 오류", "터미널을 열 폴더를 찾을 수 없습니다. 먼저 폴더를 선택해 주세요.");
                if (TerminalToggleButton != null) TerminalToggleButton.IsChecked = false;
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowErrorMessage("터미널 실행 실패", $"터미널을 열지 못했습니다: {ex.Message}");
            }

            if (TerminalToggleButton != null) TerminalToggleButton.IsChecked = false;
        }

        private string GetTerminalWorkingDirectory()
        {
            if (FileListView.SelectedItem is ExplorerItem selectedItem)
            {
                if (selectedItem.IsFolder && Directory.Exists(selectedItem.Path))
                {
                    return selectedItem.Path;
                }

                string? selectedFileDirectory = Path.GetDirectoryName(selectedItem.Path);
                if (!string.IsNullOrWhiteSpace(selectedFileDirectory) && Directory.Exists(selectedFileDirectory))
                {
                    return selectedFileDirectory;
                }
            }

            if (!string.IsNullOrWhiteSpace(_currentFolderPath) && Directory.Exists(_currentFolderPath))
            {
                return _currentFolderPath;
            }

            if (!string.IsNullOrWhiteSpace(_currentRepoPath) && Directory.Exists(_currentRepoPath))
            {
                return _currentRepoPath;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        #endregion

        #region Interactive Terminal Embedding

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_CHILD = 0x40000000;

        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private IntPtr _terminalWindowHandle = IntPtr.Zero;

        private void OpenEmbeddedTerminal(string workingDirectory)
        {
            TerminalPanelRow.Height = new GridLength(220);
            TerminalPanel.Visibility = Visibility.Visible;
            TerminalTitleText.Text = $"터미널 - {workingDirectory}";

            if (_terminalProcess != null && !_terminalProcess.HasExited && _terminalWorkingDirectory.Equals(workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                if (_terminalWindowHandle != IntPtr.Zero)
                {
                    const int SW_SHOW = 5;
                    ShowWindow(_terminalWindowHandle, SW_SHOW);
                    ResizeEmbeddedTerminal();
                }
                else
                {
                    TerminalInputBox.Focus(FocusState.Programmatic);
                }
                return;
            }

            StartEmbeddedTerminal(workingDirectory);
        }

        private async void StartEmbeddedTerminal(string workingDirectory)
        {
            StopEmbeddedTerminal();
            _terminalWorkingDirectory = workingDirectory;

            // Show native terminal border container and hide old textBox controls
            TerminalHostBorder.Visibility = Visibility.Visible;
            TerminalOutputTextBox.Visibility = Visibility.Collapsed;
            TerminalInputAreaGrid.Visibility = Visibility.Collapsed;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -Command \"$Host.UI.RawUI.WindowTitle = 'Ueditor_Console_{Process.GetCurrentProcess().Id}'\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                };

                _terminalProcess = Process.Start(startInfo);
                if (_terminalProcess == null)
                {
                    throw new Exception("PowerShell native process failed to start.");
                }

                IntPtr childHwnd = IntPtr.Zero;
                string targetTitle = $"Ueditor_Console_{Process.GetCurrentProcess().Id}";

                // Wait up to 3 seconds for console window creation
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(100);
                    childHwnd = FindWindow("ConsoleWindowClass", targetTitle);
                    if (childHwnd != IntPtr.Zero)
                    {
                        break;
                    }
                }

                if (childHwnd == IntPtr.Zero)
                {
                    _terminalProcess.Refresh();
                    childHwnd = _terminalProcess.MainWindowHandle;
                }

                if (childHwnd == IntPtr.Zero)
                {
                    throw new Exception("Native terminal window handle could not be resolved.");
                }

                _terminalWindowHandle = childHwnd;

                // Reparent console window into WinUI 3 Window HWND
                IntPtr parentHwnd = WindowNative.GetWindowHandle(this);
                SetParent(_terminalWindowHandle, parentHwnd);

                // Override style properties to child borderless window
                int style = GetWindowLong(_terminalWindowHandle, GWL_STYLE);
                style = (style | WS_CHILD) & ~WS_CAPTION & ~WS_THICKFRAME & ~WS_MINIMIZEBOX & ~WS_MAXIMIZEBOX & ~WS_SYSMENU;
                SetWindowLong(_terminalWindowHandle, GWL_STYLE, style);

                // Force frame changed repaint update
                SetWindowPos(_terminalWindowHandle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOOWNERZORDER);

                // Synchronize size alignment
                ResizeEmbeddedTerminal();

                // Show window inside border
                const int SW_SHOW = 5;
                ShowWindow(_terminalWindowHandle, SW_SHOW);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed native terminal hosting, falling back to redirected textbox: {ex.Message}");
                
                // Graceful fallback UI restoration
                TerminalHostBorder.Visibility = Visibility.Collapsed;
                TerminalOutputTextBox.Visibility = Visibility.Visible;
                TerminalInputAreaGrid.Visibility = Visibility.Visible;

                StartRedirectedTerminal(workingDirectory);
            }
        }

        private void StartRedirectedTerminal(string workingDirectory)
        {
            StopEmbeddedTerminal();
            _terminalWorkingDirectory = workingDirectory;
            TerminalOutputTextBox.Text = $"PowerShell 시작 (리다이렉션 모드): {workingDirectory}\r\n";

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoLogo -NoProfile -NoExit -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; [Console]::InputEncoding=[System.Text.Encoding]::UTF8; $OutputEncoding=[System.Text.Encoding]::UTF8\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                _terminalProcess = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                _terminalProcess.OutputDataReceived += (_, args) => AppendTerminalOutput(args.Data);
                _terminalProcess.ErrorDataReceived += (_, args) => AppendTerminalOutput(args.Data);
                _terminalProcess.Exited += (_, __) => AppendTerminalOutput("[터미널 종료]");

                _terminalProcess.Start();
                _terminalProcess.BeginOutputReadLine();
                _terminalProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                AppendTerminalOutput($"터미널을 시작하지 못했습니다: {ex.Message}");
            }
        }

        private void StopEmbeddedTerminal()
        {
            try
            {
                _terminalWindowHandle = IntPtr.Zero;
                if (_terminalProcess != null)
                {
                    if (!_terminalProcess.HasExited)
                    {
                        try { _terminalProcess.StandardInput.WriteLine("exit"); } catch {}
                        if (!_terminalProcess.WaitForExit(300))
                        {
                            _terminalProcess.Kill();
                        }
                    }

                    _terminalProcess.Dispose();
                    _terminalProcess = null;
                }
            }
            catch
            {
                _terminalProcess = null;
            }
        }

        private void ResizeEmbeddedTerminal()
        {
            if (_terminalWindowHandle == IntPtr.Zero || TerminalHostBorder == null || TerminalHostBorder.Visibility != Visibility.Visible)
            {
                return;
            }

            try
            {
                var transform = TerminalHostBorder.TransformToVisual(this.Content);
                var bounds = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, TerminalHostBorder.ActualWidth, TerminalHostBorder.ActualHeight));

                int x = (int)bounds.X;
                int y = (int)bounds.Y;
                int width = (int)bounds.Width;
                int height = (int)bounds.Height;

                if (width > 0 && height > 0)
                {
                    MoveWindow(_terminalWindowHandle, x, y, width, height, true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to resize native terminal child window: {ex.Message}");
            }
        }

        private void OnTerminalHostBorderSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ResizeEmbeddedTerminal();
        }

        private void AppendTerminalOutput(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;

            DispatcherQueue.TryEnqueue(() =>
            {
                TerminalOutputTextBox.Text += text + Environment.NewLine;
                TerminalOutputTextBox.Select(TerminalOutputTextBox.Text.Length, 0);
            });
        }

        private void OnTerminalInputKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;

            string command = TerminalInputBox.Text;
            TerminalInputBox.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(command)) return;

            TerminalOutputTextBox.Text += $"> {command}{Environment.NewLine}";
            TerminalOutputTextBox.Select(TerminalOutputTextBox.Text.Length, 0);

            try
            {
                if (_terminalProcess == null || _terminalProcess.HasExited)
                {
                    StartRedirectedTerminal(GetTerminalWorkingDirectory());
                }

                _terminalProcess?.StandardInput.WriteLine(command);
                _terminalProcess?.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                AppendTerminalOutput($"명령 전송 실패: {ex.Message}");
            }
        }

        private void OnCloseTerminalClick(object sender, RoutedEventArgs e)
        {
            StopEmbeddedTerminal();
            TerminalPanel.Visibility = Visibility.Collapsed;
            TerminalPanelRow.Height = new GridLength(0);
        }

        private IEnumerable<ExplorerItem> CreateDirectoryItems(string parentPath)
        {
            var items = new List<ExplorerItem>();
            try
            {
                var dirInfo = new DirectoryInfo(parentPath);
                var enumerationOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false
                };

                // 1. Folders first
                foreach (var dir in dirInfo.EnumerateDirectories("*", enumerationOptions))
                {
                    // Ignore hidden directories like .git
                    if (dir.Attributes.HasFlag(FileAttributes.Hidden) || dir.Name.StartsWith("."))
                        continue;

                    items.Add(new ExplorerItem { Name = dir.Name, Path = dir.FullName, IsFolder = true });
                }

                // 2. Files next
                foreach (var file in dirInfo.EnumerateFiles("*", enumerationOptions))
                {
                    if (file.Attributes.HasFlag(FileAttributes.Hidden))
                        continue;

                    items.Add(new ExplorerItem { Name = file.Name, Path = file.FullName, IsFolder = false });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed reading folder hierarchy: {ex.Message}");
            }

            return items;
        }

        private void OnExplorerUpClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentFolderPath)) return;

            var parent = Directory.GetParent(_currentFolderPath);
            if (parent == null) return;

            _currentRepoPath = FindGitRepositoryRoot(parent.FullName) ?? string.Empty;
            LoadDirectoryRoot(parent.FullName);
        }

        private void OnFileListViewDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            var item = GetDataContextFromOriginalSource<ExplorerItem>(e.OriginalSource) ?? FileListView.SelectedItem as ExplorerItem;
            if (item == null) return;

            if (item.IsFolder)
            {
                _currentRepoPath = FindGitRepositoryRoot(item.Path) ?? string.Empty;
                LoadDirectoryRoot(item.Path);
            }
            else
            {
                // Open file in new Tab
                _ = LoadFileIntoTabAsync(item.Path);
            }
        }

        private void OnFileListViewItemRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ExplorerItem item })
            {
                FileListView.SelectedItem = item;
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
                bool saved = await SaveTabAsync(tab);
                if (saved)
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

        private async void OnEditorTabViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Reset selection context to prevent leak/cross-talk between documents
            _lastSelectionText = string.Empty;
            SelectionStatsText.Text = "선택 영역: 없음 (전체 전송 차단 활성화)";

            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId)
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null)
                {
                    UpdateStatusFileStats(tab);
                    UpdateLivePreview(tab);
                    UpdateLanguageUI(tab);

                    // Sync selection for active Monaco editor
                    if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                    {
                        await bridgeGroup.Bridge.RequestSelectionAsync();
                    }
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

        private async void OnPreviewModeComboSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settingsService != null && PreviewModeCombo != null)
            {
                var settings = _settingsService.CurrentSettings;
                settings.PreviewMode = PreviewModeCombo.SelectedIndex switch
                {
                    1 => "HTML",
                    2 => "LaTeX",
                    _ => "Markdown"
                };
                await _settingsService.SaveSettingsAsync(settings);
            }

            if (PreviewWebView != null && PreviewWebView.CoreWebView2 != null)
            {
                OnRefreshPreviewClick(this, new RoutedEventArgs());
            }
        }

        #endregion

        #region Markdown Toolbar

        private async Task ApplyMarkdownCommandToActiveEditorAsync(string command, string? color = null)
        {
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId &&
                _tabBridges.TryGetValue(tabId, out var bridgeGroup) &&
                bridgeGroup.Bridge != null)
            {
                await bridgeGroup.Bridge.ApplyMarkdownCommandAsync(command, color);
            }
        }

        private async void OnMarkdownBoldClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("bold");
        private async void OnMarkdownItalicClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("italic");
        private async void OnMarkdownUnderlineClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("underline");
        private async void OnMarkdownHighlightClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("highlight");
        private async void OnMarkdownInlineCodeClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("inlineCode");
        private async void OnMarkdownCodeBlockClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("codeBlock");
        private async void OnMarkdownHeadingClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("heading");
        private async void OnMarkdownLinkClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("link");
        private async void OnMarkdownQuoteClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("quote");
        private async void OnMarkdownUlClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("ul");
        private async void OnMarkdownOlClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("ol");
        private async void OnMarkdownTaskClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("task");
        private async void OnMarkdownTableClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("table");
        private async void OnMarkdownMathClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("math");
        private async void OnMarkdownArrowClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("arrow");
        private async void OnMarkdownFontIncreaseClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("fontIncrease");
        private async void OnMarkdownFontDecreaseClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("fontDecrease");
        private async void OnMarkdownTextColorClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("textColor", _lastTextColorHex);
        private async void OnMarkdownCutLineClick(object sender, RoutedEventArgs e) => await ApplyMarkdownCommandToActiveEditorAsync("cutLine");

        private async void OnMarkdownToolbarBackgroundClick(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.CurrentSettings;
            settings.MarkdownToolbarBackgroundColor = settings.MarkdownToolbarBackgroundColor switch
            {
                "#243B53" => "#ECECEE",
                "#ECECEE" => string.Empty,
                _ => "#243B53"
            };
            await _settingsService.SaveSettingsAsync(settings);
            ApplyUiPersonalization(settings);
        }

        private void OnToggleMarkdownToolbarClick(object sender, RoutedEventArgs e)
        {
            bool show = MarkdownToolbarToggle?.IsChecked == true;
            MarkdownToolbar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnTextColorButtonRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            ColorPickerFlyout.ShowAt(TextColorButton);
        }

        private async void OnApplyTextColorClick(object sender, RoutedEventArgs e)
        {
            ColorPickerFlyout.Hide();
            var color = TextColorPicker.Color;
            string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            _lastTextColorHex = hex;
            UpdateTextColorButtonVisual(color);
            await ApplyMarkdownCommandToActiveEditorAsync("textColor", hex);
        }

        private void UpdateTextColorButtonVisual(Windows.UI.Color color)
        {
            var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
            TextColorButton.Foreground = brush;
            TextColorButton.Resources["AppBarButtonForegroundPointerOver"] = brush;
            TextColorButton.Resources["AppBarButtonForegroundPressed"] = brush;
        }

        private async void OnAddFolderToFavoritesClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item &&
                (item.Tag as ExplorerItem ?? item.DataContext as ExplorerItem ?? FileListView.SelectedItem as ExplorerItem) is ExplorerItem explorerItem)
            {
                string folderPath = explorerItem.IsFolder
                    ? explorerItem.Path
                    : (Path.GetDirectoryName(explorerItem.Path) ?? string.Empty);

                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

                var settings = _settingsService.CurrentSettings;
                if (!settings.FavoritePaths.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
                {
                    settings.FavoritePaths.Add(folderPath);
                    await _settingsService.SaveSettingsAsync(settings);
                    RefreshFavoritesUI();
                }
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

        private static string NormalizeWebMessageJson(CoreWebView2WebMessageReceivedEventArgs args)
        {
            string json = args.WebMessageAsJson;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return doc.RootElement.GetString() ?? "{}";
                }
            }
            catch
            {
                string? asString = args.TryGetWebMessageAsString();
                if (!string.IsNullOrWhiteSpace(asString))
                {
                    return asString;
                }
            }

            return json;
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

        private OpenedTab? GetActiveTab()
        {
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId)
            {
                return _tabs.FirstOrDefault(t => t.Id == tabId);
            }

            return null;
        }

        private string GetActiveSelectionLanguage()
        {
            var activeTab = GetActiveTab();
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
                return GetMonacoLanguageName(activeTab.FilePath);
            }

            return "plaintext";
        }

        private async void OnLlmExplainClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText))
            {
                ShowErrorMessage("AI 오류", "선택된 텍스트가 없습니다. 에디터에서 분석할 범위를 드래그한 후 실행하십시오.");
                return;
            }

            string language = GetActiveSelectionLanguage();
            await PreflightCheckAndRunAsync("선택 영역 설명 (Explain)", _lastSelectionText,
                () => _llmService.ExplainCodeAsync(_lastSelectionText, language));
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

        private async void OnLlmTranslateClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText))
            {
                ShowErrorMessage("AI 오류", "선택된 텍스트가 없습니다. 번역할 범위를 드래그하십시오.");
                return;
            }

            await PreflightCheckAndRunAsync("선택 영역 번역 (Translate)", _lastSelectionText,
                () => _llmService.TranslateTextAsync(_lastSelectionText));
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

        private static string? FindGitRepositoryRoot(string? startPath)
        {
            if (string.IsNullOrEmpty(startPath))
            {
                return null;
            }

            var dir = new DirectoryInfo(startPath);
            while (dir != null)
            {
                string gitPath = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }

        #endregion

        #region Favorites Handlers

        private async void OnAddFileToFavoritesClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item &&
                (item.Tag as ExplorerItem ?? item.DataContext as ExplorerItem ?? FileListView.SelectedItem as ExplorerItem) is ExplorerItem explorerItem)
            {
                if (explorerItem.IsFolder) return; // This handler is for files only

                var settings = _settingsService.CurrentSettings;
                if (!settings.FavoritePaths.Contains(explorerItem.Path, StringComparer.OrdinalIgnoreCase))
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
                bool isFolder = Directory.Exists(path);
                bool isFile = !isFolder && File.Exists(path);
                if (isFolder || isFile)
                {
                    _favoritesList.Add(new FavoriteItem
                    {
                        Name = isFolder ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : Path.GetFileName(path),
                        Path = path,
                        IsFolder = isFolder
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
            var item = GetDataContextFromOriginalSource<FavoriteItem>(e.OriginalSource) ?? FavoritesListView.SelectedItem as FavoriteItem;
            if (item != null)
            {
                if (item.IsFolder)
                {
                    await NavigateExplorerToFolderAsync(item.Path);
                }
                else
                {
                    string? parentDir = Path.GetDirectoryName(item.Path);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        await NavigateExplorerToFolderAsync(parentDir);
                    }
                    await LoadFileIntoTabAsync(item.Path);
                }
            }
        }

        #endregion

        #region Recent Files Handlers

        private void LoadRecentFiles()
        {
            try
            {
                if (File.Exists(_recentFilesFilePath))
                {
                    string json = File.ReadAllText(_recentFilesFilePath);
                    var items = System.Text.Json.JsonSerializer.Deserialize<List<RecentFileItem>>(json);
                    if (items != null)
                    {
                        _recentFilesList.Clear();
                        foreach (var item in items)
                        {
                            _recentFilesList.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load recent files: {ex.Message}");
            }
        }

        private void SaveRecentFiles()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_recentFilesFilePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var list = _recentFilesList.ToList();
                string json = System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_recentFilesFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save recent files: {ex.Message}");
            }
        }

        private void AddRecentFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            this.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    string fullPath = Path.GetFullPath(filePath);
                    var existing = _recentFilesList.FirstOrDefault(f => f.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        _recentFilesList.Remove(existing);
                    }

                    var newItem = new RecentFileItem
                    {
                        Name = Path.GetFileName(fullPath),
                        Path = fullPath,
                        LastOpenedText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };

                    _recentFilesList.Insert(0, newItem);

                    while (_recentFilesList.Count > 30)
                    {
                        _recentFilesList.RemoveAt(_recentFilesList.Count - 1);
                    }

                    SaveRecentFiles();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to add recent file: {ex.Message}");
                }
            });
        }

        private void OnRemoveRecentFileClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                var existing = _recentFilesList.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    _recentFilesList.Remove(existing);
                    SaveRecentFiles();
                }
            }
        }

        private async void OnRecentFileItemDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            var item = GetDataContextFromOriginalSource<RecentFileItem>(e.OriginalSource) ?? RecentFilesListView.SelectedItem as RecentFileItem;
            if (item != null)
            {
                if (File.Exists(item.Path))
                {
                    await LoadFileIntoTabAsync(item.Path);
                }
                else
                {
                    ShowErrorMessage("파일 열기 실패", $"최근 파일이 존재하지 않습니다:\n{item.Path}");
                }
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

        #region UI Personalization Helper
        private void ApplyUiPersonalization(EditorSettings settings)
        {
            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = settings.Theme == "Light"
                    ? ElementTheme.Light
                    : ElementTheme.Dark;

                ApplyTitleBarTheme(settings);
                ApplyMarkdownToolbarTheme(settings);

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

        private void ApplyTitleBarTheme(EditorSettings settings)
        {
            try
            {
                var titleBar = AppWindow.TitleBar;
                bool light = settings.Theme == "Light";

                Windows.UI.Color background = TryParseHexColor(settings.CustomBackgroundColor, out var customBg)
                    ? customBg
                    : (light ? Windows.UI.Color.FromArgb(255, 243, 243, 243) : Windows.UI.Color.FromArgb(255, 32, 32, 32));
                Windows.UI.Color foreground = TryParseHexColor(settings.CustomForegroundColor, out var customFg)
                    ? customFg
                    : (light ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 242, 242, 242));
                Windows.UI.Color inactiveBackground = light
                    ? Windows.UI.Color.FromArgb(255, 232, 232, 232)
                    : Windows.UI.Color.FromArgb(255, 38, 38, 38);
                Windows.UI.Color hoverBackground = light
                    ? Windows.UI.Color.FromArgb(255, 224, 224, 224)
                    : Windows.UI.Color.FromArgb(255, 56, 56, 56);

                titleBar.BackgroundColor = background;
                titleBar.ForegroundColor = foreground;
                titleBar.InactiveBackgroundColor = inactiveBackground;
                titleBar.InactiveForegroundColor = foreground;
                titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonForegroundColor = foreground;
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonInactiveForegroundColor = foreground;
                titleBar.ButtonHoverBackgroundColor = hoverBackground;
                titleBar.ButtonHoverForegroundColor = foreground;
                titleBar.ButtonPressedBackgroundColor = hoverBackground;
                titleBar.ButtonPressedForegroundColor = foreground;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply titlebar theme: {ex.Message}");
            }
        }

        private void ApplyMarkdownToolbarTheme(EditorSettings settings)
        {
            try
            {
                Windows.UI.Color background = TryParseHexColor(settings.MarkdownToolbarBackgroundColor, out var customToolbarBg)
                    ? customToolbarBg
                    : (settings.Theme == "Light"
                        ? Windows.UI.Color.FromArgb(255, 236, 236, 238)
                        : Windows.UI.Color.FromArgb(255, 43, 47, 54));
                MarkdownToolbar.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(background);
                MarkdownToolbar.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(GetReadableForeground(background));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply markdown toolbar theme: {ex.Message}");
            }
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

        private static Windows.UI.Color GetReadableForeground(Windows.UI.Color background)
        {
            double luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255.0;
            return luminance < 0.48
                ? Windows.UI.Color.FromArgb(255, 245, 247, 250)
                : Windows.UI.Color.FromArgb(255, 24, 24, 27);
        }

        private void ApplyFontFamilyRecursively(DependencyObject parent, Microsoft.UI.Xaml.Media.FontFamily fontFamily)
        {
            if (parent == null) return;

            if (parent is IconElement)
            {
                return;
            }

            if (parent is Control ctrl)
            {
                if (ctrl.FontFamily.Source.Contains("Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (ctrl is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button &&
                    button.Content is string content &&
                    content.Any(ch => ch >= '\uE000' && ch <= '\uF8FF'))
                {
                    return;
                }

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
                StatusGitBranch.Text = "Git: 감지 안됨";
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

                bool isStaged = status.Length > 0 && status[0] != ' ' && status != "??";
                bool isUnstaged = status.Length > 1 && status[1] != ' ';
                string statusDesc = isStaged ? "Staged" : "Unstaged";
                if (status == "??") statusDesc = "Untracked";
                else if (isStaged && isUnstaged) statusDesc = "Staged + Unstaged";

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
            if (string.IsNullOrWhiteSpace(query)) return;

            if (string.IsNullOrEmpty(_currentFolderPath) && string.IsNullOrEmpty(_currentRepoPath))
            {
                ShowErrorMessage("검색 실패", "먼저 탐색기에서 작업할 폴더를 선택하십시오.");
                return;
            }

            _lastSearchQuery = query;
            _searchResultsList.Clear();
            string searchRoot = !string.IsNullOrEmpty(_currentFolderPath) ? _currentFolderPath : _currentRepoPath;
            bool isRegex = SearchRegexToggle.IsChecked == true;
            bool isMatchCase = SearchMatchCaseToggle.IsChecked == true;
            bool isWholeWord = SearchWholeWordToggle.IsChecked == true;
            Regex? searchRegex;

            try
            {
                searchRegex = BuildSearchRegex(query, isRegex, isMatchCase, isWholeWord);
            }
            catch (ArgumentException ex)
            {
                ShowErrorMessage("검색 실패", $"정규식이 올바르지 않습니다.\n{ex.Message}");
                return;
            }

            int foundCount = 0;
            int skippedFiles = 0;
            await Task.Run(() =>
            {
                var tempResults = new List<SearchResultItem>();
                var settings = _settingsService.CurrentSettings;
                long thresholdBytes = settings.LargeFileThresholdMB * 1024 * 1024;

                foreach (var file in EnumerateSearchFiles(searchRoot))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length > thresholdBytes)
                        {
                            var largeResults = _fileService.SearchLargeFileAsync(file, query, isRegex, isMatchCase, isWholeWord).GetAwaiter().GetResult();
                            foreach (var lr in largeResults)
                            {
                                tempResults.Add(new SearchResultItem
                                {
                                    Path = file,
                                    LineNumber = lr.LineNumber,
                                    LineContent = lr.LineContent,
                                    IndexOfMatch = lr.IndexOfMatch,
                                    MatchLength = lr.MatchLength
                                });
                                foundCount++;

                                FlushSearchResultsIfNeeded(tempResults);
                            }

                            continue;
                        }

                        int lineNum = 1;
                        foreach (var line in File.ReadLines(file))
                        {
                            var match = searchRegex.Match(line);
                            if (match.Success)
                            {
                                tempResults.Add(new SearchResultItem
                                {
                                    Path = file,
                                    LineNumber = lineNum,
                                    LineContent = line,
                                    IndexOfMatch = match.Index,
                                    MatchLength = match.Length
                                });
                                foundCount++;

                                FlushSearchResultsIfNeeded(tempResults);
                            }

                            lineNum++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is NotSupportedException)
                    {
                        skippedFiles++;
                        System.Diagnostics.Debug.WriteLine($"Skipped search file {file}: {ex.Message}");
                    }
                }

                FlushSearchResults(tempResults);
            });

            if (foundCount == 0 && skippedFiles > 0)
            {
                ShowErrorMessage("검색 완료", $"검색 결과가 없습니다.\n읽을 수 없어 건너뛴 파일: {skippedFiles:N0}개");
            }
            else if (foundCount > 0)
            {
                this.DispatcherQueue.TryEnqueue(async () =>
                {
                    SearchResultsList.SelectedIndex = 0;
                    SearchResultsList.ScrollIntoView(SearchResultsList.SelectedItem);
                    if (SearchResultsList.SelectedItem is SearchResultItem selectedItem)
                    {
                        await LoadFileIntoTabAndHighlightAsync(selectedItem);
                    }
                });
            }
        }

        private static Regex BuildSearchRegex(string query, bool isRegex, bool isMatchCase, bool isWholeWord)
        {
            string pattern = isRegex ? query : Regex.Escape(query);
            if (isWholeWord)
            {
                pattern = $"\\b{pattern}\\b";
            }

            var options = isMatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            return new Regex(pattern, options);
        }

        private IEnumerable<string> EnumerateSearchFiles(string searchRoot)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            };

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(searchRoot, "*", options);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is DirectoryNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine($"Search root unavailable: {ex.Message}");
                yield break;
            }

            foreach (var file in files)
            {
                if (ShouldSkipSearchPath(file))
                {
                    continue;
                }

                yield return file;
            }
        }

        private static bool ShouldSkipSearchPath(string filePath)
        {
            string normalized = filePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string[] skippedSegments =
            {
                $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}packages{Path.DirectorySeparatorChar}"
            };

            return skippedSegments.Any(segment => normalized.Contains(segment, StringComparison.OrdinalIgnoreCase));
        }

        private void FlushSearchResultsIfNeeded(List<SearchResultItem> results)
        {
            if (results.Count >= 30)
            {
                FlushSearchResults(results);
            }
        }

        private void FlushSearchResults(List<SearchResultItem> results)
        {
            if (results.Count == 0)
            {
                return;
            }

            var batch = results.ToList();
            results.Clear();
            this.DispatcherQueue.TryEnqueue(() =>
            {
                foreach (var item in batch)
                {
                    _searchResultsList.Add(item);
                }
            });
        }

        private static T? GetDataContextFromOriginalSource<T>(object originalSource) where T : class
        {
            if (originalSource is not DependencyObject current)
            {
                return null;
            }

            while (current != null)
            {
                if (current is FrameworkElement { DataContext: T item })
                {
                    return item;
                }

                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private string ReplaceSearchMatches(string original, string query, string replace)
        {
            bool isRegex = SearchRegexToggle.IsChecked == true;
            bool isMatchCase = SearchMatchCaseToggle.IsChecked == true;
            bool isWholeWord = SearchWholeWordToggle.IsChecked == true;

            if (isRegex || isWholeWord)
            {
                var regex = BuildSearchRegex(query, isRegex, isMatchCase, isWholeWord);
                return regex.Replace(original, replace);
            }

            var comparison = isMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return original.Replace(query, replace, comparison);
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
                                    lines[res.LineNumber - 1] = ReplaceSearchMatches(lines[res.LineNumber - 1], query, replace);
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
            var item = GetDataContextFromOriginalSource<SearchResultItem>(e.OriginalSource) ?? SearchResultsList.SelectedItem as SearchResultItem;
            if (item != null)
            {
                // Open file
                await LoadFileIntoTabAsync(item.Path);

                // Tiny delay to allow WebView2 rendering and virtual host loading to settle
                await Task.Delay(250);

                if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                    activeTabItem.Tag is string tabId &&
                    _tabBridges.TryGetValue(tabId, out var bridgeGroup))
                {
                    if (bridgeGroup.Bridge != null)
                    {
                        await bridgeGroup.Bridge.RevealLineAsync(item.LineNumber, item.IndexOfMatch, item.MatchLength, _lastSearchQuery);
                    }
                    else if (bridgeGroup.WebView != null && bridgeGroup.WebView.CoreWebView2 != null)
                    {
                        var revealMsg = new { action = "revealLine", lineNumber = item.LineNumber, indexOfMatch = item.IndexOfMatch, matchLength = item.MatchLength, query = _lastSearchQuery };
                        string revealJson = System.Text.Json.JsonSerializer.Serialize(revealMsg);
                        bridgeGroup.WebView.CoreWebView2.PostWebMessageAsJson(revealJson);
                    }
                }
            }
        }

        
        // ----------------------------------------------------
        // Premium Helpers added for Ueditor Enhancements
        // ----------------------------------------------------

        private async Task<bool> SaveTabAsync(OpenedTab tab)
        {
            var tabItem = EditorTabView.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id);
            if (tabItem == null) return false;

            if (tab.IsLargeFileMode)
            {
                if (string.IsNullOrEmpty(tab.FilePath)) return false;

                try
                {
                    await _fileService.SaveLargeFileWithPatchesAsync(tab.FilePath, tab.LargeFilePatches);
                    tab.LargeFilePatches.Clear();
                    tab.IsDirty = false;
                    tabItem.Header = tab.DisplayTitle;

                    WebView2? wv = null;
                    if (tabItem.Content is Grid grid)
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
                            fontSize = _settingsService.CurrentSettings.FontSize,
                            fontFamily = _settingsService.CurrentSettings.FontFamily,
                            customBackgroundColor = _settingsService.CurrentSettings.CustomBackgroundColor,
                            customForegroundColor = _settingsService.CurrentSettings.CustomForegroundColor,
                            readOnly = true
                        };
                        string initJson = System.Text.Json.JsonSerializer.Serialize(initMsg);
                        wv.CoreWebView2.PostWebMessageAsJson(initJson);
                    }

                    UpdateStatusFileStats(tab);
                    await RefreshGitStatusUIAsync();
                    return true;
                }
                catch (Exception ex)
                {
                    ShowErrorMessage("대용량 파일 저장 실패", ex.Message);
                    return false;
                }
            }

            if (string.IsNullOrEmpty(tab.FilePath))
            {
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
                    tab.Language = GetMonacoLanguageName(file.Path);
                }
                else
                {
                    return false; // Canceled
                }
            }

            try
            {
                await _fileService.SaveTextFileAsync(tab.FilePath, tab.Content);
                tab.IsDirty = false;
                tabItem.Header = tab.DisplayTitle;
                UpdateStatusFileStats(tab);
                UpdateLanguageUI(tab);
                return true;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("저장 실패", ex.Message);
                return false;
            }
        }

        private void OnCloseActiveTabShortcutInvoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            if (args != null) args.Handled = true;
            if (EditorTabView.SelectedItem is TabViewItem tabItem && tabItem.Tag is string tabId)
            {
                var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null)
                {
                    if (tab.IsDirty)
                    {
                        WarnUnsavedAndClose(tab, tabItem);
                    }
                    else
                    {
                        CloseTabAndCleanup(tab, tabItem);
                    }
                }
            }
        }

        private bool _isClosingConfirmed = false;
        private async void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (_isClosingConfirmed) return;

            var dirtyTabs = _tabs.Where(t => t.IsDirty).ToList();
            if (dirtyTabs.Count == 0) return;

            args.Cancel = true; // Prevent immediate close

            var dialog = new ContentDialog
            {
                Title = "저장되지 않은 변경 사항",
                Content = $"저장되지 않은 탭이 {dirtyTabs.Count}개 있습니다. 종료하기 전에 저장하시겠습니까?",
                PrimaryButtonText = "저장하고 종료",
                SecondaryButtonText = "저장하지 않고 종료",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                foreach (var tab in dirtyTabs)
                {
                    bool saved = await SaveTabAsync(tab);
                    if (!saved) return; // Abort exit if save fails or cancels
                }
                _isClosingConfirmed = true;
                this.Close();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                _isClosingConfirmed = true;
                this.Close();
            }
        }

        private string DetectLanguageFromContent(string text, string defaultLanguage = "plaintext")
        {
            if (string.IsNullOrWhiteSpace(text)) return defaultLanguage;

            string sample = text.Trim();
            if (sample.Length > 2000) sample = sample.Substring(0, 2000);

            if (sample.StartsWith("{") && sample.EndsWith("}") && sample.Contains("\"")) return "json";
            if (sample.StartsWith("[") && sample.EndsWith("]") && sample.Contains("{\"")) return "json";

            if (sample.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
                sample.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                sample.Contains("<head", StringComparison.OrdinalIgnoreCase) ||
                sample.Contains("<body", StringComparison.OrdinalIgnoreCase)) return "html";

            if (sample.Contains("\\documentclass") ||
                sample.Contains("\\begin{document}") ||
                sample.Contains("\\begin{align}") ||
                sample.Contains("$$\n") ||
                sample.Contains("\\frac{")) return "latex";

            if (sample.Contains("\n# ") || sample.StartsWith("# ") ||
                sample.Contains("## ") ||
                sample.Contains("```") ||
                sample.Contains("- [ ] ") ||
                sample.Contains("**")) return "markdown";

            if (sample.Contains("using System;") ||
                sample.Contains("namespace ") ||
                (sample.Contains("public class ") && sample.Contains("void Main")) ||
                sample.Contains("Console.WriteLine(")) return "csharp";

            if (sample.Contains("#include <iostream>") ||
                sample.Contains("std::cout") ||
                sample.Contains("int main()")) return "cpp";

            if (sample.Contains("public class ") && sample.Contains("public static void main") ||
                sample.Contains("System.out.println(")) return "java";

            if (sample.Contains("import os") ||
                (sample.Contains("def ") && sample.Contains(":")) ||
                (sample.Contains("print(") && sample.Contains("if __name__ == ")) ||
                sample.Contains("elif ")) return "python";

            if ((sample.Contains("const ") && sample.Contains(" = require(")) ||
                (sample.Contains("import ") && sample.Contains(" from ")) ||
                sample.Contains("console.log(") ||
                sample.Contains("document.getElementById(")) return "javascript";

            if (sample.Contains("fn main()") ||
                sample.Contains("let mut ") ||
                sample.Contains("pub struct ") ||
                sample.Contains("impl ") ||
                sample.Contains("use std::")) return "rust";

            if (sample.Contains("package main") ||
                sample.Contains("import (") ||
                sample.Contains("func main()")) return "go";

            if (sample.Contains("SELECT ", StringComparison.OrdinalIgnoreCase) &&
                sample.Contains("FROM ", StringComparison.OrdinalIgnoreCase)) return "sql";

            if (sample.Contains("body {") ||
                sample.Contains(".class {") ||
                sample.Contains("#id {") ||
                sample.Contains("margin:") ||
                sample.Contains("padding:")) return "css";

            if (sample.Contains("---") &&
                (sample.Contains("version:") || sample.Contains("name:") || sample.Contains("author:"))) return "yaml";

            if (sample.StartsWith("#!/bin/bash") ||
                sample.StartsWith("#!/bin/sh") ||
                sample.Contains("echo ") ||
                sample.Contains("export ")) return "shell";

            return defaultLanguage;
        }

        private void UpdateLanguageUI(OpenedTab tab)
        {
            if (tab == null) return;
            string detected = tab.Language;
            if (detected == "plaintext" || string.IsNullOrEmpty(detected))
            {
                detected = DetectLanguageFromContent(tab.Content, "plaintext");
            }

            if (StatusLanguage != null)
            {
                StatusLanguage.Text = detected.ToUpper();
            }

            if (tab.Language != detected)
            {
                tab.Language = detected;
                if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                {
                    _ = bridgeGroup.Bridge.SetLanguageAsync(detected);
                }
            }
        }

        private async Task LoadFileIntoTabAndHighlightAsync(SearchResultItem item)
        {
            await LoadFileIntoTabAsync(item.Path);
            await Task.Delay(250);
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId &&
                _tabBridges.TryGetValue(tabId, out var bridgeGroup))
            {
                if (bridgeGroup.Bridge != null)
                {
                    await bridgeGroup.Bridge.RevealLineAsync(item.LineNumber, item.IndexOfMatch, item.MatchLength, _lastSearchQuery);
                }
                else if (bridgeGroup.WebView?.CoreWebView2 != null)
                {
                    var revealMsg = new { action = "revealLine", lineNumber = item.LineNumber, indexOfMatch = item.IndexOfMatch, matchLength = item.MatchLength, query = _lastSearchQuery };
                    bridgeGroup.WebView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(revealMsg));
                }
            }
        }

        private async void OnSearchQueryInputKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                string query = SearchQueryInput.Text;
                if (string.IsNullOrWhiteSpace(query)) return;

                if (_searchResultsList.Count == 0 || query != _lastSearchQuery)
                {
                    OnSearchAllFilesClick(this, new RoutedEventArgs());
                }
                else
                {
                    int nextIndex = 0;
                    if (SearchResultsList.SelectedIndex >= 0)
                    {
                        nextIndex = (SearchResultsList.SelectedIndex + 1) % _searchResultsList.Count;
                    }
                    SearchResultsList.SelectedIndex = nextIndex;
                    SearchResultsList.ScrollIntoView(SearchResultsList.SelectedItem);
                    
                    if (SearchResultsList.SelectedItem is SearchResultItem selectedItem)
                    {
                        await LoadFileIntoTabAndHighlightAsync(selectedItem);
                    }
                }
            }
        }

        private void ApplyPreviewVisibility(bool show)
        {
            RightPanelToggle.IsChecked = show;
            if (!show)
            {
                double currentWidth = PreviewGrid.ActualWidth > 0 ? PreviewGrid.ActualWidth : PreviewColumn.Width.Value;
                if (currentWidth > 0)
                {
                    _lastPreviewWidth = currentWidth;
                }
                PreviewColumn.MinWidth = 0;
                PreviewColumn.Width = new GridLength(0);
                RightSplitter.Visibility = Visibility.Collapsed;
                PreviewGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                PreviewColumn.MinWidth = PreviewPanelMinWidth;
                PreviewColumn.Width = new GridLength(Math.Max(_lastPreviewWidth, PreviewColumn.MinWidth));
                RightSplitter.Visibility = Visibility.Visible;
                PreviewGrid.Visibility = Visibility.Visible;
                if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                    activeTabItem.Tag is string tabId)
                {
                    var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
                    if (tab != null) UpdateLivePreview(tab);
                }
            }
        }

        private async void OnCompareFilesClick(object sender, RoutedEventArgs e)
        {
            var panel = new StackPanel { Spacing = 12, Width = 400 };
            
            // Build tab list for ComboBoxes
            var tabChoices = new List<string> { "직접 파일 선택..." };
            foreach (var t in _tabs)
            {
                tabChoices.Add($"[탭] {t.Title}");
            }

            var originalLabel = new TextBlock { Text = "원본 파일 (Original File)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var originalCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var choice in tabChoices) originalCombo.Items.Add(choice);
            originalCombo.SelectedIndex = 0;

            var originalPathBox = new TextBox { PlaceholderText = "원본 파일 경로...", IsReadOnly = true };
            var originalBrowseBtn = new Button { Content = "찾아보기..." };
            var originalRow = new Grid();
            originalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            originalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(originalPathBox, 0);
            Grid.SetColumn(originalBrowseBtn, 1);
            originalBrowseBtn.Margin = new Thickness(8, 0, 0, 0);
            originalRow.Children.Add(originalPathBox);
            originalRow.Children.Add(originalBrowseBtn);

            var modifiedLabel = new TextBlock { Text = "비교 대상 파일 (Modified File)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var modifiedCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var choice in tabChoices) modifiedCombo.Items.Add(choice);
            modifiedCombo.SelectedIndex = 0;

            var modifiedPathBox = new TextBox { PlaceholderText = "비교 대상 파일 경로...", IsReadOnly = true };
            var modifiedBrowseBtn = new Button { Content = "찾아보기..." };
            var modifiedRow = new Grid();
            modifiedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modifiedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(modifiedPathBox, 0);
            Grid.SetColumn(modifiedBrowseBtn, 1);
            modifiedBrowseBtn.Margin = new Thickness(8, 0, 0, 0);
            modifiedRow.Children.Add(modifiedPathBox);
            modifiedRow.Children.Add(modifiedBrowseBtn);

            panel.Children.Add(originalLabel);
            panel.Children.Add(originalCombo);
            panel.Children.Add(originalRow);
            panel.Children.Add(new MenuFlyoutSeparator());
            panel.Children.Add(modifiedLabel);
            panel.Children.Add(modifiedCombo);
            panel.Children.Add(modifiedRow);

            originalCombo.SelectionChanged += (_, __) =>
            {
                bool isBrowse = originalCombo.SelectedIndex == 0;
                originalBrowseBtn.IsEnabled = isBrowse;
                if (!isBrowse)
                {
                    originalPathBox.Text = originalCombo.SelectedItem.ToString();
                }
                else
                {
                    originalPathBox.Text = string.Empty;
                }
            };

            modifiedCombo.SelectionChanged += (_, __) =>
            {
                bool isBrowse = modifiedCombo.SelectedIndex == 0;
                modifiedBrowseBtn.IsEnabled = isBrowse;
                if (!isBrowse)
                {
                    modifiedPathBox.Text = modifiedCombo.SelectedItem.ToString();
                }
                else
                {
                    modifiedPathBox.Text = string.Empty;
                }
            };

            originalBrowseBtn.Click += async (_, __) =>
            {
                var picker = new FileOpenPicker();
                InitializePickerWindow(picker);
                picker.FileTypeFilter.Add("*");
                var file = await picker.PickSingleFileAsync();
                if (file != null) originalPathBox.Text = file.Path;
            };

            modifiedBrowseBtn.Click += async (_, __) =>
            {
                var picker = new FileOpenPicker();
                InitializePickerWindow(picker);
                picker.FileTypeFilter.Add("*");
                var file = await picker.PickSingleFileAsync();
                if (file != null) modifiedPathBox.Text = file.Path;
            };

            var dialog = new ContentDialog
            {
                Title = "파일 비교 (File Compare)",
                Content = panel,
                PrimaryButtonText = "비교하기",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                OpenedTab? tabA = null;
                OpenedTab? tabB = null;

                if (originalCombo.SelectedIndex > 0)
                {
                    tabA = _tabs[originalCombo.SelectedIndex - 1];
                }
                if (modifiedCombo.SelectedIndex > 0)
                {
                    tabB = _tabs[modifiedCombo.SelectedIndex - 1];
                }

                string pathA = tabA == null ? originalPathBox.Text.Trim() : (string.IsNullOrEmpty(tabA.FilePath) ? tabA.Title : tabA.FilePath);
                string pathB = tabB == null ? modifiedPathBox.Text.Trim() : (string.IsNullOrEmpty(tabB.FilePath) ? tabB.Title : tabB.FilePath);

                bool validA = tabA != null || (!string.IsNullOrEmpty(pathA) && File.Exists(pathA));
                bool validB = tabB != null || (!string.IsNullOrEmpty(pathB) && File.Exists(pathB));

                if (validA && validB)
                {
                    await OpenCompareTabAsync(pathA, pathB, tabA?.Content, tabB?.Content);
                }
                else
                {
                    ShowErrorMessage("비교 오류", "올바른 두 파일 혹은 탭을 선택해 주세요.");
                }
            }
        }

        private async Task OpenCompareTabAsync(string pathA, string pathB, string? contentA = null, string? contentB = null)
        {
            if (contentA == null) contentA = await _fileService.ReadTextFileAsync(pathA);
            if (contentB == null) contentB = await _fileService.ReadTextFileAsync(pathB);

            string title = $"비교: {Path.GetFileName(pathA)} ↔ {Path.GetFileName(pathB)}";

            var tab = new OpenedTab
            {
                Title = title,
                FilePath = "",
                Content = ""
            };

            _tabs.Add(tab);

            var grid = new Grid();
            var diffWebView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            grid.Children.Add(diffWebView);

            var tabItem = new TabViewItem
            {
                Header = tab.Title,
                Content = grid,
                Tag = tab.Id
            };

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cacheFolder = Path.Combine(localAppData, "Ueditor", "WebView2Cache");
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, cacheFolder, null);
            await diffWebView.EnsureCoreWebView2Async(env);

            string webResourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebResources");
            diffWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "ueditor.local",
                webResourcesPath,
                CoreWebView2HostResourceAccessKind.Allow
            );

            diffWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            diffWebView.CoreWebView2.Settings.IsScriptEnabled = true;
            diffWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            diffWebView.Source = new Uri("http://ueditor.local/diff.html");

            diffWebView.NavigationCompleted += (s, e) =>
            {
                var msg = new
                {
                    action = "compare",
                    titleA = Path.GetFileName(pathA),
                    titleB = Path.GetFileName(pathB),
                    textA = contentA,
                    textB = contentB,
                    theme = _settingsService.CurrentSettings.Theme,
                    uiFontFamily = _settingsService.CurrentSettings.UiFontFamily
                };
                string json = System.Text.Json.JsonSerializer.Serialize(msg);
                diffWebView.CoreWebView2.PostWebMessageAsJson(json);
            };

            _tabBridges[tab.Id] = (diffWebView, null!);

            EditorTabView.TabItems.Add(tabItem);
            EditorTabView.SelectedItem = tabItem;
        }

        private void OnRootKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            if (ctrl)
            {
                if (shift && e.Key == Windows.System.VirtualKey.F)
                {
                    e.Handled = true;
                    EnsureLeftPanelVisible();
                    ShowLeftSidebarPage(4);
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        SearchQueryInput.Focus(FocusState.Programmatic);
                        SearchQueryInput.Focus(FocusState.Keyboard);
                    });
                }
                else if (e.Key == Windows.System.VirtualKey.W)
                {
                    e.Handled = true;
                    OnCloseActiveTabShortcutInvoked(null!, null!);
                }
                else if (e.Key == Windows.System.VirtualKey.S)
                {
                    e.Handled = true;
                    OnSaveFileClick(this, new RoutedEventArgs());
                }
                else if (e.Key == Windows.System.VirtualKey.O)
                {
                    e.Handled = true;
                    OnOpenFileClick(this, new RoutedEventArgs());
                }
                else if (e.Key == Windows.System.VirtualKey.T)
                {
                    e.Handled = true;
                    OnOpenTerminalClick(this, new RoutedEventArgs());
                }
                else if (e.Key == Windows.System.VirtualKey.F)
                {
                    e.Handled = true;
                    OnFindClick(this, new RoutedEventArgs());
                }
            }
        }

        #endregion
    }

    public class RecentFileItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string LastOpenedText { get; set; } = string.Empty;
        public string IconGlyph => "\uE7C3"; // Document glyph
    }

    public class FavoriteItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsFolder { get; set; } = false;
        /// <summary>Returns the appropriate glyph for display in the favorites list.</summary>
        public string IconGlyph => IsFolder ? "\uE8B7" : "\uE734"; // Folder or Star glyph
        public Windows.UI.Color IconColor => IsFolder
            ? Windows.UI.Color.FromArgb(255, 255, 195, 0)   // Amber for folders
            : Windows.UI.Color.FromArgb(255, 255, 215, 0);  // Gold for files
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
