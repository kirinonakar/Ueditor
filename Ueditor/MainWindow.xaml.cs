using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Ueditor.Core.Interfaces;
using Ueditor.Core.Services;
using Ueditor.Core.Models;
using Ueditor.Controls;
using Ueditor.Editor;
using Ueditor.ViewModels;


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
        private readonly ILanguageDetectionService _languageDetectionService;
        private readonly IRecentFilesService _recentFilesService;
        private readonly IFileSearchService _fileSearchService;
        private readonly IStickyNoteService _stickyNoteService;
        private readonly ISettingsDialogService _settingsDialogService;
        private readonly IUiPersonalizationService _uiPersonalizationService;
        private readonly ILocalizationService _localizationService;
        private readonly IFileSaveDialogService _fileSaveDialogService;
        private readonly ShellPanelLayoutService _shellPanelLayoutService;
        private readonly TerminalShortcutService _terminalShortcutService;
        private readonly ExplorerDirectoryService _explorerDirectoryService;
        private readonly CompareSelectionDialogService _compareSelectionDialogService;
        private readonly SearchReplaceController _searchReplaceController;
        private readonly GitPanelController _gitPanelController;
        private readonly FavoritesRecentController _favoritesRecentController;
        private readonly SnippetsController _snippetsController;
        private readonly LlmAssistantController _llmAssistantController;
        private readonly MainWindowViewModel _viewModel = new MainWindowViewModel();
        private string _currentFolderPath = string.Empty;
        private string _currentRepoPath = string.Empty;
        private bool _isSyncingEncodingCombo = false;
        
        // Dynamic tabs collection
        private readonly Dictionary<string, (WebView2 WebView, MonacoBridge Bridge)> _tabBridges = 
            new Dictionary<string, (WebView2 WebView, MonacoBridge Bridge)>();
        private readonly Dictionary<string, EditorDocumentSession> _editorSessions =
            new Dictionary<string, EditorDocumentSession>();

        // Timer for debouncing live preview renders
        private readonly DispatcherTimer _previewDebounceTimer;
        private OpenedTab? _activeTabForPreview = null;

        // Autosave timer
        private readonly DispatcherTimer _autoSaveTimer;
        private bool _autoSaveEnabled = false;

        private readonly DispatcherTimer _gitAutoRefreshTimer;

        private ToggleButton LeftPanelToggle => StatusBarPane.LeftPanelToggleButton;
        private ToggleButton RightPanelToggle => StatusBarPane.RightPanelToggleButton;
        private TextBlock StatusLine => StatusBarPane.LineText;
        private TextBlock StatusCol => StatusBarPane.ColumnText;
        private TextBlock StatusFileStats => StatusBarPane.FileStatsText;
        private TextBlock StatusGitBranch => StatusBarPane.GitBranchText;
        private TextBlock StatusLanguage => StatusBarPane.LanguageText;
        private ComboBox StatusEncodingCombo => StatusBarPane.EncodingCombo;
        private TextBlock ExplorerStatusText => LeftSidebarTabView.ExplorerStatus;
        private ListView FileListView => LeftSidebarTabView.FileList;
        private ListView SearchResultsList => LeftSidebarTabView.SearchResults;
        private TextBox SearchQueryInput => LeftSidebarTabView.SearchQuery;
        private TextBox ReplaceQueryInput => LeftSidebarTabView.ReplaceQuery;
        private ToggleButton SearchMatchCaseToggle => LeftSidebarTabView.SearchMatchCase;
        private ToggleButton SearchWholeWordToggle => LeftSidebarTabView.SearchWholeWord;
        private ToggleButton SearchRegexToggle => LeftSidebarTabView.SearchRegex;
        private ComboBox PreviewModeCombo => PreviewGrid.PreviewMode;
        private WebView2 PreviewWebView => PreviewGrid.PreviewWebViewControl;
        private TextBlock SelectionStatsText => PreviewGrid.SelectionStats;
        private TabView EditorTabView => EditorWorkspace.EditorTabViewControl;
        private TabView EditorTabView2 => EditorWorkspace.EditorTabView2Control;
        private TerminalPane TerminalPane => EditorWorkspace.TerminalPaneControl;

        public MainWindow()
        {
            this.InitializeComponent();
            WindowPlacementService.SetWindowIcon(AppWindow);

            _fileService = new FileService();
            _settingsService = new SettingsService();
            _credentialService = new CredentialService();
            _llmService = new LLMService(_settingsService, _credentialService);
            _gitService = new GitService();
            _snippetService = new SnippetService();
            _languageDetectionService = new LanguageDetectionService();
            _recentFilesService = new RecentFilesService();
            _fileSearchService = new FileSearchService(_fileService);
            _stickyNoteService = new StickyNoteService();
            _settingsDialogService = new SettingsDialogService(_llmService);
            _uiPersonalizationService = new UiPersonalizationService();
            _localizationService = new ResourceLocalizationService(_settingsService);
            _fileSaveDialogService = new FileSaveDialogService();
            _explorerDirectoryService = new ExplorerDirectoryService();
            _compareSelectionDialogService = new CompareSelectionDialogService();
            _shellPanelLayoutService = new ShellPanelLayoutService(
                MainWorkGrid,
                ExplorerColumn,
                PreviewColumn,
                LeftSplitter,
                RightSplitter,
                LeftSidebarTabView,
                PreviewGrid);
            _terminalShortcutService = new TerminalShortcutService(WindowNative.GetWindowHandle(this));
            _terminalShortcutService.ToggleRequested += (_, _) => ToggleTerminal();
            _gitAutoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _gitAutoRefreshTimer.Tick += OnGitAutoRefreshTimerTick;
            _searchReplaceController = new SearchReplaceController(
                _fileSearchService,
                _viewModel,
                SearchQueryInput,
                ReplaceQueryInput,
                SearchMatchCaseToggle,
                SearchWholeWordToggle,
                SearchRegexToggle,
                SearchResultsList,
                GetSearchRoot,
                GetLargeFileThresholdBytes,
                () => this.Content.XamlRoot,
                ShowErrorMessage,
                LoadFileIntoTabAndHighlightAsync,
                RefreshGitStatusUIAsync);
            _gitPanelController = new GitPanelController(
                _gitService,
                _fileService,
                _viewModel,
                LeftSidebarTabView,
                StatusGitBranch,
                () => _currentRepoPath,
                () => this.Content.XamlRoot,
                GetLocalizedString,
                IsGitNotDetectedText,
                ShowErrorMessage,
                () => _gitAutoRefreshTimer.Start(),
                OpenCompareTabAsync);
            _favoritesRecentController = new FavoritesRecentController(
                _settingsService,
                _recentFilesService,
                _viewModel,
                LeftSidebarTabView,
                callback => DispatcherQueue.TryEnqueue(() => callback()),
                NavigateExplorerToFolderAsync,
                LoadFileIntoTabAsync,
                ShowErrorMessage);
            _snippetsController = new SnippetsController(
                _snippetService,
                _viewModel,
                LeftSidebarTabView,
                () => this.Content.XamlRoot,
                InsertTextIntoActiveEditorAsync,
                ShowErrorMessage);
            _llmAssistantController = new LlmAssistantController(
                _llmService,
                _settingsService,
                _languageDetectionService,
                PreviewGrid,
                () => this.Content.XamlRoot,
                GetActiveTab,
                GetTabTextForLlmContext,
                InsertTextIntoActiveEditorAsync,
                ShowErrorMessage);

            if (Content is FrameworkElement rootElement)
            {
                rootElement.DataContext = _viewModel;
            }

            // Bind Left Sidebar Tab items
            FileListView.ItemsSource = _viewModel.ExplorerItems;
            SearchResultsList.ItemsSource = _viewModel.SearchResults;
            foreach (string encodingName in TextEncodingService.SupportedEncodingNames)
            {
                StatusEncodingCombo.Items.Add(encodingName);
            }
            StatusEncodingCombo.SelectedItem = "UTF-8";
            StatusBarPane.LeftPanelToggleClick += OnToggleLeftPanelClick;
            StatusBarPane.RightPanelToggleClick += OnTogglePreviewClick;
            StatusBarPane.EncodingSelectionChanged += OnStatusEncodingSelectionChanged;
            StatusBarPane.LineNumberClick += OnStatusLineNumberClick;
            StatusBarPane.LineEndingClick += OnStatusLineEndingClick;
            MarkdownToolbar.CommandRequested += OnMarkdownToolbarCommandRequested;
            WireTopToolbarEvents();
            WireLeftSidebarEvents();
            WireRightSidebarEvents();
            WireEditorWorkspaceEvents();
            TerminalPane.AttachOwner(this);
            TerminalPane.WorkingDirectoryProvider = GetTerminalWorkingDirectory;
            TerminalPane.SessionsEmptied += OnTerminalPaneSessionsEmptied;
            TerminalPane.CloseRequested += OnTerminalPaneCloseRequested;

            // Initialize Preview Debounce Timer (50ms for near-real-time preview)
            _previewDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _previewDebounceTimer.Tick += OnPreviewDebounceTimerTick;

            // Initialize Autosave Timer (5 second interval)
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _autoSaveTimer.Tick += OnAutoSaveTimerTick;

            // Load local configurations and boot initial states
            // Setup custom title bar
            SetupCustomTitleBar();

            this.Activated += OnWindowActivated;
            this.Activated += OnWindowActivationChanged;
            this.Closed += OnWindowClosed;
            this.AppWindow.Closing += OnAppWindowClosing;
        }

        private void WireLeftSidebarEvents()
        {
            LeftSidebarTabView.LeftActivityClick += OnLeftActivityClick;
            LeftSidebarTabView.ExplorerUpClick += OnExplorerUpClick;
            LeftSidebarTabView.SelectFolderClick += OnSelectFolderClick;
            LeftSidebarTabView.OpenTerminalClick += OnOpenTerminalClick;
            LeftSidebarTabView.FileListViewDoubleTapped += OnFileListViewDoubleTapped;
            LeftSidebarTabView.FileListViewItemRightTapped += OnFileListViewItemRightTapped;
            LeftSidebarTabView.SearchQueryInputKeyDown += OnSearchQueryInputKeyDown;
            LeftSidebarTabView.SearchAllFilesClick += OnSearchAllFilesClick;
            LeftSidebarTabView.ReplaceAllClick += OnReplaceAllClick;
            LeftSidebarTabView.SearchResultDoubleTapped += OnSearchResultDoubleTapped;
        }

        private void WireRightSidebarEvents()
        {
            PreviewGrid.PreviewModeSelectionChanged += OnPreviewModeComboSelectionChanged;
            PreviewGrid.OpenPreviewInBrowserClick += OnOpenPreviewInBrowserClick;
        }

        private void WireTopToolbarEvents()
        {
            TopToolbar.OpenFileClick += OnOpenFileClick;
            TopToolbar.SaveFileClick += OnSaveFileClick;
            TopToolbar.SaveAsFileClick += OnSaveAsFileClick;
            TopToolbar.CompareFilesClick += OnCompareFilesClick;
            TopToolbar.OpenTerminalClick += OnOpenTerminalClick;
            TopToolbar.PrintClick += OnPrintClick;
            TopToolbar.TopMostToggleClick += OnTopMostToggleClick;
            TopToolbar.StickyNoteClick += OnStickyNoteClick;
            TopToolbar.WordWrapToggleClick += OnWordWrapToggleClick;
            TopToolbar.FindClick += OnFindClick;
            TopToolbar.ToggleMarkdownToolbarClick += OnToggleMarkdownToolbarClick;
            TopToolbar.ToggleThemeClick += OnToggleThemeClick;
            TopToolbar.SplitNoneClick += OnSplitNoneClick;
            TopToolbar.SplitVerticalClick += OnSplitVerticalClick;
            TopToolbar.SplitHorizontalClick += OnSplitHorizontalClick;
            TopToolbar.SettingsClick += OnSettingsClick;
        }

        private void WireEditorWorkspaceEvents()
        {
            EditorWorkspace.PrimaryAddTabButtonClick += OnEditorTabViewAddTabClick;
            EditorWorkspace.PrimaryTabCloseRequested += OnEditorTabViewTabCloseRequested;
            EditorWorkspace.PrimarySelectionChanged += OnEditorTabViewSelectionChanged;
            EditorWorkspace.SecondaryAddTabButtonClick += OnEditorTabView2AddTabClick;
            EditorWorkspace.SecondaryTabCloseRequested += OnEditorTabView2TabCloseRequested;
            EditorWorkspace.SecondarySelectionChanged += OnEditorTabView2SelectionChanged;
            EditorWorkspace.TabViewGotFocus += OnTabViewGotFocus;
            EditorWorkspace.MoveTabLeftClick += OnMoveTabLeftClick;
            EditorWorkspace.MoveTabRightClick += OnMoveTabRightClick;
            EditorWorkspace.TerminalPanelHeightChanged += async (_, _) => await SaveUiLayoutSettingsAsync();
        }

        private void SetupCustomTitleBar()
        {
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _terminalShortcutService.Stop();

            _previewDebounceTimer.Stop();
            _autoSaveTimer.Stop();
            _gitAutoRefreshTimer.Stop();

            EditorWorkspace.StopAllTerminalSessions();

            foreach (var bridge in _tabBridges.Values)
            {
                try { bridge.WebView.Close(); }
                catch { }
            }
            _tabBridges.Clear();

            try { PreviewWebView.Close(); }
            catch { }
        }

        private void CleanupBeforeRestart()
        {
            _terminalShortcutService.Stop();

            _previewDebounceTimer.Stop();
            _autoSaveTimer.Stop();
            _gitAutoRefreshTimer.Stop();

            EditorWorkspace.StopAllTerminalSessions();

            foreach (var bridge in _tabBridges.Values)
            {
                try { bridge.WebView.Close(); }
                catch { }
            }
            _tabBridges.Clear();

            try { PreviewWebView.Close(); }
            catch { }
        }

        private async Task SaveUiLayoutSettingsAsync()
        {
            try
            {
                var settings = _settingsService.CurrentSettings;
                WindowPlacementService.CaptureRestoredWindowPlacement(AppWindow, settings);
                settings.TerminalPanelHeight = EditorWorkspace.PersistedTerminalPanelHeight;
                settings.LeftSidebarVisible = _shellPanelLayoutService.IsLeftSidebarVisible;
                settings.RightSidebarVisible = _shellPanelLayoutService.IsRightSidebarVisible;

                await _settingsService.SaveSettingsAsync(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save UI layout settings: {ex.Message}");
            }
        }

        private async Task SaveSidebarVisibilitySettingsAsync()
        {
            try
            {
                var settings = _settingsService.CurrentSettings;
                settings.LeftSidebarVisible = _shellPanelLayoutService.IsLeftSidebarVisible;
                settings.RightSidebarVisible = _shellPanelLayoutService.IsRightSidebarVisible;
                await _settingsService.SaveSettingsAsync(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save sidebar visibility settings: {ex.Message}");
            }
        }

        private void OnWindowActivationChanged(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                _terminalShortcutService.Stop();
            }
            else
            {
                _terminalShortcutService.Start();
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

            // Load settings first so InitializeEditorWebView can use the correct theme from the start
            await _settingsService.LoadSettingsAsync();
            WindowPlacementService.ApplySavedWindowPlacement(AppWindow, _settingsService.CurrentSettings);
            EditorWorkspace.LastTerminalHeight = Math.Clamp(_settingsService.CurrentSettings.TerminalPanelHeight, 120, 600);

            if (filesToOpen.Count > 0)
            {
                foreach (var filePath in filesToOpen)
                {
                    _ = LoadFileIntoTabAsync(filePath);
                }
            }
            else
            {
                // Open a blank tab instantly (so the tab and Monaco editor container are rendered immediately)
                OpenNewTab();
            }

            // 2. Apply settings to UI and initialize preview panel WebView2 in the background
            TopToolbar.WordWrapIsChecked = _settingsService.CurrentSettings.WordWrap;
            LeftPanelToggle.IsChecked = _settingsService.CurrentSettings.LeftSidebarVisible;
            ApplyLeftSidebarVisibility(_settingsService.CurrentSettings.LeftSidebarVisible);
            bool rightPanelVisible = _settingsService.CurrentSettings.RightSidebarVisible && _settingsService.CurrentSettings.DefaultMarkdownEnabled;
            RightPanelToggle.IsChecked = rightPanelVisible;
            ApplyPreviewVisibility(rightPanelVisible);
            TopToolbar.MarkdownToolbarIsChecked = _settingsService.CurrentSettings.DefaultMarkdownToolbarEnabled;
            MarkdownToolbar.Visibility = _settingsService.CurrentSettings.DefaultMarkdownToolbarEnabled ? Visibility.Visible : Visibility.Collapsed;
            PreviewModeCombo.SelectedIndex = _settingsService.CurrentSettings.PreviewMode switch
            {
                "HTML" => 1,
                "LaTeX" => 2,
                "Aozora" => 3,
                _ => 0
            };
            ApplyUiPersonalization(_settingsService.CurrentSettings);
            LocalizeUi();
            ApplyToolbarSettings(_settingsService.CurrentSettings);

            // If we have a Git repo path from a loaded file, refresh Git status UI
            if (!string.IsNullOrEmpty(_currentRepoPath))
            {
                _ = RefreshGitStatusUIAsync();
                _gitAutoRefreshTimer.Start();
            }

            await InitializePreviewWebViewAsync();

            // 3. Load Snippets, Favorites and Recent Files
            await _snippetsController.LoadAsync();
            _favoritesRecentController.RefreshFavorites();
            _favoritesRecentController.LoadRecentFiles();
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
                PreviewWebView.WebMessageReceived += OnPreviewWebMessageReceived;

                PreviewWebView.NavigationCompleted += (s, e) =>
                {
                    var tab = GetActiveTab();
                    if (tab != null)
                    {
                        UpdateLivePreview(tab);
                    }
                };

                // Load preview renderer page
                PreviewWebView.Source = new Uri("http://ueditor.local/preview.html");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to init preview webview: {ex.Message}");
            }
        }

        private void OnPreviewWebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string json = NormalizeWebMessageJson(args);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp))
                {
                    return;
                }

                if (!string.Equals(typeProp.GetString(), "previewRequestLines", StringComparison.Ordinal))
                {
                    return;
                }

                int requestId = root.TryGetProperty("requestId", out var requestIdProp) ? requestIdProp.GetInt32() : 0;
                int startLine = root.TryGetProperty("startLine", out var startLineProp) ? startLineProp.GetInt32() : 1;
                int count = root.TryGetProperty("count", out var countProp) ? countProp.GetInt32() : 80;
                var activeTab = GetActiveTab();
                IReadOnlyList<string> lines = Array.Empty<string>();
                if (activeTab != null && _editorSessions.TryGetValue(activeTab.Id, out var session))
                {
                    lines = session.GetLines(startLine, count);
                }

                var reply = new
                {
                    action = "previewLines",
                    requestId = requestId,
                    startLine = startLine,
                    lines = lines
                };
                PreviewWebView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(reply));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed handling preview line request: {ex.Message}");
            }
        }

        #endregion

        #region Tab Operations (탭 비즈니스 로직)

        private void OpenNewTab(
            string? filePath = null,
            string content = "",
            bool isReadOnly = false,
            string encodingName = "UTF-8",
            bool encodingWasAutoDetected = true,
            ITextModel? textModel = null)
        {
            var tab = new OpenedTab();
            tab.EncodingName = encodingName;
            tab.EncodingWasAutoDetected = encodingWasAutoDetected;

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
                tab.Language = _languageDetectionService.GetMonacoLanguageName(filePath);
                if (File.Exists(filePath))
                {
                    _favoritesRecentController.AddRecentFile(filePath);
                }
            }
            else
            {
                tab.Title = "제목 없음";
                tab.Content = "";
            }

            var documentModel = textModel ?? LineArrayTextModel.FromText(content);
            var session = new EditorDocumentSession(tab, documentModel);
            _editorSessions[tab.Id] = session;

            _viewModel.Tabs.Add(tab);

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
            var targetTabView = GetCurrentActiveTabView();

            // Tab right-click context menu
            var tabContextMenu = new MenuFlyout();
            var bookmarkItem = new MenuFlyoutItem { Text = "북마크 추가" };
            bookmarkItem.Click += (s, args) => OnTabAddBookmark(tab);
            tabContextMenu.Items.Add(bookmarkItem);

            var openFolderItem = new MenuFlyoutItem { Text = "해당 폴더로 이동" };
            openFolderItem.Click += (s, args) => OnTabOpenFolder(tab);
            tabContextMenu.Items.Add(openFolderItem);

            tabContextMenu.Items.Add(new MenuFlyoutSeparator());

            var closeRightItem = new MenuFlyoutItem { Text = "오른쪽 탭 닫기" };
            closeRightItem.Click += (s, args) => OnCloseRightTabs(tab, tabItem, targetTabView);
            tabContextMenu.Items.Add(closeRightItem);

            var closeLeftItem = new MenuFlyoutItem { Text = "왼쪽 탭 닫기" };
            closeLeftItem.Click += (s, args) => OnCloseLeftTabs(tab, tabItem, targetTabView);
            tabContextMenu.Items.Add(closeLeftItem);

            tabItem.ContextFlyout = tabContextMenu;

            // Apply UI font directly to TabViewItem to guarantee visual style consistency
            try
            {
                if (!string.IsNullOrEmpty(_settingsService.CurrentSettings.UiFontFamily))
                {
                    tabItem.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(_settingsService.CurrentSettings.UiFontFamily);
                }
            }
            catch { }

            var bridge = new MonacoBridge(editorWebView);
            _tabBridges[tab.Id] = (editorWebView, bridge);

            WireEditorBridge(bridge, tab, tabItem, session, isReadOnly);

            // Initialize editor inside WebView2 using virtual host mappings
            InitializeEditorWebView(editorWebView, bridge);

            targetTabView.TabItems.Add(tabItem);
            targetTabView.SelectedItem = tabItem;

            UpdateStatusFileStats(tab);
            SyncEncodingCombo(tab);
        }

        private void WireEditorBridge(
            MonacoBridge bridge,
            OpenedTab tab,
            TabViewItem tabItem,
            EditorDocumentSession session,
            bool isReadOnly)
        {
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
                            _terminalShortcutService.RequestToggle();
                            break;
                        case "closeTab":
                            OnCloseActiveTabShortcutInvoked(null!, null!);
                            break;
                        case "searchAll":
                            EnsureLeftPanelVisible();
                            ShowLeftSidebarPage(3);
                            this.DispatcherQueue.TryEnqueue(() =>
                            {
                                SearchQueryInput.Focus(FocusState.Programmatic);
                                SearchQueryInput.Focus(FocusState.Keyboard);
                            });
                            break;
                        case "undo":
                            {
                                var text = session.Undo();
                                if (text != null)
                                {
                                    MarkTabDirty(tab, tabItem);
                                    SchedulePreview(tab);
                                    _ = bridge.SetTextAsync(text);
                                }
                            }
                            break;
                        case "redo":
                            {
                                var text = session.Redo();
                                if (text != null)
                                {
                                    MarkTabDirty(tab, tabItem);
                                    SchedulePreview(tab);
                                    _ = bridge.SetTextAsync(text);
                                }
                            }
                            break;
                    }
                });
            };

            bridge.EditorReady += async () =>
            {
                await bridge.InitializeModelAsync(
                    session.Model.LineCount,
                    tab.Language,
                    _settingsService.CurrentSettings,
                    isReadOnly);
            };

            bridge.LinesRequested += async (requestId, startLine, count) =>
            {
                try
                {
                    var lines = session.GetLines(startLine, count);
                    await bridge.SendLinesAsync(requestId, startLine, lines);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to send editor lines: {ex.Message}");
                }
            };

            bridge.LineChanged += (lineNumber, text) =>
            {
                session.ReplaceLine(lineNumber, text);
                MarkTabDirty(tab, tabItem);
                SchedulePreview(tab);
            };

            bridge.LineInsertRequested += async (lineNumber, text) =>
            {
                int lineCount = session.InsertLine(lineNumber, text);
                MarkTabDirty(tab, tabItem);
                await bridge.UpdateLineCountAsync(lineCount);
                SchedulePreview(tab);
            };

            bridge.LineSplitRequested += async (lineNumber, before, after) =>
            {
                int lineCount = session.SplitLine(lineNumber, before, after);
                MarkTabDirty(tab, tabItem);
                await bridge.UpdateLineCountAsync(lineCount);
                SchedulePreview(tab);
            };

            bridge.MergeLineWithPreviousRequested += async (lineNumber) =>
            {
                int lineCount = session.MergeLineWithPrevious(lineNumber);
                MarkTabDirty(tab, tabItem);
                await bridge.UpdateLineCountAsync(lineCount);
                SchedulePreview(tab);
            };

            bridge.DeleteLineRequested += async (lineNumber) =>
            {
                int lineCount = session.DeleteLine(lineNumber);
                MarkTabDirty(tab, tabItem);
                await bridge.UpdateLineCountAsync(lineCount);
                SchedulePreview(tab);
            };

            bridge.FindRequested += async (query, startLine, startColumn, reverse, matchCase) =>
            {
                var result = session.Find(query, startLine, startColumn, reverse, matchCase);
                await bridge.SendFindResultAsync(result, query);
            };

            bridge.FindAllRequested += async (query, matchCase) =>
            {
                var results = session.FindAll(query, matchCase);
                await bridge.SendFindAllResultsAsync(results, query);
            };

            bridge.ContentChanged += (_) =>
            {
                MarkTabDirty(tab, tabItem);
                SchedulePreview(tab);
                UpdateLanguageUI(tab);
            };

            bridge.CursorChanged += (line, col) =>
            {
                if (GetActiveTab() == tab)
                {
                    StatusLine.Text = line.ToString();
                    StatusCol.Text = col.ToString();
                    _ = bridge.RequestSelectionAsync();
                }
            };

            bridge.SelectionReceived += (selectedText) =>
            {
                _llmAssistantController.SetSelectionText(selectedText);
                if (GetActiveTab() == tab)
                {
                    if (string.IsNullOrEmpty(selectedText))
                    {
                        SelectionStatsText.Text = GetLocalizedString("SelectionNoneBlocked", "선택 영역: 없음 (전체 전송 차단 활성화)");
                    }
                    else
                    {
                        string fmt = GetLocalizedString("SelectionStats", "선택 영역: {0} 글자 수 (약 {1} 토큰)");
                        SelectionStatsText.Text = string.Format(fmt, selectedText.Length.ToString("N0"), (selectedText.Length / 4).ToString("N0"));
                    }
                }
            };
        }

        private void MarkTabDirty(OpenedTab tab, TabViewItem tabItem)
        {
            if (!tab.IsDirty)
            {
                tab.IsDirty = true;
                tabItem.Header = tab.DisplayTitle;
            }
        }

        private void SchedulePreview(OpenedTab tab)
        {
            _activeTabForPreview = tab;
            _previewDebounceTimer.Stop();
            _previewDebounceTimer.Start();
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

                var settings = _settingsService.CurrentSettings;
                var url = $"http://ueditor.local/editor.html?theme={Uri.EscapeDataString(settings.Theme)}" +
                    $"&fontSize={settings.FontSize}" +
                    $"&fontFamily={Uri.EscapeDataString(settings.FontFamily)}" +
                    $"&wordWrap={(settings.WordWrap ? "pre-wrap" : "pre")}";
                if (!string.IsNullOrEmpty(settings.CustomBackgroundColor))
                    url += $"&customBg={Uri.EscapeDataString(settings.CustomBackgroundColor)}";
                if (!string.IsNullOrEmpty(settings.CustomForegroundColor))
                    url += $"&customFg={Uri.EscapeDataString(settings.CustomForegroundColor)}";

                bridge.LoadEditor(url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed initialization of editor: {ex.Message}");
            }
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

                // Sync current combo mode selection based on index (index 0=Markdown, 1=HTML, 2=LaTeX, 3=Aozora)
                string mode = PreviewModeCombo.SelectedIndex switch
                {
                    1 => "html",
                    2 => "latex",
                    3 => "aozora",
                    _ => "markdown"
                };

                var renderMsg = new
                {
                    action = "initVirtualPreview",
                    lineCount = _editorSessions.TryGetValue(tab.Id, out var session) ? session.Model.LineCount : 1,
                    mode = mode,
                    wordWrap = _settingsService.CurrentSettings.WordWrap,
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
            picker.FileTypeFilter.Add(".fs");
            picker.FileTypeFilter.Add(".vb");
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".jsonc");
            picker.FileTypeFilter.Add(".tex");
            picker.FileTypeFilter.Add(".py");
            picker.FileTypeFilter.Add(".java");
            picker.FileTypeFilter.Add(".kt");
            picker.FileTypeFilter.Add(".swift");
            picker.FileTypeFilter.Add(".php");
            picker.FileTypeFilter.Add(".rb");
            picker.FileTypeFilter.Add(".rs");
            picker.FileTypeFilter.Add(".go");
            picker.FileTypeFilter.Add(".dart");
            picker.FileTypeFilter.Add(".lua");
            picker.FileTypeFilter.Add(".cpp");
            picker.FileTypeFilter.Add(".c");
            picker.FileTypeFilter.Add(".cc");
            picker.FileTypeFilter.Add(".cxx");
            picker.FileTypeFilter.Add(".h");
            picker.FileTypeFilter.Add(".hpp");
            picker.FileTypeFilter.Add(".xml");
            picker.FileTypeFilter.Add(".xaml");
            picker.FileTypeFilter.Add(".sql");
            picker.FileTypeFilter.Add(".sh");
            picker.FileTypeFilter.Add(".ps1");
            picker.FileTypeFilter.Add(".yaml");
            picker.FileTypeFilter.Add(".yml");
            picker.FileTypeFilter.Add(".toml");
            picker.FileTypeFilter.Add(".ini");
            picker.FileTypeFilter.Add(".diff");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await LoadFileIntoTabAsync(file.Path);
            }
        }

        internal async Task LoadFileIntoTabAsync(string filePath)
        {
            try
            {
                string? repoRoot = _gitService.FindRepositoryRoot(Path.GetDirectoryName(filePath));
                if (!string.IsNullOrEmpty(repoRoot))
                {
                    _currentRepoPath = repoRoot;
                    await RefreshGitStatusUIAsync();
                }

                var readResult = await LineArrayTextModel.LoadFromFileAsync(filePath, "Auto");
                OpenNewTab(
                    filePath,
                    "",
                    encodingName: readResult.EncodingName,
                    encodingWasAutoDetected: readResult.EncodingWasAutoDetected,
                    textModel: readResult.Model);
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
                        string? folderPath = Path.GetDirectoryName(item.Path);
                        if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
                        {
                            await NavigateExplorerToFolderAsync(folderPath);
                        }

                        await LoadFileIntoTabAsync(item.Path);
                    }
                    else if (Directory.Exists(item.Path))
                    {
                        await NavigateExplorerToFolderAsync(item.Path);
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
                var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null)
                {
                    await SaveTabAsync(tab);
                }
            }
        }

        private async void OnSaveAsFileClick(object sender, RoutedEventArgs e)
        {
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId)
            {
                var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null)
                {
                    await SaveAsTabAsync(tab);
                }
            }
        }

        private async void OnWordWrapToggleClick(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.CurrentSettings;
            settings.WordWrap = TopToolbar.WordWrapIsChecked;
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

        private void OnTopMostToggleClick(object sender, RoutedEventArgs e)
        {
            _stickyNoteService.ApplyTopMost(this, TopToolbar.TopMostIsChecked);
        }

        private void OnStickyNoteClick(object sender, RoutedEventArgs e)
        {
            _stickyNoteService.ShowOrActivate(this);
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
            ShowLeftSidebarPage(3);
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
            int safeIndex = LeftSidebarTabView.ShowPage(index);

            if (safeIndex == 1)
            {
                LeftSidebarTabView.FavoritesFileTabButton.IsChecked = true;
                LeftSidebarTabView.FavoritesFolderTabButton.IsChecked = false;
                _favoritesRecentController.RefreshFavorites(true);
            }

            if (safeIndex == 3)
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
            if (LeftPanelToggle.IsChecked == true && _shellPanelLayoutService.IsLeftSidebarVisible)
            {
                return;
            }

            LeftPanelToggle.IsChecked = true;
            ApplyLeftSidebarVisibility(true);
            _ = SaveSidebarVisibilitySettingsAsync();
        }

        private void ApplyLeftSidebarVisibility(bool show)
        {
            _shellPanelLayoutService.ApplyLeftSidebarVisibility(show);
        }

        private async void OnToggleLeftPanelClick(object sender, RoutedEventArgs e)
        {
            bool show = LeftPanelToggle.IsChecked == true;
            ApplyLeftSidebarVisibility(show);
            await SaveSidebarVisibilitySettingsAsync();
        }

        private async void OnTogglePreviewClick(object sender, RoutedEventArgs e)
        {
            ApplyPreviewVisibility(RightPanelToggle.IsChecked == true);
            await SaveSidebarVisibilitySettingsAsync();
        }

        private async void OnToggleThemeClick(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.CurrentSettings;
            settings.Theme = settings.Theme == "Light" ? "Dark" : "Light";
            await _settingsService.SaveSettingsAsync(settings);
            ApplyUiPersonalization(settings);
            RefreshAllSplitters();

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
                var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null) UpdateLivePreview(tab);
            }
        }

        private void RefreshAllSplitters()
        {
            LeftSplitter.RefreshTheme();
            RightSplitter.RefreshTheme();
            EditorWorkspace.RefreshSplitters();
        }

        private string GetLocalizedString(string key, string fallback)
        {
            return _localizationService.GetString(key, fallback);
        }

        private void ApplyResourceLanguage()
        {
            _localizationService.ApplyResourceLanguage();
        }

        private void LocalizeUi()
        {
            try
            {
                ApplyResourceLanguage();
                string GetString(string key, string fallback) => GetLocalizedString(key, fallback);

                TopToolbar.Localize(GetString);
                EditorWorkspace.Localize(GetString);
                LeftSidebarTabView.Localize(GetString, string.IsNullOrEmpty(_currentFolderPath), IsGitNotDetectedText);
                StatusBarPane.Localize(GetString, IsGitNotDetectedText);
                PreviewGrid.Localize(GetString);
                MarkdownToolbar.LocalizeTooltips(GetString);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to localize UI: {ex.Message}");
            }
        }

        private static bool IsGitNotDetectedText(string text)
        {
            return text.Equals("Git: 감지 안됨", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("Git: Not Detected", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("Git: 検出されていません", StringComparison.OrdinalIgnoreCase);
        }

        private async void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            // Suspend native terminal windows so settings dialog is not hidden behind them
            bool terminalWasVisible = EditorWorkspace.IsTerminalVisible;
            if (terminalWasVisible)
                TerminalPane.SuspendNativeWindows();

            var settings = _settingsService.CurrentSettings;
            string oldLanguage = settings.Language;

            string GetSettingsString(string key, string fallback) => GetLocalizedString(key, fallback);

            var result = await _settingsDialogService.ShowAsync(settings, this.Content.XamlRoot, GetSettingsString);
            if (terminalWasVisible)
                TerminalPane.ResumeNativeWindows();
            if (!result.Saved)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.ApiKeyStatusMessage))
            {
                _llmAssistantController.SetOutput(result.ApiKeyStatusMessage);
            }

            await _settingsService.SaveSettingsAsync(settings);
            ApplyResourceLanguage();
            ApplyPreviewVisibility(settings.DefaultMarkdownEnabled);
            TopToolbar.MarkdownToolbarIsChecked = settings.DefaultMarkdownToolbarEnabled;
            MarkdownToolbar.Visibility = settings.DefaultMarkdownToolbarEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Enable auto-save if setting is on and git is available
            _autoSaveEnabled = settings.AutoSave && !string.IsNullOrEmpty(_currentRepoPath);
            if (_autoSaveEnabled) _autoSaveTimer.Start();
            else _autoSaveTimer.Stop();
            TopToolbar.WordWrapIsChecked = settings.WordWrap;
            ApplyUiPersonalization(settings);
            LocalizeUi();
            ApplyToolbarSettings(settings);

            if (oldLanguage != settings.Language && await ConfirmRestartForLanguageChangeAsync(GetSettingsString))
            {
                CleanupBeforeRestart();
                Microsoft.Windows.AppLifecycle.AppInstance.Restart("");
                return;
            }

            await ApplySettingsToOpenEditorsAsync(settings);
            RefreshActivePreview();
        }

        private async Task<bool> ConfirmRestartForLanguageChangeAsync(Func<string, string, string> getString)
        {
            var restartDialog = new ContentDialog
            {
                Title = getString("LanguageChangedTitle", "Language Change"),
                Content = getString("LanguageChangedMessage", "You must restart the application to apply the language settings. Would you like to restart now?"),
                PrimaryButtonText = getString("Restart", "Restart"),
                CloseButtonText = getString("No", "Later"),
                XamlRoot = this.Content.XamlRoot
            };

            return await restartDialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task ApplySettingsToOpenEditorsAsync(EditorSettings settings)
        {
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
                    string updateJson = System.Text.Json.JsonSerializer.Serialize(updateMsg);
                    grp.WebView.CoreWebView2.PostWebMessageAsJson(updateJson);
                }
            }
        }

        private void RefreshActivePreview()
        {
            var activeTabView = GetCurrentActiveTabView();
            if (activeTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId)
            {
                var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null)
                {
                    UpdateLivePreview(tab);
                }
            }
        }

        #endregion

        #region Custom Splitters Event Handlers

        private void OnLeftSplitterPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _shellPanelLayoutService.OnLeftSplitterPointerPressed(sender, e);
        }

        private void OnLeftSplitterPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _shellPanelLayoutService.OnLeftSplitterPointerMoved(sender, e);
        }

        private void OnLeftSplitterPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _shellPanelLayoutService.OnLeftSplitterPointerReleased(sender, e);
        }

        private void OnRightSplitterPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _shellPanelLayoutService.OnRightSplitterPointerPressed(sender, e);
        }

        private void OnRightSplitterPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _shellPanelLayoutService.OnRightSplitterPointerMoved(sender, e);
        }

        private void OnRightSplitterPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _shellPanelLayoutService.OnRightSplitterPointerReleased(sender, e);
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
                _currentRepoPath = _gitService.FindRepositoryRoot(folder.Path) ?? string.Empty;
                LoadDirectoryRoot(folder.Path);

                // Trigger Git branch detection & status update
                await RefreshGitStatusUIAsync();
            }
        }

        private void LoadDirectoryRoot(string folderPath)
        {
            _viewModel.ExplorerItems.Clear();
            _currentFolderPath = folderPath;

            foreach (var item in _explorerDirectoryService.CreateDirectoryItems(folderPath))
            {
                _viewModel.ExplorerItems.Add(item);
            }

            ExplorerStatusText.Text = $"{folderPath}\n{_viewModel.ExplorerItems.Count:N0}개 항목";
        }

        private async Task NavigateExplorerToFolderAsync(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

            _currentFolderPath = folderPath;
            _currentRepoPath = _gitService.FindRepositoryRoot(folderPath) ?? string.Empty;
            LoadDirectoryRoot(folderPath);

            // Ensure the left panel is visible and switch to Explorer page (index 0)
            EnsureLeftPanelVisible();
            ShowLeftSidebarPage(0);

            await RefreshGitStatusUIAsync();
        }

        private void OnOpenTerminalClick(object sender, RoutedEventArgs e)
        {
            ToggleTerminal();
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

        #region Terminal Panel Layout

        private void ToggleTerminal()
        {
            TopToolbar.TerminalIsChecked = EditorWorkspace.ToggleTerminal(GetTerminalWorkingDirectory);
        }

        private void OnTerminalPaneCloseRequested(object? sender, EventArgs e)
        {
            ToggleTerminal();
        }

        private void OnTerminalPaneSessionsEmptied(object? sender, EventArgs e)
        {
            if (EditorWorkspace.HideTerminalPanelIfEmpty())
            {
                TopToolbar.TerminalIsChecked = false;
            }
        }

        #endregion

        #region Split Editor Layout

        private void OnTabViewGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TabView tabView)
            {
                EditorWorkspace.ActiveTabView = tabView;
                var activeTab = GetActiveTab();
                if (activeTab != null)
                {
                    UpdateStatusFileStats(activeTab);
                    UpdateLivePreview(activeTab);
                    UpdateLanguageUI(activeTab);
                    SyncEncodingCombo(activeTab);
                }
            }
        }

        private TabView GetCurrentActiveTabView()
        {
            return EditorWorkspace.GetCurrentActiveTabView();
        }

        private void OnSplitNoneClick(object sender, RoutedEventArgs e) => EditorWorkspace.SetSplitMode(EditorSplitMode.None, () => OpenNewTab());
        private void OnSplitVerticalClick(object sender, RoutedEventArgs e) => EditorWorkspace.SetSplitMode(EditorSplitMode.Vertical, () => OpenNewTab());
        private void OnSplitHorizontalClick(object sender, RoutedEventArgs e) => EditorWorkspace.SetSplitMode(EditorSplitMode.Horizontal, () => OpenNewTab());

        private void OnEditorTabView2AddTabClick(TabView sender, object args)
        {
            EditorWorkspace.ActiveTabView = sender;
            OpenNewTab();
        }

        private void OnEditorTabView2TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            EditorWorkspace.ActiveTabView = sender;
            OnEditorTabViewTabCloseRequested(sender, args);
        }

        private async void OnEditorTabView2SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EditorWorkspace.ActiveTabView = EditorTabView2;
            if (EditorTabView2.SelectedItem is TabViewItem activeTabItem)
            {
                await HandleTabViewSelectionChangedAsync(activeTabItem);
            }
        }

        private async Task HandleTabViewSelectionChangedAsync(TabViewItem activeTabItem)
        {
            _llmAssistantController.ClearSelection();
            SelectionStatsText.Text = GetLocalizedString("SelectionNoneBlocked", "선택 영역: 없음 (전체 전송 차단 활성화)");

            if (activeTabItem.Tag is string tabId)
            {
                var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab != null)
                {
                    UpdateStatusFileStats(tab);
                    UpdateLivePreview(tab);
                    UpdateLanguageUI(tab);
                    SyncEncodingCombo(tab);

                    if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                    {
                        await bridgeGroup.Bridge.RequestSelectionAsync();
                    }
                }
            }
        }

        private void OnMoveTabLeftClick(object sender, RoutedEventArgs e)
        {
            var activeTabView = GetCurrentActiveTabView();
            if (activeTabView == null || activeTabView.TabItems.Count <= 1) return;

            int index = activeTabView.SelectedIndex;
            if (index < 0) return;

            var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

            if (ctrl || shift)
            {
                if (index > 0)
                {
                    var item = activeTabView.TabItems[index] as TabViewItem;
                    if (item != null)
                    {
                        activeTabView.TabItems.RemoveAt(index);
                        activeTabView.TabItems.Insert(index - 1, item);
                        activeTabView.SelectedIndex = index - 1;

                        if (item.Tag is string tabId)
                        {
                            var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
                            if (tab != null)
                            {
                                int tabIdx = _viewModel.Tabs.IndexOf(tab);
                                if (tabIdx > 0)
                                {
                                    _viewModel.Tabs.RemoveAt(tabIdx);
                                    _viewModel.Tabs.Insert(tabIdx - 1, tab);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (index > 0)
                {
                    activeTabView.SelectedIndex = index - 1;
                }
                else
                {
                    activeTabView.SelectedIndex = activeTabView.TabItems.Count - 1;
                }
            }
        }

        private void OnMoveTabRightClick(object sender, RoutedEventArgs e)
        {
            var activeTabView = GetCurrentActiveTabView();
            if (activeTabView == null || activeTabView.TabItems.Count <= 1) return;

            int index = activeTabView.SelectedIndex;
            if (index < 0) return;

            var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

            if (ctrl || shift)
            {
                if (index < activeTabView.TabItems.Count - 1)
                {
                    var item = activeTabView.TabItems[index] as TabViewItem;
                    if (item != null)
                    {
                        activeTabView.TabItems.RemoveAt(index);
                        activeTabView.TabItems.Insert(index + 1, item);
                        activeTabView.SelectedIndex = index + 1;

                        if (item.Tag is string tabId)
                        {
                            var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
                            if (tab != null)
                            {
                                int tabIdx = _viewModel.Tabs.IndexOf(tab);
                                if (tabIdx < _viewModel.Tabs.Count - 1)
                                {
                                    _viewModel.Tabs.RemoveAt(tabIdx);
                                    _viewModel.Tabs.Insert(tabIdx + 1, tab);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (index < activeTabView.TabItems.Count - 1)
                {
                    activeTabView.SelectedIndex = index + 1;
                }
                else
                {
                    activeTabView.SelectedIndex = 0;
                }
            }
        }

        #endregion

        #region Explorer Directory Items

        private void OnExplorerUpClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentFolderPath)) return;

            var parent = Directory.GetParent(_currentFolderPath);
            if (parent == null) return;

            _currentRepoPath = _gitService.FindRepositoryRoot(parent.FullName) ?? string.Empty;
            LoadDirectoryRoot(parent.FullName);
        }

        private void OnFileListViewDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            var item = VisualTreeDataContext.FindFromOriginalSource<ExplorerItem>(e.OriginalSource) ?? FileListView.SelectedItem as ExplorerItem;
            if (item == null) return;

            if (item.IsFolder)
            {
                _currentRepoPath = _gitService.FindRepositoryRoot(item.Path) ?? string.Empty;
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
            if (sender is FrameworkElement element && element.ContextFlyout is MenuFlyout flyout && flyout.Items.Count >= 2)
            {
                ((MenuFlyoutItem)flyout.Items[0]).Text = GetLocalizedString("ExplorerAddToFavorites", "즐겨찾기에 추가");
                ((MenuFlyoutItem)flyout.Items[1]).Text = GetLocalizedString("ExplorerAddFolderToFavorites", "폴더를 즐겨찾기에 추가");
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
                var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
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
            _viewModel.Tabs.Remove(tab);
            if (EditorTabView.TabItems.Contains(tabItem))
            {
                EditorTabView.TabItems.Remove(tabItem);
            }
            else if (EditorTabView2.TabItems.Contains(tabItem))
            {
                EditorTabView2.TabItems.Remove(tabItem);
            }

            if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup))
            {
                bridgeGroup.WebView.Close();
                _tabBridges.Remove(tab.Id);
            }
            _editorSessions.Remove(tab.Id);

            if (EditorTabView.TabItems.Count == 0 && EditorTabView2.TabItems.Count == 0)
            {
                OpenNewTab();
            }
        }

        private async void OnEditorTabViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EditorWorkspace.ActiveTabView = EditorTabView;
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem)
            {
                await HandleTabViewSelectionChangedAsync(activeTabItem);
            }
        }

        #endregion

        #region Preview Header Syncs

        private void OnRefreshPreviewClick(object sender, RoutedEventArgs e)
        {
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId)
            {
                var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
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
                    3 => "Aozora",
                    _ => "Markdown"
                };
                await _settingsService.SaveSettingsAsync(settings);
            }

            if (PreviewWebView != null && PreviewWebView.CoreWebView2 != null)
            {
                OnRefreshPreviewClick(this, new RoutedEventArgs());
            }
        }

        private async void OnOpenPreviewInBrowserClick(object sender, RoutedEventArgs e)
        {
            var tab = GetActiveTab();
            if (tab == null)
            {
                ShowErrorMessage("브라우저 열기", "브라우저로 열 활성 탭이 없습니다.");
                return;
            }

            try
            {
                string targetPath = tab.FilePath ?? string.Empty;
                bool isSavedHtml = !string.IsNullOrWhiteSpace(targetPath) &&
                    File.Exists(targetPath) &&
                    (targetPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                     targetPath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase));

                if (!isSavedHtml)
                {
                    string previewDir = Path.Combine(Path.GetTempPath(), "Ueditor", "Preview");
                    Directory.CreateDirectory(previewDir);
                    targetPath = Path.Combine(previewDir, $"preview-{tab.Id}.html");
                    string previewText = _editorSessions.TryGetValue(tab.Id, out var session)
                        ? session.GetText()
                        : tab.Content ?? string.Empty;
                    await File.WriteAllTextAsync(targetPath, previewText, Encoding.UTF8);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = targetPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowErrorMessage("브라우저 열기 실패", ex.Message);
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

        private async void OnMarkdownToolbarCommandRequested(object? sender, MarkdownCommandRequestedEventArgs e)
        {
            await ApplyMarkdownCommandToActiveEditorAsync(e.Command, e.Color);
        }

        private void OnToggleMarkdownToolbarClick(object sender, RoutedEventArgs e)
        {
            bool show = TopToolbar.MarkdownToolbarIsChecked;
            MarkdownToolbar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region Helpers & UI Triggers

        private void UpdateStatusFileStats(OpenedTab tab)
        {
            if (!string.IsNullOrEmpty(tab.FilePath) && File.Exists(tab.FilePath))
            {
                long bytes = new FileInfo(tab.FilePath).Length;
                string format = GetLocalizedString("StatusFileSizeFormat", "크기: {0:N0} bytes");
                StatusFileStats.Text = string.Format(format, bytes);
            }
            else
            {
                string format = GetLocalizedString("StatusFileSizeFormat", "크기: {0:N0} bytes");
                StatusFileStats.Text = string.Format(format, 0);
            }
        }

        private void SyncEncodingCombo(OpenedTab tab)
        {
            try
            {
                _isSyncingEncodingCombo = true;
                string encodingName = string.IsNullOrWhiteSpace(tab.EncodingName) ? "UTF-8" : tab.EncodingName;
                StatusEncodingCombo.SelectedItem = StatusEncodingCombo.Items.Contains(encodingName) ? encodingName : "UTF-8";
            }
            finally
            {
                _isSyncingEncodingCombo = false;
            }
        }

        private async void OnStatusEncodingSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingEncodingCombo) return;
            if (StatusEncodingCombo.SelectedItem is not string selectedEncoding) return;

            var tab = GetActiveTab();
            if (tab == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(tab.FilePath) || !File.Exists(tab.FilePath))
            {
                tab.EncodingName = selectedEncoding == "Auto" ? "UTF-8" : selectedEncoding;
                tab.EncodingWasAutoDetected = false;
                return;
            }

            if (tab.IsDirty)
            {
                var dialog = new ContentDialog
                {
                    Title = "인코딩 변경",
                    Content = "인코딩을 바꾸면 파일을 다시 읽습니다. 저장하지 않은 변경 사항을 먼저 저장하시겠습니까?",
                    PrimaryButtonText = "저장 후 변경",
                    SecondaryButtonText = "저장 안 함",
                    CloseButtonText = "취소",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    bool saved = await SaveTabAsync(tab);
                    if (!saved)
                    {
                        SyncEncodingCombo(tab);
                        return;
                    }
                }
                else if (result != ContentDialogResult.Secondary)
                {
                    SyncEncodingCombo(tab);
                    return;
                }
            }

            await ReloadTabWithEncodingAsync(tab, selectedEncoding);
        }

        private async Task ReloadTabWithEncodingAsync(OpenedTab tab, string encodingName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tab.FilePath)) return;

                var readResult = await LineArrayTextModel.LoadFromFileAsync(tab.FilePath, encodingName);
                tab.EncodingName = readResult.EncodingName;
                tab.EncodingWasAutoDetected = readResult.EncodingWasAutoDetected;
                tab.IsDirty = false;
                var session = new EditorDocumentSession(tab, readResult.Model);
                _editorSessions[tab.Id] = session;

                var tabItem = EditorTabView.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id)
                           ?? EditorTabView2.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id);
                if (tabItem != null)
                {
                    tabItem.Header = tab.DisplayTitle;
                }

                if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                {
                    await bridgeGroup.Bridge.InitializeModelAsync(
                        session.Model.LineCount,
                        tab.Language,
                        _settingsService.CurrentSettings,
                        isReadOnly: false);
                    await bridgeGroup.Bridge.SetLanguageAsync(tab.FilePath);
                }

                UpdateLivePreview(tab);
                UpdateStatusFileStats(tab);
                UpdateLanguageUI(tab);
                SyncEncodingCombo(tab);
            }
            catch (Exception ex)
            {
                ShowErrorMessage("인코딩 변경 실패", ex.Message);
                SyncEncodingCombo(tab);
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

        private OpenedTab? GetActiveTab()
        {
            var activeTabView = GetCurrentActiveTabView();
            if (activeTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId)
            {
                return _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
            }

            return null;
        }

        private async Task<bool> InsertTextIntoActiveEditorAsync(string text)
        {
            var activeTabView = GetCurrentActiveTabView();
            if (activeTabView.SelectedItem is not TabViewItem activeTabItem ||
                activeTabItem.Tag is not string tabId ||
                !_tabBridges.TryGetValue(tabId, out var bridgeGroup) ||
                bridgeGroup.Bridge == null)
            {
                return false;
            }

            await bridgeGroup.Bridge.InsertTextAsync(text);
            return true;
        }

        private string GetTabTextForLlmContext(OpenedTab tab, int maxChars)
        {
            if (_editorSessions.TryGetValue(tab.Id, out var session))
            {
                return session.GetText(maxChars);
            }

            return tab.Content ?? string.Empty;
        }

        private async Task UpdateGitBranchStatusAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string branch = await _gitService.GetCurrentBranchAsync(path);
            StatusGitBranch.Text = IsGitNotDetectedText(branch) ? GetLocalizedString("GitNotDetected", "Git: 감지 안됨") : branch;
        }

        #endregion

        #region UI Personalization Helper
        private void ApplyUiPersonalization(EditorSettings settings)
        {
            _uiPersonalizationService.Apply(
                settings,
                AppWindow,
                Content as FrameworkElement,
                MarkdownToolbar.SetToolbarBackground);
        }

        private void ApplyToolbarSettings(EditorSettings settings)
        {
            TopToolbar.ApplySettings(settings, GetLocalizedString);
        }
        #endregion

        #region Advanced Git Handlers

        private async Task RefreshGitStatusUIAsync()
        {
            await _gitPanelController.RefreshAsync();
        }

        #endregion

        #region Advanced Search & Replace Handlers

        private async void OnSearchAllFilesClick(object sender, RoutedEventArgs e)
        {
            await _searchReplaceController.SearchAllFilesAsync();
        }

        private async void OnReplaceAllClick(object sender, RoutedEventArgs e)
        {
            await _searchReplaceController.ReplaceAllAsync();
        }

        private async void OnSearchResultDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            await _searchReplaceController.OpenSearchResultAsync(e.OriginalSource);
        }

        private string GetSearchRoot()
        {
            return !string.IsNullOrEmpty(_currentFolderPath) ? _currentFolderPath : _currentRepoPath;
        }

        private long GetLargeFileThresholdBytes()
        {
            return _settingsService.CurrentSettings.LargeFileThresholdMB * 1024L * 1024L;
        }

        private string? GetSaveInitialDirectory()
        {
            if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                return _currentFolderPath;
            return null;
        }

        private bool TryChooseSavePath(OpenedTab tab, string? initialDir)
        {
            string suggestedName = tab.FilePath != null
                ? Path.GetFileNameWithoutExtension(tab.FilePath)
                : tab.Title;
            string? selectedPath = _fileSaveDialogService.ShowSaveDialog(this, suggestedName, initialDir);
            if (string.IsNullOrEmpty(selectedPath))
            {
                return false;
            }

            ApplySavePathToTab(tab, selectedPath);
            return true;
        }

        private void ApplySavePathToTab(OpenedTab tab, string selectedPath)
        {
            tab.FilePath = selectedPath;
            tab.Title = Path.GetFileName(selectedPath);
            tab.Language = _languageDetectionService.GetMonacoLanguageName(selectedPath);
            if (string.IsNullOrWhiteSpace(tab.EncodingName))
            {
                tab.EncodingName = "UTF-8";
            }
        }

        private async Task<bool> SaveTabAsync(OpenedTab tab)
        {
            var tabItem = EditorTabView.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id)
                       ?? EditorTabView2.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id);
            if (tabItem == null) return false;

            if (string.IsNullOrEmpty(tab.FilePath))
            {
                if (!TryChooseSavePath(tab, GetSaveInitialDirectory()))
                    return false;
            }

            try
            {
                if (_editorSessions.TryGetValue(tab.Id, out var session))
                {
                    await session.SaveAsync(tab.FilePath!, tab.EncodingName);
                    tab.Content = session.GetText(120_000);
                }
                else
                {
                    await _fileService.SaveTextFileAsync(tab.FilePath!, tab.Content, tab.EncodingName);
                }

                tab.IsDirty = false;
                tabItem.Header = tab.DisplayTitle;
                UpdateStatusFileStats(tab);
                UpdateLanguageUI(tab);
                SyncEncodingCombo(tab);
                await RefreshGitStatusUIAsync();
                return true;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("저장 실패", ex.Message);
                return false;
            }
        }

        private async Task<bool> SaveAsTabAsync(OpenedTab tab)
        {
            var tabItem = EditorTabView.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id)
                       ?? EditorTabView2.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id);
            if (tabItem == null) return false;

            string? initialDir = GetSaveInitialDirectory();
            if (initialDir == null && !string.IsNullOrEmpty(tab.FilePath))
                initialDir = Path.GetDirectoryName(tab.FilePath);

            var oldFilePath = tab.FilePath;
            var oldTitle = tab.Title;
            var oldLanguage = tab.Language;
            var oldEncodingName = tab.EncodingName;
            if (!TryChooseSavePath(tab, initialDir))
                return false;

            try
            {
                if (_editorSessions.TryGetValue(tab.Id, out var session))
                {
                    await session.SaveAsync(tab.FilePath!, tab.EncodingName);
                    tab.Content = session.GetText(120_000);
                }
                else
                {
                    await _fileService.SaveTextFileAsync(tab.FilePath!, tab.Content, tab.EncodingName);
                }

                tab.IsDirty = false;
                tabItem.Header = tab.DisplayTitle;
                UpdateStatusFileStats(tab);
                UpdateLanguageUI(tab);
                SyncEncodingCombo(tab);
                await RefreshGitStatusUIAsync();
                return true;
            }
            catch (Exception ex)
            {
                tab.FilePath = oldFilePath;
                tab.Title = oldTitle;
                tab.Language = oldLanguage;
                tab.EncodingName = oldEncodingName;
                ShowErrorMessage(GetLocalizedString("SaveFile", "저장") + " - " + GetLocalizedString("SaveAsFile", "다른 이름으로 저장"), ex.Message);
                return false;
            }
        }

        private void OnCloseActiveTabShortcutInvoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
        {
            if (args != null) args.Handled = true;
            var activeTabView = GetCurrentActiveTabView();
            if (activeTabView.SelectedItem is TabViewItem tabItem && tabItem.Tag is string tabId)
            {
                var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
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
            if (_isClosingConfirmed)
            {
                await SaveUiLayoutSettingsAsync();
                return;
            }

            var dirtyTabs = _viewModel.Tabs.Where(t => t.IsDirty).ToList();
            if (dirtyTabs.Count > 0)
            {
                args.Cancel = true; // Prevent immediate close before awaiting UI work
            }

            await SaveUiLayoutSettingsAsync();
            if (dirtyTabs.Count == 0) return;

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

        private void UpdateLanguageUI(OpenedTab tab)
        {
            if (tab == null) return;
            string detected = tab.Language;
            if (detected == "plaintext" || string.IsNullOrEmpty(detected))
            {
                string content = tab.Content;
                if (_editorSessions.TryGetValue(tab.Id, out var session))
                {
                    content = session.GetText(2000);
                }
                detected = _languageDetectionService.DetectLanguageFromContent(content, "plaintext");
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

        private async Task LoadFileIntoTabAndHighlightAsync(SearchResultItem item, string query)
        {
            await LoadFileIntoTabAsync(item.Path);
            await Task.Delay(250);
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId &&
                _tabBridges.TryGetValue(tabId, out var bridgeGroup))
            {
                if (bridgeGroup.Bridge != null)
                {
                    await bridgeGroup.Bridge.RevealLineAsync(item.LineNumber, item.IndexOfMatch, item.MatchLength, query);
                }
                else if (bridgeGroup.WebView?.CoreWebView2 != null)
                {
                    var revealMsg = new { action = "revealLine", lineNumber = item.LineNumber, indexOfMatch = item.IndexOfMatch, matchLength = item.MatchLength, query };
                    bridgeGroup.WebView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(revealMsg));
                }
            }
        }

        private async void OnSearchQueryInputKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await _searchReplaceController.HandleSearchQueryEnterAsync();
            }
        }

        private void ApplyPreviewVisibility(bool show)
        {
            RightPanelToggle.IsChecked = show;
            _shellPanelLayoutService.ApplyPreviewVisibility(show);
            if (show)
            {
                RefreshActivePreview();
            }
        }

        private async void OnCompareFilesClick(object sender, RoutedEventArgs e)
        {
            var selection = await _compareSelectionDialogService.ShowAsync(this, this.Content.XamlRoot, _viewModel.Tabs);
            if (selection == null)
            {
                return;
            }

            if (selection.IsValid)
            {
                await OpenCompareTabAsync(selection.PathA, selection.PathB, selection.ContentA, selection.ContentB);
            }
            else
            {
                ShowErrorMessage("비교 오류", "올바른 두 파일 혹은 탭을 선택해 주세요.");
            }
        }

        private async Task OpenCompareTabAsync(string pathA, string pathB, string? contentA = null, string? contentB = null, string? customTitle = null, string? labelA = null, string? labelB = null)
        {
            if (contentA == null) contentA = await _fileService.ReadTextFileAsync(pathA);
            if (contentB == null) contentB = await _fileService.ReadTextFileAsync(pathB);

            string title = customTitle ?? $"비교: {Path.GetFileName(pathA)} ↔ {Path.GetFileName(pathB)}";

            var tab = new OpenedTab
            {
                Title = title,
                FilePath = "",
                Content = ""
            };

            _viewModel.Tabs.Add(tab);

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
                    titleA = labelA ?? Path.GetFileName(pathA),
                    titleB = labelB ?? Path.GetFileName(pathB),
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

        private void OnTabAddBookmark(OpenedTab tab)
        {
            if (string.IsNullOrEmpty(tab.FilePath)) return;
            _ = _favoritesRecentController.AddFavoritePathAsync(tab.FilePath);
        }

        private async void OnTabOpenFolder(OpenedTab tab)
        {
            if (string.IsNullOrEmpty(tab.FilePath)) return;
            string? folderPath = Path.GetDirectoryName(tab.FilePath);
            if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
            {
                await NavigateExplorerToFolderAsync(folderPath);
            }
        }

        private void OnCloseRightTabs(OpenedTab tab, TabViewItem tabItem, TabView tabView)
        {
            var items = tabView.TabItems.Cast<TabViewItem>().ToList();
            int currentIndex = items.IndexOf(tabItem);
            if (currentIndex < 0) return;
            for (int i = items.Count - 1; i > currentIndex; i--)
            {
                if (items[i].Tag is string tabId)
                {
                    var t = _viewModel.Tabs.FirstOrDefault(x => x.Id == tabId);
                    if (t != null)
                    {
                        if (t.IsDirty) WarnUnsavedAndClose(t, items[i]);
                        else CloseTabAndCleanup(t, items[i]);
                    }
                }
            }
        }

        private void OnCloseLeftTabs(OpenedTab tab, TabViewItem tabItem, TabView tabView)
        {
            var items = tabView.TabItems.Cast<TabViewItem>().ToList();
            int currentIndex = items.IndexOf(tabItem);
            if (currentIndex < 0) return;
            for (int i = currentIndex - 1; i >= 0; i--)
            {
                if (items[i].Tag is string tabId)
                {
                    var t = _viewModel.Tabs.FirstOrDefault(x => x.Id == tabId);
                    if (t != null)
                    {
                        if (t.IsDirty) WarnUnsavedAndClose(t, items[i]);
                        else CloseTabAndCleanup(t, items[i]);
                    }
                }
            }
        }

        private async void OnAutoSaveTimerTick(object? sender, object e)
        {
            if (!_autoSaveEnabled) return;
            if (string.IsNullOrEmpty(_currentRepoPath)) return;
            var dirtyTabs = _viewModel.Tabs.Where(t => t.IsDirty && !string.IsNullOrEmpty(t.FilePath)).ToList();
            foreach (var tab in dirtyTabs)
                await SaveTabAsync(tab);
        }

        private async void OnGitAutoRefreshTimerTick(object? sender, object e)
        {
            if (!string.IsNullOrEmpty(_currentRepoPath))
                await RefreshGitStatusUIAsync();
        }

        private async void OnPrintClick(object sender, RoutedEventArgs e)
        {
            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId &&
                _editorSessions.TryGetValue(tabId, out var session) &&
                _tabBridges.TryGetValue(tabId, out var bridgeGroup) &&
                bridgeGroup.WebView.CoreWebView2 != null)
            {
                string fullText = session.GetText();
                string jsonText = System.Text.Json.JsonSerializer.Serialize(fullText);
                await bridgeGroup.WebView.CoreWebView2.ExecuteScriptAsync(
                    $"printDocument({jsonText})");
            }
        }

        private async void OnStatusLineNumberClick(object sender, RoutedEventArgs e)
        {
            var activeTab = GetActiveTab();
            if (activeTab == null) return;
            var lineBox = new TextBox { PlaceholderText = "이동할 줄 번호 입력...", Width = 200 };
            int currentLine = int.TryParse(StatusLine.Text, out int line) ? line : 1;
            lineBox.Text = currentLine.ToString();
            var dialog = new ContentDialog
            {
                Title = "줄 이동 (Go to Line)",
                Content = lineBox,
                PrimaryButtonText = "이동",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && int.TryParse(lineBox.Text, out int targetLine) && targetLine > 0)
            {
                if (_tabBridges.TryGetValue(activeTab.Id, out var bridgeGroup))
                {
                    if (bridgeGroup.Bridge != null)
                        await bridgeGroup.Bridge.RevealLineAsync(targetLine, 0, 0, "");
                    else if (bridgeGroup.WebView?.CoreWebView2 != null)
                    {
                        var msg = new { action = "revealLine", lineNumber = targetLine, indexOfMatch = 0, matchLength = 0, query = "" };
                        bridgeGroup.WebView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(msg));
                    }
                }
            }
        }

        private void OnStatusLineEndingClick(object sender, RoutedEventArgs e)
        {
            var flyout = new MenuFlyout();
            var lfItem = new MenuFlyoutItem { Text = "LF" };
            var crlfItem = new MenuFlyoutItem { Text = "CRLF" };
            lfItem.Click += (s, args) => { _currentLineEnding = "LF"; StatusBarPane.LineEndingText.Text = "LF"; };
            crlfItem.Click += (s, args) => { _currentLineEnding = "CRLF"; StatusBarPane.LineEndingText.Text = "CRLF"; };
            flyout.Items.Add(lfItem);
            flyout.Items.Add(crlfItem);
            if (sender is Button btn)
                flyout.ShowAt(btn, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Top });
        }

#pragma warning disable CS0414
        private static string? _currentLineEnding = "LF";
#pragma warning restore CS0414

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
                    ShowLeftSidebarPage(3);
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
                else if (e.Key == Windows.System.VirtualKey.F)
                {
                    e.Handled = true;
                    OnFindClick(this, new RoutedEventArgs());
                }
                else if (e.Key == Windows.System.VirtualKey.P)
                {
                    e.Handled = true;
                    OnPrintClick(this, new RoutedEventArgs());
                }
                else if (TerminalShortcutService.IsTerminalToggleKey(e))
                {
                    e.Handled = true;
                    _terminalShortcutService.RequestToggle();
                }
            }
        }

        #endregion
    }

}
