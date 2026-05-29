using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
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
        private const string PreviewResourceHostName = "ueditor.local";
        private const string PreviewDocumentHostName = "ueditor-doc.local";

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
        private readonly FunctionKeyShortcutService _functionKeyShortcutService;
        private readonly ExplorerDirectoryService _explorerDirectoryService;
        private readonly CompareSelectionDialogService _compareSelectionDialogService;
        private readonly SearchReplaceController _searchReplaceController;
        private readonly GitPanelController _gitPanelController;
        private readonly FavoritesRecentController _favoritesRecentController;
        private readonly SnippetsController _snippetsController;
        private readonly LlmAssistantController _llmAssistantController;
        private readonly TocController _tocController;
        private readonly MainWindowViewModel _viewModel = new MainWindowViewModel();
        private string _currentFolderPath = string.Empty;
        private string _currentRepoPath = string.Empty;
        private bool _isSyncingEncodingCombo = false;

        private string CurrentRepoPath
        {
            get => _currentRepoPath;
            set
            {
                if (_currentRepoPath != value)
                {
                    _currentRepoPath = value;
                    UpdateAutoSaveStatus();
                }
            }
        }

        private void UpdateAutoSaveStatus()
        {
            var settings = _settingsService?.CurrentSettings;
            if (settings == null) return;

            _autoSaveEnabled = settings.AutoSave && !string.IsNullOrEmpty(_currentRepoPath);
            if (_autoSaveTimer != null)
            {
                if (_autoSaveEnabled) _autoSaveTimer.Start();
                else _autoSaveTimer.Stop();
            }
        }

        private bool _isStickyNoteMode = false;
        private bool _wasLeftSidebarVisible = false;
        private bool _wasRightSidebarVisible = false;
        private bool _scrollSyncEnabled = true;
        private bool _wasMarkdownToolbarVisible = false;
        
        // Dynamic tabs collection
        private readonly Dictionary<string, (WebView2 WebView, MonacoBridge Bridge)> _tabBridges = 
            new Dictionary<string, (WebView2 WebView, MonacoBridge Bridge)>();
        private readonly Dictionary<string, EditorDocumentSession> _editorSessions =
            new Dictionary<string, EditorDocumentSession>();
        private readonly Dictionary<string, PendingSplitImeSyncState> _pendingSplitImeSyncStates =
            new Dictionary<string, PendingSplitImeSyncState>();
        private const int SplitImeDeferredUiSyncDelayMs = 260;

        private sealed class PendingSplitImeSyncState
        {
            public Dictionary<int, string> Lines { get; } = new Dictionary<int, string>();
            public DispatcherTimer? DeferredSyncTimer { get; set; }
            public bool IsColumnEdit { get; set; }
        }

        // Timer for debouncing live preview renders
        private readonly DispatcherTimer _previewDebounceTimer;
        private OpenedTab? _activeTabForPreview = null;
        private string _mappedPreviewDocumentDirectory = string.Empty;
        private const int InitialEditorLineWarmupCount = 120;
        private const int InitialPreviewLineWarmupCount = 120;

        // Autosave timer
        private readonly DispatcherTimer _autoSaveTimer;
        private bool _autoSaveEnabled = false;

        private readonly DispatcherTimer _gitAutoRefreshTimer;

        private ToggleButton LeftPanelToggle => StatusBarPane.LeftPanelToggleButton;
        private ToggleButton RightPanelToggle => StatusBarPane.RightPanelToggleButton;
        private TextBlock StatusLine => StatusBarPane.LineText;
        private TextBlock StatusCol => StatusBarPane.ColumnText;
        private TextBlock StatusTotalLines => StatusBarPane.TotalLinesText;
        private TextBlock StatusSelectionStats => StatusBarPane.StatusSelectionStatsText;
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
            _functionKeyShortcutService = new FunctionKeyShortcutService(WindowNative.GetWindowHandle(this));
            _functionKeyShortcutService.TopMostRequested += (_, _) => ToggleTopMostShortcut();
            _functionKeyShortcutService.ThemeRequested += (_, _) => OnToggleThemeClick(this, new RoutedEventArgs());
            _functionKeyShortcutService.StickyNoteRequested += (_, _) => OnStickyNoteClick(this, new RoutedEventArgs());
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
            _searchReplaceController.FileModified += OnSearchReplaceFileModifiedAsync;
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
            _gitPanelController.FileRestored += OnGitFileRestored;
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
                SyncSnippetsToOpenEditorsAsync,
                ShowErrorMessage,
                GetLocalizedString,
                InitializePickerWindow);
            _llmAssistantController = new LlmAssistantController(
                _llmService,
                _settingsService,
                _languageDetectionService,
                PreviewGrid,
                () => this.Content.XamlRoot,
                GetActiveTab,
                GetTabTextForLlmContext,
                InsertTextIntoActiveEditorAsync,
                ShowErrorMessage,
                GetLocalizedString);
            _tocController = new TocController(
                _viewModel,
                LeftSidebarTabView,
                GetActiveTab,
                tab => _editorSessions.TryGetValue(tab.Id, out var s) ? s : null,
                () => PreviewModeCombo.SelectedIndex == 3,
                async targetLine =>
                {
                    var activeTab = GetActiveTab();
                    if (activeTab != null && _tabBridges.TryGetValue(activeTab.Id, out var bridgeGroup))
                    {
                        if (bridgeGroup.Bridge != null)
                            await bridgeGroup.Bridge.RevealLineAsync(targetLine, 0, 0, "");
                        else if (bridgeGroup.WebView?.CoreWebView2 != null)
                        {
                            var msg = new { action = "revealLine", lineNumber = targetLine, indexOfMatch = 0, matchLength = 0, query = "" };
                            bridgeGroup.WebView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(msg));
                        }
                    }
                });

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
            _terminalShortcutService.Start();
            _functionKeyShortcutService.Start();
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
            LeftSidebarTabView.ReplaceOneClick += OnReplaceOneClick;
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
            try
            {
                _terminalShortcutService.Stop();
                _functionKeyShortcutService.Stop();
            }
            catch { }

            try
            {
                _previewDebounceTimer.Stop();
                _autoSaveTimer.Stop();
                _gitAutoRefreshTimer.Stop();
                foreach (var tabId in _pendingSplitImeSyncStates.Keys.ToList())
                {
                    ClearPendingSplitImeSync(tabId);
                }
            }
            catch { }

            try
            {
                EditorWorkspace.StopAllTerminalSessions();
            }
            catch { }

            foreach (var bridge in _tabBridges.Values)
            {
                try { bridge.WebView.Close(); }
                catch { }
            }
            _tabBridges.Clear();

            try { PreviewWebView.Close(); }
            catch { }

            try
            {
                if (Application.Current is App app)
                {
                    app.CleanupAppResources();
                }
                else
                {
                    Environment.Exit(0);
                }
            }
            catch
            {
                Environment.Exit(0);
            }
        }

        private void CleanupBeforeRestart()
        {
            _terminalShortcutService.Stop();
            _functionKeyShortcutService.Stop();

            _previewDebounceTimer.Stop();
            _autoSaveTimer.Stop();
            _gitAutoRefreshTimer.Stop();
            foreach (var tabId in _pendingSplitImeSyncStates.Keys.ToList())
            {
                ClearPendingSplitImeSync(tabId);
            }

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
                _functionKeyShortcutService.Stop();
            }
            else
            {
                _terminalShortcutService.Start();
                _functionKeyShortcutService.Start();
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

            // Load Snippets, Favorites and Recent Files FIRST so opening files can safely update them
            await _snippetsController.LoadAsync();
            _favoritesRecentController.RefreshFavorites();
            _favoritesRecentController.LoadRecentFiles();

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

            UpdateAutoSaveStatus();

            await InitializePreviewWebViewAsync();
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

                var coreWebView = PreviewWebView.CoreWebView2;
                if (coreWebView == null)
                {
                    throw new InvalidOperationException("CoreWebView2 failed to initialize.");
                }

                try
                {
                    bool isDark = string.Equals(_settingsService.CurrentSettings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
                    if (coreWebView.Profile != null)
                    {
                        coreWebView.Profile.PreferredColorScheme = isDark
                            ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
                            : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;
                    }
                }
                catch { }
                
                // Configure Virtual Host Mapping to access local files under WebResources folder via simulated URL http://ueditor.local/
                string webResourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebResources");
                
                coreWebView.SetVirtualHostNameToFolderMapping(
                    PreviewResourceHostName, 
                    webResourcesPath, 
                    CoreWebView2HostResourceAccessKind.Allow
                );
                coreWebView.AddWebResourceRequestedFilter(
                    $"http://{PreviewDocumentHostName}/*",
                    CoreWebView2WebResourceContext.All);

                coreWebView.Settings.IsWebMessageEnabled = true;
                coreWebView.Settings.IsScriptEnabled = true;
                coreWebView.Settings.AreDefaultContextMenusEnabled = false;
                coreWebView.Settings.AreDevToolsEnabled = false;
                PreviewWebView.WebMessageReceived += OnPreviewWebMessageReceived;
                coreWebView.WebResourceRequested += OnPreviewDocumentResourceRequested;

                PreviewWebView.NavigationCompleted += (s, e) =>
                {
                    var tab = GetActiveTab();
                    if (tab != null)
                    {
                        UpdateLivePreview(tab);
                    }
                };

                // Load preview renderer page. Version query avoids stale WebView2 virtual-host cache.
                PreviewWebView.Source = new Uri($"http://{PreviewResourceHostName}/preview.html?v={GetWebResourceVersion("preview.html")}");
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
                string type = typeProp.GetString() ?? string.Empty;
                if (string.Equals(type, "shortcut", StringComparison.Ordinal))
                {
                    if (root.TryGetProperty("name", out var nameProp))
                    {
                        string name = nameProp.GetString() ?? string.Empty;
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (string.Equals(name, "find", StringComparison.Ordinal))
                                OnFindClick(null!, null!);
                            else if (string.Equals(name, "f9", StringComparison.Ordinal))
                                ToggleTopMostShortcut();
                            else if (string.Equals(name, "f10", StringComparison.Ordinal))
                                OnToggleThemeClick(this, new RoutedEventArgs());
                            else if (string.Equals(name, "f12", StringComparison.Ordinal))
                                OnStickyNoteClick(this, new RoutedEventArgs());
                        });
                    }
                    return;
                }

                if (string.Equals(type, "previewScroll", StringComparison.Ordinal))
                {
                    if (!_scrollSyncEnabled)
                    {
                        return;
                    }

                    int firstLine = root.TryGetProperty("firstLine", out var firstLineProp) ? firstLineProp.GetInt32() : 1;
                    double offset = root.TryGetProperty("offset", out var offsetProp) ? offsetProp.GetDouble() : 0;
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        var activeTab = GetActiveTab();
                        if (activeTab != null && _tabBridges.TryGetValue(activeTab.Id, out var bridgeGroup))
                        {
                            if (bridgeGroup.Bridge != null)
                            {
                                _ = bridgeGroup.Bridge.SyncScrollFromPreviewAsync(firstLine, offset);
                            }
                        }
                    });
                    return;
                }

                if (!string.Equals(type, "previewRequestLines", StringComparison.Ordinal))
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

        private OpenedTab OpenNewTab(
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
                tab.Title = GetLocalizedString("UntitledNewTab", "제목 없음");
                tab.Content = "";
            }

            var documentModel = textModel ?? LineArrayTextModel.FromText(content);
            var session = new EditorDocumentSession(tab, documentModel);
            _editorSessions[tab.Id] = session;

            _viewModel.Tabs.Add(tab);

            // Determine editor background color based on custom settings or theme
            Windows.UI.Color editorBgColor;
            var settings = _settingsService.CurrentSettings;
            if (!string.IsNullOrEmpty(settings.CustomBackgroundColor) && TryParseHexColor(settings.CustomBackgroundColor, out var parsedBg))
            {
                editorBgColor = parsedBg;
            }
            else
            {
                bool isLight = string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
                editorBgColor = isLight ? Windows.UI.Color.FromArgb(255, 255, 255, 255) : Windows.UI.Color.FromArgb(255, 30, 30, 30);
            }

            // Create host layout grid for standard WebView2 editor
            var grid = new Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(editorBgColor)
            };
            var editorWebView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0), // HTML 로드 전 부모 Grid 배경색이 자연스럽게 보이도록 투명화
                Opacity = 1 // 처음부터 백그라운드 렌더링이 지연 없이 가동되도록 1로 설정
            };
            grid.Children.Add(editorWebView);

            // Instantiate TabViewItem XAML element
            // Build tab header with dirty indicator as a red prefix dot
            var dirtyIndicator = new TextBlock
            {
                Text = "●",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 60, 60)),
                FontSize = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 4, 0),
                Visibility = Visibility.Collapsed
            };
            var titleText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            titleText.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath("Title"),
                Mode = BindingMode.OneWay,
                Source = tab
            });
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerPanel.Children.Add(dirtyIndicator);
            headerPanel.Children.Add(titleText);
            // Track dirty state changes to update the indicator visibility
            tab.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(OpenedTab.IsDirty))
                {
                    dirtyIndicator.Visibility = tab.IsDirty ? Visibility.Visible : Visibility.Collapsed;
                }
            };
            var tabItem = new TabViewItem
            {
                Content = grid,
                Tag = tab.Id,
                Header = headerPanel
            };
            var targetTabView = GetCurrentActiveTabView();

            // Tab right-click context menu
            var tabContextMenu = new MenuFlyout();
            var bookmarkItem = new MenuFlyoutItem { Text = GetLocalizedString("TabMenuAddBookmark", "북마크 추가") };
            bookmarkItem.Click += (s, args) => OnTabAddBookmark(tab);
            tabContextMenu.Items.Add(bookmarkItem);

            var openFolderItem = new MenuFlyoutItem { Text = GetLocalizedString("TabMenuOpenFolder", "해당 폴더로 이동") };
            openFolderItem.Click += (s, args) => OnTabOpenFolder(tab);
            tabContextMenu.Items.Add(openFolderItem);

            tabContextMenu.Items.Add(new MenuFlyoutSeparator());

            var closeRightItem = new MenuFlyoutItem { Text = GetLocalizedString("TabMenuCloseRight", "오른쪽 탭 닫기") };
            closeRightItem.Click += (s, args) => OnCloseRightTabs(tab, tabItem, targetTabView);
            tabContextMenu.Items.Add(closeRightItem);

            var closeLeftItem = new MenuFlyoutItem { Text = GetLocalizedString("TabMenuCloseLeft", "왼쪽 탭 닫기") };
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

            var bridge = new MonacoBridge(editorWebView, _localizationService);
            _tabBridges[tab.Id] = (editorWebView, bridge);

            WireEditorBridge(bridge, editorWebView, tab, tabItem, session, isReadOnly);

            // Initialize editor inside WebView2 using virtual host mappings
            InitializeEditorWebView(editorWebView, bridge);

            targetTabView.TabItems.Add(tabItem);
            targetTabView.SelectedItem = tabItem;

            UpdateStatusFileStats(tab);
            UpdateTotalLines(tab);
            UpdateStatusSelectionStats(null);
            SyncEncodingCombo(tab);
            SyncLineEndingText(tab);
            UpdateWindowTitle();
            return tab;
        }

        private void WireEditorBridge(
            MonacoBridge bridge,
            WebView2 editorWebView,
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
                        case "f9":
                            ToggleTopMostShortcut();
                            break;
                        case "f10":
                            OnToggleThemeClick(this, new RoutedEventArgs());
                            break;
                        case "f12":
                            OnStickyNoteClick(this, new RoutedEventArgs());
                            break;
                        case "toggleLeftPanel":
                            _ = ToggleLeftPanelAsync();
                            break;
                        case "toggleRightPanel":
                            _ = ToggleRightPanelAsync();
                            break;
                        case "newTab":
                            OpenNewTab();
                            break;
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
                                    PropagateDirtyStateToOtherTabs(tab);
                                    SchedulePreview(tab);
                                    _ = bridge.SetTextAsync(text);
                                    _ = SyncEditsToOtherTabsAsync(tab);
                                }
                            }
                            break;
                        case "redo":
                            {
                                var text = session.Redo();
                                if (text != null)
                                {
                                    MarkTabDirty(tab, tabItem);
                                    PropagateDirtyStateToOtherTabs(tab);
                                    SchedulePreview(tab);
                                    _ = bridge.SetTextAsync(text);
                                    _ = SyncEditsToOtherTabsAsync(tab);
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
                    isReadOnly,
                    session.GetLines(1, InitialEditorLineWarmupCount));
                await bridge.UpdateSnippetsAsync(_snippetService.GetSnippets());
                await bridge.UpdateScrollSyncStateAsync(_scrollSyncEnabled);

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    if (GetActiveTab() == tab)
                    {
                        editorWebView.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                        _ = bridge.FocusAsync();
                    }
                });
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

            bridge.LineChanged += async (lineNumber, text, isComposing) =>
            {
                session.ReplaceLine(lineNumber, text);

                if (!isComposing)
                {
                    MarkTabDirty(tab, tabItem);
                    PropagateDirtyStateToOtherTabs(tab);
                }

                SchedulePreview(tab);

                // Split 상태의 같은 파일 IME 조합 중에는 반대편 pane에 live patch를 보내지 않는다.
                // 특히 컬럼 입력은 첫 줄과 나머지 줄 이벤트 간격이 일정하지 않아 짧은 타이머로는
                // 컬럼 여부를 안전하게 판정할 수 없다. 따라서 조합 중에는 현재 pane의 session만 갱신하고,
                // composition 완료(isComposing=false) 후 보류된 줄을 한 번에 동기화한다.
                // split이 아니거나 같은 파일 반대편 탭이 없는 경우, 그리고 IME 조합 중이 아닌 일반 입력은 기존 경로를 유지한다.
                if (isComposing && QueuePendingSplitImeLineSyncIfNeeded(tab, lineNumber, text))
                {
                    return;
                }

                if (!isComposing && SchedulePendingSplitImeCompletionSyncIfNeeded(tab, lineNumber, text))
                {
                    return;
                }

                await SyncLineChangeToOtherTabsAsync(tab, lineNumber, text, isComposing);
            };

            bridge.LineInsertRequested += async (lineNumber, text) =>
            {
                int lineCount = session.InsertLine(lineNumber, text);
                MarkTabDirty(tab, tabItem);
                PropagateDirtyStateToOtherTabs(tab);
                await bridge.UpdateLineCountAsync(lineCount);
                SchedulePreview(tab);
                await SyncEditsToOtherTabsAsync(tab);
                UpdateTotalLines(tab);
            };

            bridge.LineSplitRequested += async (lineNumber, before, after) =>
            {
                int lineCount = session.SplitLine(lineNumber, before, after);
                MarkTabDirty(tab, tabItem);
                PropagateDirtyStateToOtherTabs(tab);
                await bridge.UpdateLineCountAsync(lineCount);
                SchedulePreview(tab);
                await SyncEditsToOtherTabsAsync(tab);
                UpdateTotalLines(tab);
            };

            bridge.MergeLineWithPreviousRequested += async (lineNumber) =>
            {
                int lineCount = session.MergeLineWithPrevious(lineNumber);
                MarkTabDirty(tab, tabItem);
                PropagateDirtyStateToOtherTabs(tab);
                await bridge.UpdateLineCountAsync(lineCount);
                SchedulePreview(tab);
                await SyncEditsToOtherTabsAsync(tab);
                UpdateTotalLines(tab);
            };

            bridge.DeleteLineRequested += async (lineNumber) =>
            {
                int lineCount = session.DeleteLine(lineNumber);
                MarkTabDirty(tab, tabItem);
                PropagateDirtyStateToOtherTabs(tab);
                await bridge.UpdateLineCountAsync(lineCount);
                SchedulePreview(tab);
                await SyncEditsToOtherTabsAsync(tab);
                UpdateTotalLines(tab);
            };

            bridge.FindRequested += async (query, startLine, startColumn, reverse, matchCase, isRegex) =>
            {
                var result = session.Find(query, startLine, startColumn, reverse, matchCase, isRegex);
                await bridge.SendFindResultAsync(result, query);
            };

            bridge.FindAllRequested += async (query, matchCase, isRegex) =>
            {
                var results = session.FindAll(query, matchCase, isRegex);
                await bridge.SendFindAllResultsAsync(results, query);
            };

            bridge.ReplaceAllRequested += async (query, replace, matchCase, isRegex) =>
            {
                session.ReplaceAll(query, replace, matchCase, isRegex);
                string updatedText = session.GetText();
                await bridge.SetTextAsync(updatedText, shouldFocus: false);
                await SyncEditsToOtherTabsAsync(tab);
                await bridge.SendFindAllResultsAsync(session.FindAll(query, matchCase, isRegex), query);

                MarkTabDirty(tab, tabItem);
                PropagateDirtyStateToOtherTabs(tab);
                SchedulePreview(tab);
                UpdateTotalLines(tab);
            };

            bridge.ContentChanged += async (isComposing) =>
            {
                if (!isComposing)
                {
                    MarkTabDirty(tab, tabItem);
                    PropagateDirtyStateToOtherTabs(tab);
                    UpdateLanguageUI(tab);
                    _tocController?.RefreshToc(tab);
                    UpdateTotalLines(tab);
                    ScheduleDeferredPendingSplitImeSyncIfNeeded(tab);
                }

                SchedulePreview(tab);
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
                    UpdateStatusSelectionStats(selectedText);
                }
            };

            bridge.ScrollChanged += (firstLine, offset) =>
            {
                if (!_scrollSyncEnabled)
                {
                    return;
                }

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    if (GetActiveTab() == tab)
                    {
                        try
                        {
                            if (PreviewWebView.CoreWebView2 != null)
                            {
                                var syncMsg = new
                                {
                                    action = "syncScroll",
                                    firstLine = firstLine,
                                    offset = offset
                                };
                                PreviewWebView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(syncMsg));
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to sync scroll to preview: {ex.Message}");
                        }
                    }
                });
            };

            bridge.ScrollSyncChanged += (enabled) =>
            {
                this.DispatcherQueue.TryEnqueue(async () =>
                {
                    _scrollSyncEnabled = enabled;

                    // Synchronize all open editor tabs to this state
                    foreach (var grp in _tabBridges.Values)
                    {
                        if (grp.Bridge != null)
                        {
                            await grp.Bridge.UpdateScrollSyncStateAsync(enabled);
                        }
                    }

                    // Synchronize preview tab
                    try
                    {
                        if (PreviewWebView.CoreWebView2 != null)
                        {
                            var syncMsg = new
                            {
                                action = "scrollSyncChanged",
                                enabled = enabled
                            };
                            PreviewWebView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(syncMsg));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to sync scroll sync state to preview: {ex.Message}");
                    }
                });
            };
        }

        private void MarkTabDirty(OpenedTab tab, TabViewItem? tabItem = null)
        {
            SetDirtyStateForFileGroup(tab, true);
        }

        private List<OpenedTab> GetTabsForSameFile(OpenedTab sourceTab)
        {
            string? pathKey = NormalizeTabPath(sourceTab.FilePath);
            if (pathKey == null)
            {
                return new List<OpenedTab> { sourceTab };
            }

            var tabs = _viewModel.Tabs
                .Where(tab =>
                {
                    string? otherPathKey = NormalizeTabPath(tab.FilePath);
                    return otherPathKey != null &&
                           string.Equals(otherPathKey, pathKey, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            if (!tabs.Any(tab => tab.Id == sourceTab.Id))
            {
                tabs.Add(sourceTab);
            }

            return tabs;
        }

        private bool IsAnySameFileTabDirty(OpenedTab sourceTab)
        {
            return GetTabsForSameFile(sourceTab).Any(tab => tab.IsDirty);
        }

        private void SetDirtyStateForFileGroup(OpenedTab sourceTab, bool isDirty)
        {
            bool changed = false;
            foreach (var tab in GetTabsForSameFile(sourceTab))
            {
                if (tab.IsDirty != isDirty)
                {
                    tab.IsDirty = isDirty;
                    changed = true;
                }
            }

            if (changed)
            {
                UpdateWindowTitle();
            }
        }

        private void PropagateDirtyStateToOtherTabs(OpenedTab sourceTab)
        {
            SetDirtyStateForFileGroup(sourceTab, true);
        }

        private void CleanDirtyStateOnOtherTabs(OpenedTab sourceTab)
        {
            SetDirtyStateForFileGroup(sourceTab, false);
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

                var coreWebView = wv.CoreWebView2;
                if (coreWebView == null)
                {
                    throw new InvalidOperationException("CoreWebView2 failed to initialize.");
                }

                try
                {
                    bool isDark = string.Equals(_settingsService.CurrentSettings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
                    if (coreWebView.Profile != null)
                    {
                        coreWebView.Profile.PreferredColorScheme = isDark
                            ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
                            : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;
                    }
                }
                catch { }
                
                string webResourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebResources");
                coreWebView.SetVirtualHostNameToFolderMapping(
                    "ueditor.local", 
                    webResourcesPath, 
                    CoreWebView2HostResourceAccessKind.Allow
                );

                var settings = _settingsService.CurrentSettings;
                var url = $"http://ueditor.local/editor.html?v={GetWebResourceVersion("editor.html")}" +
                    $"&theme={Uri.EscapeDataString(settings.Theme)}" +
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

        private static string GetWebResourceVersion(string fileName)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebResources", fileName);
                return File.Exists(path)
                    ? File.GetLastWriteTimeUtc(path).Ticks.ToString()
                    : DateTime.UtcNow.Ticks.ToString();
            }
            catch
            {
                return DateTime.UtcNow.Ticks.ToString();
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
                if (string.Equals(tab.Language, "html", StringComparison.OrdinalIgnoreCase))
                {
                    mode = "html";
                }

                if (string.Equals(mode, "html", StringComparison.Ordinal))
                {
                    string previewText = _editorSessions.TryGetValue(tab.Id, out var htmlSession)
                        ? htmlSession.GetText()
                        : tab.Content ?? string.Empty;
                    var htmlMsg = new
                    {
                        action = "renderHtmlPreview",
                        text = previewText,
                        baseHref = GetPreviewBaseHref(tab),
                        scrollSyncEnabled = _scrollSyncEnabled
                    };

                    string htmlJson = System.Text.Json.JsonSerializer.Serialize(htmlMsg);
                    PreviewWebView.CoreWebView2.PostWebMessageAsJson(htmlJson);
                    return;
                }

                _editorSessions.TryGetValue(tab.Id, out var previewSession);
                var renderMsg = new
                {
                    action = "initVirtualPreview",
                    lineCount = previewSession?.Model.LineCount ?? 1,
                    initialStartLine = 1,
                    initialLines = previewSession?.GetLines(1, InitialPreviewLineWarmupCount) ?? Array.Empty<string>(),
                    mode = mode,
                    baseHref = GetPreviewBaseHref(tab),
                    wordWrap = _settingsService.CurrentSettings.WordWrap,
                    theme = _settingsService.CurrentSettings.Theme,
                    customBackgroundColor = _settingsService.CurrentSettings.CustomBackgroundColor,
                    customForegroundColor = _settingsService.CurrentSettings.CustomForegroundColor,
                    uiFontFamily = _settingsService.CurrentSettings.UiFontFamily,
                    previewFontFamily = _settingsService.CurrentSettings.PreviewFontFamily,
                    previewFontSize = _settingsService.CurrentSettings.PreviewFontSize,
                    previewCustomBackgroundColor = _settingsService.CurrentSettings.PreviewCustomBackgroundColor,
                    previewCustomForegroundColor = _settingsService.CurrentSettings.PreviewCustomForegroundColor,
                    scrollSyncEnabled = _scrollSyncEnabled
                };

                string json = System.Text.Json.JsonSerializer.Serialize(renderMsg);
                PreviewWebView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed sending live preview rendering data: {ex.Message}");
            }
        }

        private string GetPreviewBaseHref(OpenedTab tab)
        {
            try
            {
                string directory = string.Empty;
                if (!string.IsNullOrWhiteSpace(tab.FilePath))
                {
                    directory = Path.GetDirectoryName(tab.FilePath) ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(_currentFolderPath))
                {
                    directory = _currentFolderPath;
                }

                if (string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(_currentRepoPath))
                {
                    directory = _currentRepoPath;
                }

                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    return string.Empty;
                }

                ConfigurePreviewDocumentFolderMapping(directory);
                return $"http://{PreviewDocumentHostName}/";
            }
            catch
            {
                return string.Empty;
            }
        }

        private void ConfigurePreviewDocumentFolderMapping(string directory)
        {
            try
            {
                if (PreviewWebView.CoreWebView2 == null || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    return;
                }

                string normalizedDirectory = Path.GetFullPath(directory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(_mappedPreviewDocumentDirectory, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    PreviewDocumentHostName,
                    normalizedDirectory,
                    CoreWebView2HostResourceAccessKind.Allow);
                _mappedPreviewDocumentDirectory = normalizedDirectory;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to map preview document folder: {ex.Message}");
            }
        }

        private void OnPreviewDocumentResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_mappedPreviewDocumentDirectory) ||
                    !Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var requestUri) ||
                    !string.Equals(requestUri.Host, PreviewDocumentHostName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string relativePath = Uri.UnescapeDataString(requestUri.AbsolutePath.TrimStart('/'))
                    .Replace('/', Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    args.Response = sender.Environment.CreateWebResourceResponse(
                        CreateEmptyPreviewResourceStream(),
                        404,
                        "Not Found",
                        "Content-Type: text/plain");
                    return;
                }

                string root = Path.GetFullPath(_mappedPreviewDocumentDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string targetPath = Path.GetFullPath(Path.Combine(root, relativePath));
                if (!targetPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(targetPath))
                {
                    args.Response = sender.Environment.CreateWebResourceResponse(
                        CreateEmptyPreviewResourceStream(),
                        404,
                        "Not Found",
                        "Content-Type: text/plain");
                    return;
                }

                var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                string headers = $"Content-Type: {GetPreviewResourceContentType(targetPath)}\r\nCache-Control: no-store";
                args.Response = sender.Environment.CreateWebResourceResponse(stream.AsRandomAccessStream(), 200, "OK", headers);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed serving preview document resource: {ex.Message}");
                try
                {
                    args.Response = sender.Environment.CreateWebResourceResponse(
                        CreateEmptyPreviewResourceStream(),
                        500,
                        "Internal Server Error",
                        "Content-Type: text/plain");
                }
                catch { }
            }
        }

        private static Windows.Storage.Streams.IRandomAccessStream CreateEmptyPreviewResourceStream()
        {
            return new MemoryStream(Array.Empty<byte>()).AsRandomAccessStream();
        }

        private static string GetPreviewResourceContentType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".bmp" => "image/bmp",
                ".ico" => "image/x-icon",
                ".avif" => "image/avif",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".css" => "text/css; charset=utf-8",
                ".js" => "text/javascript; charset=utf-8",
                ".html" or ".htm" => "text/html; charset=utf-8",
                _ => "application/octet-stream"
            };
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
            picker.FileTypeFilter.Add(".reg");

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
                    CurrentRepoPath = repoRoot;
                    await RefreshGitStatusUIAsync();
                }

                // Check if file is already open in an existing tab
                var existingTab = _viewModel.Tabs.FirstOrDefault(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                if (existingTab != null)
                {
                    var tabItem = EditorTabView.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == existingTab.Id);
                    if (tabItem != null)
                    {
                        EditorTabView.SelectedItem = tabItem;
                    }
                    else
                    {
                        tabItem = EditorTabView2.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == existingTab.Id);
                        if (tabItem != null)
                        {
                            EditorTabView2.SelectedItem = tabItem;
                        }
                    }

                    if (tabItem != null)
                    {
                        if (_tabBridges.TryGetValue(existingTab.Id, out var bridgeGroup))
                        {
                            if (bridgeGroup.WebView != null)
                            {
                                bridgeGroup.WebView.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                            }
                            if (bridgeGroup.Bridge != null)
                            {
                                _ = bridgeGroup.Bridge.FocusAsync();
                            }
                        }
                    }

                    return;
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
            var activeTabView = GetCurrentActiveTabView();
            if (activeTabView.SelectedItem is TabViewItem activeTabItem &&
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
            var activeTabView = GetCurrentActiveTabView();
            if (activeTabView.SelectedItem is TabViewItem activeTabItem &&
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
            bool topMost = TopToolbar.TopMostIsChecked;
            _stickyNoteService.ApplyTopMost(this, topMost);
            if (StickyNoteTopMostButton != null)
            {
                StickyNoteTopMostButton.IsChecked = topMost;
            }
        }

        private void ToggleTopMostShortcut()
        {
            bool nextTopMost = !TopToolbar.TopMostIsChecked;
            TopToolbar.TopMostIsChecked = nextTopMost;
            _stickyNoteService.ApplyTopMost(this, nextTopMost);
            if (StickyNoteTopMostButton != null)
            {
                StickyNoteTopMostButton.IsChecked = nextTopMost;
            }
        }

        private void OnStickyNoteClick(object sender, RoutedEventArgs e)
        {
            if (_isStickyNoteMode)
            {
                ExitStickyNoteMode();
            }
            else
            {
                EnterStickyNoteMode();
            }
        }

        private void EnterStickyNoteMode()
        {
            if (_isStickyNoteMode) return;
            _isStickyNoteMode = true;

            // Save current states
            _wasLeftSidebarVisible = _shellPanelLayoutService.IsLeftSidebarVisible;
            _wasRightSidebarVisible = _shellPanelLayoutService.IsRightSidebarVisible;
            _wasMarkdownToolbarVisible = MarkdownToolbar.Visibility == Visibility.Visible;

            // Sync topmost button state
            StickyNoteTopMostButton.IsChecked = TopToolbar.TopMostIsChecked;

            // Hide normal Titlebar and show Sticky Note Header
            AppTitleBar.Visibility = Visibility.Collapsed;
            ExitStickyNoteBar.Visibility = Visibility.Visible;
            this.SetTitleBar(ExitStickyNoteBar);

            // Collapse normal toolbars and status bar
            TopToolbar.Visibility = Visibility.Collapsed;
            MarkdownToolbar.Visibility = Visibility.Collapsed;
            StatusBarPane.Visibility = Visibility.Collapsed;

            // Hide left and right sidebars via layout service
            _shellPanelLayoutService.ApplyLeftSidebarVisibility(false);
            _shellPanelLayoutService.ApplyPreviewVisibility(false);
        }

        private void ExitStickyNoteMode()
        {
            if (!_isStickyNoteMode) return;
            _isStickyNoteMode = false;

            // Sync standard topmost state in toolbar with sticker header topmost state
            bool topMost = StickyNoteTopMostButton.IsChecked == true;
            TopToolbar.TopMostIsChecked = topMost;
            _stickyNoteService.ApplyTopMost(this, topMost);

            // Hide Sticky Note Header and show normal Titlebar
            ExitStickyNoteBar.Visibility = Visibility.Collapsed;
            AppTitleBar.Visibility = Visibility.Visible;
            this.SetTitleBar(AppTitleBar);

            // Restore normal toolbars and status bar
            TopToolbar.Visibility = Visibility.Visible;
            MarkdownToolbar.Visibility = _wasMarkdownToolbarVisible ? Visibility.Visible : Visibility.Collapsed;
            StatusBarPane.Visibility = Visibility.Visible;

            // Restore sidebars to cached states
            LeftPanelToggle.IsChecked = _wasLeftSidebarVisible;
            ApplyLeftSidebarVisibility(_wasLeftSidebarVisible);
            ApplyPreviewVisibility(_wasRightSidebarVisible);
        }

        private void OnExitStickyNoteClick(object sender, RoutedEventArgs e)
        {
            ExitStickyNoteMode();
        }

        private void OnStickyNoteTopMostClick(object sender, RoutedEventArgs e)
        {
            bool topMost = StickyNoteTopMostButton.IsChecked == true;
            _stickyNoteService.ApplyTopMost(this, topMost);
            TopToolbar.TopMostIsChecked = topMost;
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

            if (safeIndex == 6)
            {
                _tocController?.RefreshToc(GetActiveTab());
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

        private async Task ToggleLeftPanelAsync()
        {
            bool show = LeftPanelToggle.IsChecked != true;
            LeftPanelToggle.IsChecked = show;
            ApplyLeftSidebarVisibility(show);
            await SaveSidebarVisibilitySettingsAsync();
        }

        private async Task ToggleRightPanelAsync()
        {
            bool show = RightPanelToggle.IsChecked != true;
            RightPanelToggle.IsChecked = show;
            ApplyPreviewVisibility(show);
            await SaveSidebarVisibilitySettingsAsync();
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

            try
            {
                bool isDark = string.Equals(settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
                var scheme = isDark
                    ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
                    : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;

                if (PreviewWebView.CoreWebView2 != null && PreviewWebView.CoreWebView2.Profile != null)
                {
                    PreviewWebView.CoreWebView2.Profile.PreferredColorScheme = scheme;
                }

                foreach (var grp in _tabBridges.Values)
                {
                    if (grp.WebView?.CoreWebView2 != null && grp.WebView.CoreWebView2.Profile != null)
                    {
                        grp.WebView.CoreWebView2.Profile.PreferredColorScheme = scheme;
                    }
                }
            }
            catch { }

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
                TerminalPane.Localize(GetString);
                PreviewGrid.Localize(GetString);
                PreviewGrid.UpdateTranslateLanguage(_settingsService.CurrentSettings?.LlmTargetLanguage ?? "Korean");
                MarkdownToolbar.LocalizeTooltips(GetString);

                StickyNoteTitleText.Text = GetString("StickyNoteTitle", "스티커 노트");
                ToolTipService.SetToolTip(StickyNoteTopMostButton, GetString("TopMost", "항상위"));
                StickyNoteTopMostText.Text = GetString("TopMost", "항상위");
                ToolTipService.SetToolTip(ExitStickyNoteButton, GetString("ExitStickyNoteTooltip", "스티커 노트 모드 종료"));
                ExitStickyNoteText.Text = GetString("ExitStickyNoteText", "나가기");

                var activeTab = GetActiveTab();
                if (activeTab != null)
                {
                    UpdateTotalLines(activeTab);
                }
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
            UpdateAutoSaveStatus();
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
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = GetCurrentElementTheme()
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

        private async Task SyncSnippetsToOpenEditorsAsync()
        {
            var snippets = _snippetService.GetSnippets();
            foreach (var grp in _tabBridges.Values)
            {
                if (grp.Bridge != null)
                {
                    await grp.Bridge.UpdateSnippetsAsync(snippets);
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
                CurrentRepoPath = _gitService.FindRepositoryRoot(folder.Path) ?? string.Empty;
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
            CurrentRepoPath = _gitService.FindRepositoryRoot(folderPath) ?? string.Empty;
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
                    UpdateTotalLines(activeTab);
                    UpdateStatusSelectionStats(null);
                    UpdateLivePreview(activeTab);
                    UpdateLanguageUI(activeTab);
                    SyncEncodingCombo(activeTab);
                    SyncLineEndingText(activeTab);
                }
                UpdateWindowTitle();
            }
        }

        private TabView GetCurrentActiveTabView()
        {
            return EditorWorkspace.GetCurrentActiveTabView();
        }

        private void OpenSplitNewTab(OpenedTab? sourceTab = null)
        {
            // Capture the source tab before EditorWorkspace changes focus/active pane.
            // Otherwise split can duplicate the wrong pane and can also leave dirty state out of sync.
            OpenedTab? activeTab = sourceTab ?? GetActiveTab();

            if (activeTab != null)
            {
                string? path = activeTab.FilePath;
                string content = "";
                if (_editorSessions.TryGetValue(activeTab.Id, out var session))
                {
                    content = session.GetText();
                }
                else
                {
                    content = activeTab.Content ?? "";
                }

                bool isDirty = IsAnySameFileTabDirty(activeTab);

                var newTab = OpenNewTab(
                    filePath: path,
                    content: content,
                    isReadOnly: false,
                    encodingName: activeTab.EncodingName,
                    encodingWasAutoDetected: activeTab.EncodingWasAutoDetected
                );

                newTab.IsDirty = isDirty;
                SetDirtyStateForFileGroup(activeTab, isDirty);
            }
            else
            {
                OpenNewTab();
            }
        }

        private async void OnSplitNoneClick(object sender, RoutedEventArgs e)
        {
            var preferredTab = GetActiveTab();
            if (preferredTab != null)
            {
                await SyncEditsToOtherTabsAsync(preferredTab);
            }

            EditorWorkspace.SetSplitMode(EditorSplitMode.None, () => OpenNewTab());
            MergeDuplicateFileTabsAfterUnsplit(preferredTab?.Id);
        }
        private void OnSplitVerticalClick(object sender, RoutedEventArgs e)
        {
            var sourceTab = GetActiveTab();
            EditorWorkspace.SetSplitMode(EditorSplitMode.Vertical, () => OpenSplitNewTab(sourceTab));
        }

        private void OnSplitHorizontalClick(object sender, RoutedEventArgs e)
        {
            var sourceTab = GetActiveTab();
            EditorWorkspace.SetSplitMode(EditorSplitMode.Horizontal, () => OpenSplitNewTab(sourceTab));
        }

        private void MergeDuplicateFileTabsAfterUnsplit(string? preferredTabId)
        {
            var tabItems = EditorTabView.TabItems
                .OfType<TabViewItem>()
                .Where(item => item.Tag is string)
                .ToList();

            var groups = tabItems
                .Select(item =>
                {
                    string tabId = (string)item.Tag!;
                    var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
                    return new { Item = item, Tab = tab, PathKey = NormalizeTabPath(tab?.FilePath) };
                })
                .Where(entry => entry.Tab != null && entry.PathKey != null)
                .GroupBy(entry => entry.PathKey!, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .ToList();

            foreach (var group in groups)
            {
                var keeper = group.FirstOrDefault(entry => entry.Tab!.Id == preferredTabId) ?? group.First();
                foreach (var duplicate in group.Where(entry => entry.Tab!.Id != keeper.Tab!.Id).ToList())
                {
                    MergeDuplicateTabState(keeper.Tab!, duplicate.Tab!);
                    CloseTabAndCleanup(duplicate.Tab!, duplicate.Item);
                }

                EditorTabView.SelectedItem = keeper.Item;
            }

            UpdateWindowTitle();
        }

        private static string? NormalizeTabPath(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;

            try
            {
                return Path.GetFullPath(filePath);
            }
            catch
            {
                return filePath;
            }
        }

        private void MergeDuplicateTabState(OpenedTab keeper, OpenedTab duplicate)
        {
            if (duplicate.IsDirty && !keeper.IsDirty &&
                _editorSessions.TryGetValue(duplicate.Id, out var duplicateSession))
            {
                string duplicateText = duplicateSession.GetText();
                if (_editorSessions.TryGetValue(keeper.Id, out var keeperSession))
                {
                    keeperSession.UpdateContentFromSync(duplicateText);
                }
                keeper.Content = duplicateText;

                if (_tabBridges.TryGetValue(keeper.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                {
                    _ = bridgeGroup.Bridge.SetTextAsync(duplicateText, shouldFocus: false);
                }
            }

            keeper.IsDirty |= duplicate.IsDirty;
            keeper.EncodingName = duplicate.EncodingName;
            keeper.EncodingWasAutoDetected = duplicate.EncodingWasAutoDetected;
        }

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
                    UpdateTotalLines(tab);
                    UpdateStatusSelectionStats(null);
                    UpdateLivePreview(tab);
                    UpdateLanguageUI(tab);
                    SyncEncodingCombo(tab);
                    SyncLineEndingText(tab);

                    if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                    {
                        await bridgeGroup.Bridge.RequestSelectionAsync();
                    }
                    _tocController?.RefreshToc(tab);
                }
            }
            UpdateWindowTitle();
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

            CurrentRepoPath = _gitService.FindRepositoryRoot(parent.FullName) ?? string.Empty;
            LoadDirectoryRoot(parent.FullName);
        }

        private void OnFileListViewDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            var item = VisualTreeDataContext.FindFromOriginalSource<ExplorerItem>(e.OriginalSource) ?? FileListView.SelectedItem as ExplorerItem;
            if (item == null) return;

            if (item.IsFolder)
            {
                CurrentRepoPath = _gitService.FindRepositoryRoot(item.Path) ?? string.Empty;
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
            var dialogTheme = GetCurrentElementTheme();
            var result = await ShowUnsavedChangesDialogAsync(
                GetLocalizedString("UnsavedChangesTabCloseTitle", "변경 내용 저장"),
                string.Format(GetLocalizedString("UnsavedChangesTabCloseMessage", "파일 '{0}'의 변경 내용이 저장되지 않았습니다. 닫으시겠습니까?"), tab.Title),
                GetLocalizedString("UnsavedChangesTabCloseDiscard", "저장하지 않고 닫기"),
                GetLocalizedString("UnsavedChangesTabCloseSave", "저장"),
                dialogTheme);

            if (result == UnsavedChangesDialogResult.Discard)
            {
                CloseTabAndCleanup(tab, tabItem);
            }
            else if (result == UnsavedChangesDialogResult.Save)
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
            ClearPendingSplitImeSync(tab.Id);
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
            UpdateWindowTitle();
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
            if (e.Command == "charmap")
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "charmap.exe",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    ShowErrorMessage("문자표 실행 실패", ex.Message);
                }
                return;
            }
            else if (e.Command == "emoji")
            {
                try
                {
                    string emojiFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "md", "standard-unicode-emoji-17-no-private.md");
                    if (File.Exists(emojiFilePath))
                    {
                        await LoadFileIntoTabAsync(emojiFilePath);
                    }
                }
                catch (Exception ex)
                {
                    ShowErrorMessage("이모지 파일 열기 실패", ex.Message);
                }
                return;
            }
            else if (e.Command == "currentDate")
            {
                string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                await InsertTextIntoActiveEditorAsync(dateStr);
                return;
            }

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

        private void UpdateTotalLines(OpenedTab tab)
        {
            if (tab == null || GetActiveTab() != tab) return;
            if (_editorSessions.TryGetValue(tab.Id, out var session))
            {
                int totalLines = session.Model.LineCount;
                string format = GetLocalizedString("StatusTotalLinesFormat", "전체 줄수: {0}");
                StatusTotalLines.Text = string.Format(format, totalLines);
            }
            else
            {
                string format = GetLocalizedString("StatusTotalLinesFormat", "전체 줄수: {0}");
                StatusTotalLines.Text = string.Format(format, 1);
            }
        }

        private void UpdateStatusSelectionStats(string? selectedText)
        {
            if (string.IsNullOrEmpty(selectedText))
            {
                StatusSelectionStats.Visibility = Visibility.Collapsed;
                StatusSelectionStats.Text = string.Empty;
                return;
            }

            int charCount = selectedText.Length;
            int wordCount = selectedText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            int lineCount = selectedText.Replace("\r\n", "\n").Split('\n').Length;

            string format = GetLocalizedString("StatusSelectionStatsFormat", "선택됨: {0}자 / {1}단어 / {2}줄");
            StatusSelectionStats.Text = string.Format(format, charCount.ToString("N0"), wordCount.ToString("N0"), lineCount.ToString("N0"));
            StatusSelectionStats.Visibility = Visibility.Visible;
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

            if (selectedEncoding == "Auto")
            {
                await ReloadTabWithEncodingAsync(tab, selectedEncoding);
                return;
            }

            string dirtyWarning = tab.IsDirty
                ? GetLocalizedString("EncodingChangeDirtyWarning", "\n\n(주의: '다시 읽기'를 선택하면 저장하지 않은 변경 사항이 유실됩니다!)")
                : string.Empty;

            string contentFormat = GetLocalizedString(
                "EncodingChangeContentFormat",
                "현재 열려 있는 파일의 인코딩을 '{0}'(으)로 변경하시겠습니까?\n\n- 변환: 현재 편집 중인 텍스트를 유지하고 파일 인코딩 형식을 변환하여 저장합니다.\n- 다시 읽기: 저장된 파일을 해당 인코딩으로 다시 로드합니다.{1}");

            var dialog = new ContentDialog
            {
                Title = GetLocalizedString("EncodingChangeTitle", "인코딩 변경"),
                Content = string.Format(contentFormat, selectedEncoding, dirtyWarning),
                PrimaryButtonText = GetLocalizedString("EncodingChangeConvert", "변환"),
                SecondaryButtonText = GetLocalizedString("EncodingChangeReopen", "다시 읽기"),
                CloseButtonText = GetLocalizedString("EncodingChangeCancel", "취소"),
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = GetCurrentElementTheme()
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                tab.EncodingName = selectedEncoding;
                var tabItem = EditorTabView.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id)
                           ?? EditorTabView2.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id);
                if (tabItem != null)
                {
                    MarkTabDirty(tab, tabItem);
                }
                else
                {
                    tab.IsDirty = true;
                }
                SyncEncodingCombo(tab);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await ReloadTabWithEncodingAsync(tab, selectedEncoding);
            }
            else
            {
                SyncEncodingCombo(tab);
            }
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

                if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                {
                    await bridgeGroup.Bridge.InitializeModelAsync(
                        session.Model.LineCount,
                        tab.Language,
                        _settingsService.CurrentSettings,
                        isReadOnly: false,
                        initialLines: session.GetLines(1, InitialEditorLineWarmupCount));
                    await bridgeGroup.Bridge.SetLanguageAsync(tab.FilePath);
                }

                UpdateLivePreview(tab);
                UpdateStatusFileStats(tab);
                UpdateTotalLines(tab);
                UpdateStatusSelectionStats(null);
                UpdateLanguageUI(tab);
                SyncEncodingCombo(tab);
                SyncLineEndingText(tab);
                UpdateWindowTitle();
            }
            catch (Exception ex)
            {
                ShowErrorMessage("인코딩 변경 실패", ex.Message);
                SyncEncodingCombo(tab);
                SyncLineEndingText(tab);
            }
        }

        private void OnGitFileRestored(object? sender, string filePath)
        {
            this.DispatcherQueue.TryEnqueue(async () =>
            {
                var tabsToProcess = _viewModel.Tabs.Where(t =>
                    !string.IsNullOrEmpty(t.FilePath) &&
                    (
                        (!string.IsNullOrEmpty(filePath) && t.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)) ||
                        (string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(_currentRepoPath) && t.FilePath.StartsWith(_currentRepoPath, StringComparison.OrdinalIgnoreCase))
                    )
                ).ToList();

                foreach (var tab in tabsToProcess)
                {
                    if (!File.Exists(tab.FilePath))
                    {
                        var tabItem = EditorTabView.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id)
                                   ?? EditorTabView2.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id);
                        if (tabItem != null)
                        {
                            CloseTabAndCleanup(tab, tabItem);
                        }
                    }
                    else
                    {
                        await ReloadTabWithEncodingAsync(tab, tab.EncodingName);
                    }
                }

                // Refresh the file browser list to reflect the restored files on disk
                if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                {
                    LoadDirectoryRoot(_currentFolderPath);
                }
            });
        }

        private bool QueuePendingSplitImeLineSyncIfNeeded(OpenedTab sourceTab, int lineNumber, string text)
        {
            if (string.IsNullOrEmpty(sourceTab.FilePath)) return false;
            if (!HasOtherTabForSameFile(sourceTab)) return false;

            if (!_pendingSplitImeSyncStates.TryGetValue(sourceTab.Id, out var state))
            {
                state = new PendingSplitImeSyncState();
                _pendingSplitImeSyncStates[sourceTab.Id] = state;
            }

            // A new IME composition has started. If the previous completed syllable had
            // already scheduled a split-pane UI update, cancel it so the other WebView is
            // not patched while the new Hangul syllable is composing.
            StopPendingSplitImeTimer(state);
            state.Lines[lineNumber] = text;

            // Only defer split UI synchronization after this IME batch is proven to be
            // a column edit. A normal single-caret IME composition must keep the existing
            // immediate live synchronization behavior.
            if (state.Lines.Count > 1)
            {
                state.IsColumnEdit = true;
            }

            return state.IsColumnEdit;
        }

        private bool SchedulePendingSplitImeCompletionSyncIfNeeded(OpenedTab sourceTab, int lineNumber, string text)
        {
            if (!_pendingSplitImeSyncStates.TryGetValue(sourceTab.Id, out var state))
            {
                return false;
            }

            state.Lines[lineNumber] = text;
            if (!state.IsColumnEdit && state.Lines.Count <= 1)
            {
                // The IME composition affected only one line, so this is not a column edit.
                // Clear the candidate state and let the caller perform the normal immediate
                // split synchronization path.
                ClearPendingSplitImeSync(sourceTab.Id);
                return false;
            }

            state.IsColumnEdit = true;

            // Keep the committed lines in the pending set, but do not touch the other split
            // WebView immediately. Updating the opposite pane right after compositionend can
            // overlap with the next compositionstart and remove the first jamo of the next
            // Korean syllable during column editing.
            ScheduleDeferredPendingSplitImeSync(sourceTab, state);
            return true;
        }

        private bool ScheduleDeferredPendingSplitImeSyncIfNeeded(OpenedTab sourceTab)
        {
            if (!_pendingSplitImeSyncStates.TryGetValue(sourceTab.Id, out var state))
            {
                return false;
            }

            if (!state.IsColumnEdit && state.Lines.Count <= 1)
            {
                ClearPendingSplitImeSync(sourceTab.Id);
                return false;
            }

            state.IsColumnEdit = true;
            ScheduleDeferredPendingSplitImeSync(sourceTab, state);
            return true;
        }

        private void ScheduleDeferredPendingSplitImeSync(OpenedTab sourceTab, PendingSplitImeSyncState state)
        {
            StopPendingSplitImeTimer(state);

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SplitImeDeferredUiSyncDelayMs)
            };

            state.DeferredSyncTimer = timer;
            timer.Tick += async (_, _) =>
            {
                timer.Stop();

                if (_pendingSplitImeSyncStates.TryGetValue(sourceTab.Id, out var currentState) &&
                    ReferenceEquals(currentState, state) &&
                    ReferenceEquals(currentState.DeferredSyncTimer, timer))
                {
                    await FlushPendingSplitImeSyncAsync(sourceTab);
                }
            };
            timer.Start();
        }

        private static void StopPendingSplitImeTimer(PendingSplitImeSyncState state)
        {
            if (state.DeferredSyncTimer != null)
            {
                state.DeferredSyncTimer.Stop();
                state.DeferredSyncTimer = null;
            }
        }

        private bool HasOtherTabForSameFile(OpenedTab sourceTab)
        {
            if (string.IsNullOrEmpty(sourceTab.FilePath)) return false;
            return GetTabsForSameFile(sourceTab).Any(tab => tab.Id != sourceTab.Id);
        }

        private async Task FlushPendingSplitImeSyncAsync(OpenedTab sourceTab)
        {
            if (!_pendingSplitImeSyncStates.TryGetValue(sourceTab.Id, out var state)) return;

            StopPendingSplitImeTimer(state);
            var pendingLineNumbers = state.Lines.Keys.OrderBy(line => line).ToList();
            ClearPendingSplitImeSync(sourceTab.Id);

            foreach (int lineNumber in pendingLineNumbers)
            {
                string lineText = GetCurrentLineText(sourceTab, lineNumber, string.Empty);
                await SyncLineChangeToOtherTabsAsync(sourceTab, lineNumber, lineText, isComposing: false);
            }
        }

        private string GetCurrentLineText(OpenedTab tab, int lineNumber, string fallback)
        {
            if (_editorSessions.TryGetValue(tab.Id, out var session))
            {
                return session.GetLines(lineNumber, 1).FirstOrDefault() ?? string.Empty;
            }

            return fallback;
        }

        private void ClearPendingSplitImeSync(string tabId)
        {
            if (_pendingSplitImeSyncStates.TryGetValue(tabId, out var state))
            {
                StopPendingSplitImeTimer(state);
            }

            _pendingSplitImeSyncStates.Remove(tabId);
        }

        private async Task SyncLineChangeToOtherTabsAsync(OpenedTab sourceTab, int lineNumber, string text, bool isComposing)
        {
            if (string.IsNullOrEmpty(sourceTab.FilePath)) return;

            bool sourceDirty = sourceTab.IsDirty;
            var otherTabs = GetTabsForSameFile(sourceTab)
                .Where(t => t.Id != sourceTab.Id)
                .ToList();

            foreach (var otherTab in otherTabs)
            {
                if (_editorSessions.TryGetValue(otherTab.Id, out var otherSession))
                {
                    otherSession.ReplaceLine(lineNumber, text);
                    otherTab.Content = otherSession.GetText();
                }
                else
                {
                    otherTab.Content = text;
                }

                if (_tabBridges.TryGetValue(otherTab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                {
                    await bridgeGroup.Bridge.UpdateLineAsync(lineNumber, text, isComposing);
                }

                if (!isComposing)
                {
                    SchedulePreview(otherTab);
                }
            }

            SetDirtyStateForFileGroup(sourceTab, sourceDirty);
        }

        private async Task SyncEditsToOtherTabsAsync(OpenedTab sourceTab, bool updateUi = true)
        {
            if (string.IsNullOrEmpty(sourceTab.FilePath)) return;

            // Full-text synchronization supersedes any deferred IME line patches.
            ClearPendingSplitImeSync(sourceTab.Id);

            if (!_editorSessions.TryGetValue(sourceTab.Id, out var sourceSession)) return;
            string updatedText = sourceSession.GetText();
            bool sourceDirty = sourceTab.IsDirty;

            var otherTabs = GetTabsForSameFile(sourceTab)
                .Where(t => t.Id != sourceTab.Id)
                .ToList();

            foreach (var otherTab in otherTabs)
            {
                if (_editorSessions.TryGetValue(otherTab.Id, out var otherSession))
                {
                    otherSession.UpdateContentFromSync(updatedText);
                }
                otherTab.Content = updatedText;

                if (updateUi && _tabBridges.TryGetValue(otherTab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                {
                    await bridgeGroup.Bridge.SetTextAsync(updatedText, shouldFocus: false);
                }

                if (updateUi)
                {
                    SchedulePreview(otherTab);
                }
            }

            SetDirtyStateForFileGroup(sourceTab, sourceDirty);
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
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = GetCurrentElementTheme()
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

            var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
            if (tab != null)
            {
                MarkTabDirty(tab, activeTabItem);
                PropagateDirtyStateToOtherTabs(tab);
            }

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

        private void UpdateWindowTitle()
        {
            var activeTab = GetActiveTab();
            string pathOrTitle = activeTab != null 
                ? (!string.IsNullOrEmpty(activeTab.FilePath) ? activeTab.FilePath : activeTab.Title)
                : "";

            string newTitle = string.IsNullOrEmpty(pathOrTitle) 
                ? "Ueditor" 
                : $"Ueditor - {pathOrTitle}";

            this.Title = newTitle;

            if (AppTitleTextBlock != null)
            {
                AppTitleTextBlock.Text = newTitle;
            }
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

        private async void OnReplaceOneClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SearchResultItem item)
            {
                await _searchReplaceController.ReplaceOneAsync(item);
            }
        }

        private async Task OnSearchReplaceFileModifiedAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            var matchedTabs = _viewModel.Tabs.Where(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matchedTabs.Count == 0) return;

            try
            {
                var readResult = await LineArrayTextModel.LoadFromFileAsync(filePath, "Auto");
                
                foreach (var tab in matchedTabs)
                {
                    if (_editorSessions.TryGetValue(tab.Id, out var session))
                    {
                        session.UpdateContentFromSync(readResult.Model.GetText());
                    }

                    tab.Content = readResult.Model.GetText();
                    tab.IsDirty = false;

                    if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                    {
                        await bridgeGroup.Bridge.SetTextAsync(tab.Content, shouldFocus: false);
                    }

                    var tabItem = EditorTabView.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id)
                               ?? EditorTabView2.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id);
                    if (tabItem != null)
                    {
                        CleanDirtyStateOnOtherTabs(tab);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to hot-reload replaced file '{filePath}': {ex.Message}");
            }
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

        private async Task FlushTabEditorBeforeSaveAsync(OpenedTab tab)
        {
            if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
            {
                await bridgeGroup.Bridge.FlushPendingEditForSaveAsync();
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
                await FlushTabEditorBeforeSaveAsync(tab);
                await FlushPendingSplitImeSyncAsync(tab);

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
                CleanDirtyStateOnOtherTabs(tab);

                UpdateStatusFileStats(tab);
                UpdateTotalLines(tab);
                UpdateLanguageUI(tab);
                SyncEncodingCombo(tab);
                SyncLineEndingText(tab);
                await RefreshGitStatusUIAsync();
                UpdateWindowTitle();

                if (!string.IsNullOrEmpty(tab.FilePath) && File.Exists(tab.FilePath))
                {
                    _favoritesRecentController.AddRecentFile(tab.FilePath);
                }

                if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                {
                    LoadDirectoryRoot(_currentFolderPath);
                }

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
                await FlushTabEditorBeforeSaveAsync(tab);
                await FlushPendingSplitImeSyncAsync(tab);

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
                UpdateStatusFileStats(tab);
                UpdateTotalLines(tab);
                UpdateLanguageUI(tab);
                SyncEncodingCombo(tab);
                await RefreshGitStatusUIAsync();
                UpdateWindowTitle();

                if (!string.IsNullOrEmpty(tab.FilePath) && File.Exists(tab.FilePath))
                {
                    _favoritesRecentController.AddRecentFile(tab.FilePath);
                }

                if (!string.IsNullOrEmpty(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                {
                    LoadDirectoryRoot(_currentFolderPath);
                }

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
        private bool _isUnsavedChangesDialogShowing = false;
        private async void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (_isClosingConfirmed)
            {
                await SaveUiLayoutSettingsAsync();
                return;
            }

            if (_isUnsavedChangesDialogShowing)
            {
                args.Cancel = true;
                return;
            }

            var dirtyTabs = _viewModel.Tabs.Where(t => t.IsDirty).ToList();
            if (dirtyTabs.Count > 0)
            {
                args.Cancel = true; // Prevent immediate close before awaiting UI work
            }

            await SaveUiLayoutSettingsAsync();
            if (dirtyTabs.Count == 0) return;

            var dialogTheme = GetCurrentElementTheme();
            var result = await ShowUnsavedChangesDialogAsync(
                GetLocalizedString("UnsavedChangesAppCloseTitle", "저장되지 않은 변경 사항"),
                string.Format(GetLocalizedString("UnsavedChangesAppCloseMessage", "저장되지 않은 탭이 {0}개 있습니다. 종료하기 전에 저장하시겠습니까?"), dirtyTabs.Count),
                GetLocalizedString("UnsavedChangesAppCloseDiscard", "저장하지 않고 종료"),
                GetLocalizedString("UnsavedChangesAppCloseSave", "저장하고 종료"),
                dialogTheme);

            if (result == UnsavedChangesDialogResult.Discard)
            {
                _isClosingConfirmed = true;
                this.Close();
            }
            else if (result == UnsavedChangesDialogResult.Save)
            {
                foreach (var tab in dirtyTabs)
                {
                    bool saved = await SaveTabAsync(tab);
                    if (!saved) return; // Abort exit if save fails or cancels
                }
                _isClosingConfirmed = true;
                this.Close();
            }
        }

        private async Task<UnsavedChangesDialogResult> ShowUnsavedChangesDialogAsync(
            string title,
            string message,
            string discardButtonText,
            string saveButtonText,
            ElementTheme theme)
        {
            if (_isUnsavedChangesDialogShowing)
            {
                return UnsavedChangesDialogResult.Cancel;
            }

            _isUnsavedChangesDialogShowing = true;
            try
            {
                var result = UnsavedChangesDialogResult.Cancel;
                var dialog = new ContentDialog
                {
                    Title = title,
                    RequestedTheme = theme,
                    XamlRoot = this.Content.XamlRoot
                };

                string cancelText = GetLocalizedString("UnsavedChangesCancel", "취소");

                dialog.Content = CreateUnsavedChangesDialogContent(
                    message,
                    discardButtonText,
                    saveButtonText,
                    cancelText,
                    theme,
                    () =>
                    {
                        result = UnsavedChangesDialogResult.Discard;
                        dialog.Hide();
                    },
                    () =>
                    {
                        result = UnsavedChangesDialogResult.Save;
                        dialog.Hide();
                    },
                    () =>
                    {
                        result = UnsavedChangesDialogResult.Cancel;
                        dialog.Hide();
                    },
                    out var defaultButton);

                dialog.Opened += (_, __) => defaultButton.Focus(FocusState.Programmatic);
                await dialog.ShowAsync();
                return result;
            }
            finally
            {
                _isUnsavedChangesDialogShowing = false;
            }
        }

        private enum UnsavedChangesDialogResult
        {
            Cancel,
            Discard,
            Save
        }

        private ElementTheme GetCurrentElementTheme()
        {
            if (string.Equals(_settingsService.CurrentSettings.Theme, "Light", StringComparison.OrdinalIgnoreCase))
            {
                return ElementTheme.Light;
            }

            if (string.Equals(_settingsService.CurrentSettings.Theme, "Dark", StringComparison.OrdinalIgnoreCase))
            {
                return ElementTheme.Dark;
            }

            return this.Content is FrameworkElement element
                ? element.ActualTheme
                : ElementTheme.Default;
        }

        private static FrameworkElement CreateUnsavedChangesDialogContent(
            string message,
            string discardButtonText,
            string saveButtonText,
            string cancelButtonText,
            ElementTheme theme,
            Action discardAction,
            Action saveAction,
            Action cancelAction,
            out Button defaultButton)
        {
            var root = new StackPanel
            {
                Spacing = 22,
                MinWidth = 360,
                MaxWidth = 520
            };

            root.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            });

            var buttonRow = new Grid
            {
                ColumnSpacing = 12
            };
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var discardButton = CreateSolidDialogButton(discardButtonText, DialogButtonVisual.Destructive, theme);
            discardButton.HorizontalAlignment = HorizontalAlignment.Left;
            discardButton.Click += (_, __) => discardAction();
            Grid.SetColumn(discardButton, 0);
            buttonRow.Children.Add(discardButton);

            var rightButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelButton = new Button
            {
                Content = cancelButtonText,
                MinWidth = 90,
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(4),
                RequestedTheme = theme
            };
            cancelButton.Click += (_, __) => cancelAction();

            var saveButton = CreateSolidDialogButton(saveButtonText, DialogButtonVisual.Accent, theme);
            saveButton.Click += (_, __) => saveAction();
            defaultButton = saveButton;

            rightButtons.Children.Add(cancelButton);
            rightButtons.Children.Add(saveButton);
            Grid.SetColumn(rightButtons, 1);
            buttonRow.Children.Add(rightButtons);

            root.Children.Add(buttonRow);
            root.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    e.Handled = true;
                    cancelAction();
                }
                else if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    e.Handled = true;
                    saveAction();
                }
            };

            return root;
        }

        private enum DialogButtonVisual
        {
            Destructive,
            Accent
        }

        private static Button CreateSolidDialogButton(string text, DialogButtonVisual visual, ElementTheme theme)
        {
            bool dark = theme == ElementTheme.Dark;
            Windows.UI.Color normalColor;
            Windows.UI.Color hoverColor;
            Windows.UI.Color pressedColor;

            if (visual == DialogButtonVisual.Destructive)
            {
                normalColor = dark
                    ? Windows.UI.Color.FromArgb(255, 179, 38, 30)
                    : Windows.UI.Color.FromArgb(255, 196, 43, 28);
                hoverColor = Windows.UI.Color.FromArgb(255, 209, 52, 56);
                pressedColor = dark
                    ? Windows.UI.Color.FromArgb(255, 143, 29, 24)
                    : Windows.UI.Color.FromArgb(255, 168, 0, 0);
            }
            else
            {
                normalColor = dark
                    ? Windows.UI.Color.FromArgb(255, 96, 178, 255)
                    : Windows.UI.Color.FromArgb(255, 0, 95, 184);
                hoverColor = dark
                    ? Windows.UI.Color.FromArgb(255, 117, 188, 255)
                    : Windows.UI.Color.FromArgb(255, 0, 103, 192);
                pressedColor = dark
                    ? Windows.UI.Color.FromArgb(255, 64, 152, 232)
                    : Windows.UI.Color.FromArgb(255, 0, 74, 152);
            }

            var button = new Button
            {
                Content = text,
                MinWidth = 90,
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(4),
                RequestedTheme = theme
            };

            SetSolidButtonResources(button, normalColor, hoverColor, pressedColor);
            ApplySolidButtonColors(button, normalColor);
            button.PointerEntered += (_, __) => ApplySolidButtonColors(button, hoverColor);
            button.PointerExited += (_, __) => ApplySolidButtonColors(button, normalColor);
            button.PointerPressed += (_, __) => ApplySolidButtonColors(button, pressedColor);
            button.PointerReleased += (_, __) => ApplySolidButtonColors(button, hoverColor);
            return button;
        }

        private static void SetSolidButtonResources(
            Button button,
            Windows.UI.Color normalColor,
            Windows.UI.Color hoverColor,
            Windows.UI.Color pressedColor)
        {
            button.Resources["ButtonBackground"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(normalColor);
            button.Resources["ButtonForeground"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            button.Resources["ButtonBorderBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(normalColor);
            button.Resources["ButtonBackgroundPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(hoverColor);
            button.Resources["ButtonForegroundPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            button.Resources["ButtonBorderBrushPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(hoverColor);
            button.Resources["ButtonBackgroundPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(pressedColor);
            button.Resources["ButtonForegroundPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            button.Resources["ButtonBorderBrushPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(pressedColor);
        }

        private static void ApplySolidButtonColors(Button button, Windows.UI.Color color)
        {
            void Apply()
            {
                var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
                button.Background = brush;
                button.BorderBrush = brush;
                button.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            }

            Apply();
            button.DispatcherQueue.TryEnqueue(Apply);
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
            var selection = await _compareSelectionDialogService.ShowAsync(this, this.Content.XamlRoot, _viewModel.Tabs, GetCurrentElementTheme(), GetLocalizedString);
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

            var coreWebView = diffWebView.CoreWebView2;
            if (coreWebView == null)
            {
                throw new InvalidOperationException("CoreWebView2 failed to initialize.");
            }

            try
            {
                bool isDark = string.Equals(_settingsService.CurrentSettings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
                if (coreWebView.Profile != null)
                {
                    coreWebView.Profile.PreferredColorScheme = isDark
                        ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
                        : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;
                }
            }
            catch { }

            string webResourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebResources");
            coreWebView.SetVirtualHostNameToFolderMapping(
                "ueditor.local",
                webResourcesPath,
                CoreWebView2HostResourceAccessKind.Allow
            );

            coreWebView.Settings.IsWebMessageEnabled = true;
            coreWebView.Settings.IsScriptEnabled = true;
            coreWebView.Settings.AreDefaultContextMenusEnabled = false;
            coreWebView.Settings.AreDevToolsEnabled = false;

            diffWebView.WebMessageReceived += (s, args) =>
            {
                try
                {
                    string json = NormalizeWebMessageJson(args);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var typeProp) && string.Equals(typeProp.GetString(), "shortcut", StringComparison.Ordinal))
                    {
                        if (root.TryGetProperty("name", out var nameProp))
                        {
                            string name = nameProp.GetString() ?? string.Empty;
                            this.DispatcherQueue.TryEnqueue(() =>
                            {
                                if (string.Equals(name, "f9", StringComparison.Ordinal))
                                    ToggleTopMostShortcut();
                                else if (string.Equals(name, "f10", StringComparison.Ordinal))
                                    OnToggleThemeClick(this, new RoutedEventArgs());
                                else if (string.Equals(name, "f12", StringComparison.Ordinal))
                                    OnStickyNoteClick(this, new RoutedEventArgs());
                            });
                        }
                    }
                }
                catch { }
            };

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
                    uiFontFamily = _settingsService.CurrentSettings.UiFontFamily,
                    compareToolTitle = GetLocalizedString("DiffCompareToolTitle", "Ueditor 파일 비교 도구 (File Compare)"),
                    statsGathering = GetLocalizedString("DiffStatsGathering", "수집 중..."),
                    originalFileLabel = GetLocalizedString("DiffOriginalFileLabel", "원본 파일 (Original)"),
                    modifiedFileLabel = GetLocalizedString("DiffModifiedFileLabel", "비교 대상 파일 (Modified)"),
                    originalPrefix = GetLocalizedString("DiffOriginalPrefix", "원본: "),
                    modifiedPrefix = GetLocalizedString("DiffModifiedPrefix", "수정본: "),
                    diffStatsFormat = GetLocalizedString("DiffStatsFormat", "변경사항: 추가 {0}줄, 삭제 {1}줄")
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

            string repoPath;
            try
            {
                repoPath = Path.GetFullPath(_currentRepoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            }
            catch
            {
                return;
            }

            var dirtyTabs = _viewModel.Tabs.Where(t =>
            {
                if (!t.IsDirty || string.IsNullOrEmpty(t.FilePath)) return false;
                try
                {
                    string fullPath = Path.GetFullPath(t.FilePath);
                    return fullPath.StartsWith(repoPath, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }).ToList();

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
            var lineBox = new TextBox { PlaceholderText = GetLocalizedString("GoToLinePlaceholder", "이동할 줄 번호 입력..."), Width = 200 };
            int currentLine = int.TryParse(StatusLine.Text, out int line) ? line : 1;
            lineBox.Text = currentLine.ToString();
            var dialog = new ContentDialog
            {
                Title = GetLocalizedString("GoToLineTitle", "줄 이동 (Go to Line)"),
                Content = lineBox,
                PrimaryButtonText = GetLocalizedString("GoToLineButton", "이동"),
                CloseButtonText = GetLocalizedString("GoToLineCancel", "취소"),
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = GetCurrentElementTheme()
            };

            lineBox.KeyDown += async (s, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                {
                    args.Handled = true;
                    if (int.TryParse(lineBox.Text, out int targetLine) && targetLine > 0)
                    {
                        dialog.Hide();
                        await PerformLineNavigationAsync(activeTab.Id, targetLine);
                    }
                }
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && int.TryParse(lineBox.Text, out int clickedLine) && clickedLine > 0)
            {
                await PerformLineNavigationAsync(activeTab.Id, clickedLine);
            }
        }

        private async Task PerformLineNavigationAsync(string tabId, int targetLine)
        {
            if (_tabBridges.TryGetValue(tabId, out var bridgeGroup))
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

        private void OnStatusLineEndingClick(object sender, RoutedEventArgs e)
        {
            var tab = GetActiveTab();
            if (tab == null) return;

            string currentLe = "LF";
            if (_editorSessions.TryGetValue(tab.Id, out var session))
            {
                currentLe = session.Model.LineEnding == "\r\n" ? "CRLF" : "LF";
            }

            var flyout = new MenuFlyout();
            var lfItem = new MenuFlyoutItem { Text = "LF" };
            var crlfItem = new MenuFlyoutItem { Text = "CRLF" };

            lfItem.Click += async (s, args) =>
            {
                if (currentLe == "LF") return;
                await ChangeLineEndingWithPopupAsync(tab, "LF");
            };

            crlfItem.Click += async (s, args) =>
            {
                if (currentLe == "CRLF") return;
                await ChangeLineEndingWithPopupAsync(tab, "CRLF");
            };

            flyout.Items.Add(lfItem);
            flyout.Items.Add(crlfItem);
            if (sender is Button btn)
                flyout.ShowAt(btn, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Top });
        }

        private async Task ChangeLineEndingWithPopupAsync(OpenedTab tab, string targetEnding)
        {
            string contentFormat = GetLocalizedString(
                "LineEndingChangeContentFormat",
                "현재 열려 있는 파일의 줄 끝 방식을 '{0}'(으)로 변환하시겠습니까?");

            var dialog = new ContentDialog
            {
                Title = GetLocalizedString("LineEndingChangeTitle", "줄 끝 방식 변경"),
                Content = string.Format(contentFormat, targetEnding),
                PrimaryButtonText = GetLocalizedString("LineEndingChangeConvert", "변환"),
                CloseButtonText = GetLocalizedString("LineEndingChangeCancel", "취소"),
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = GetCurrentElementTheme()
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (_editorSessions.TryGetValue(tab.Id, out var session))
                {
                    session.Model.LineEnding = targetEnding == "CRLF" ? "\r\n" : "\n";
                }

                var tabItem = EditorTabView.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id)
                           ?? EditorTabView2.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id);
                if (tabItem != null)
                {
                    MarkTabDirty(tab, tabItem);
                }
                else
                {
                    tab.IsDirty = true;
                }

                StatusBarPane.LineEndingText.Text = targetEnding;
                _currentLineEnding = targetEnding;
            }
        }

        private void SyncLineEndingText(OpenedTab tab)
        {
            if (_editorSessions.TryGetValue(tab.Id, out var session))
            {
                string le = session.Model.LineEnding == "\r\n" ? "CRLF" : "LF";
                StatusBarPane.LineEndingText.Text = le;
                _currentLineEnding = le;
            }
            else
            {
                StatusBarPane.LineEndingText.Text = "LF";
                _currentLineEnding = "LF";
            }
        }

#pragma warning disable CS0414
        private static string? _currentLineEnding = "LF";
#pragma warning restore CS0414

        private void OnRootKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (TryHandleFunctionKeyShortcut(e.Key))
            {
                e.Handled = true;
                return;
            }

            var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            if (ctrl)
            {
                if (e.Key == Windows.System.VirtualKey.N)
                {
                    e.Handled = true;
                    OpenNewTab();
                }
                else if (e.Key == Windows.System.VirtualKey.Number1)
                {
                    e.Handled = true;
                    _ = ToggleLeftPanelAsync();
                }
                else if (e.Key == Windows.System.VirtualKey.Number2)
                {
                    e.Handled = true;
                    _ = ToggleRightPanelAsync();
                }
                else if (shift && e.Key == Windows.System.VirtualKey.F)
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
                    if (shift)
                    {
                        OnSaveAsFileClick(this, new RoutedEventArgs());
                    }
                    else
                    {
                        OnSaveFileClick(this, new RoutedEventArgs());
                    }
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

        private bool TryHandleFunctionKeyShortcut(Windows.System.VirtualKey key)
        {
            switch (key)
            {
                case Windows.System.VirtualKey.F9:
                    ToggleTopMostShortcut();
                    return true;
                case Windows.System.VirtualKey.F10:
                    OnToggleThemeClick(this, new RoutedEventArgs());
                    return true;
                case Windows.System.VirtualKey.F12:
                    OnStickyNoteClick(this, new RoutedEventArgs());
                    return true;
            }

            return false;
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

        #endregion
    }

}
