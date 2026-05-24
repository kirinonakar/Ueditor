using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Globalization;
using Windows.Graphics;
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
        private readonly MainWindowViewModel _viewModel = new MainWindowViewModel();
        private ResourceManager _resourceManager = new ResourceManager();
        private ResourceContext? _resourceContext;
        private readonly Dictionary<string, Dictionary<string, string>> _reswStringCache = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private string _lastSelectionText = string.Empty;
        private string _lastSearchQuery = string.Empty;
        private string _currentFolderPath = string.Empty;
        private string _currentRepoPath = string.Empty;
        private string _llmFileContextText = string.Empty;
        private bool _isSyncingEncodingCombo = false;
        
        // Dynamic tabs collection
        private readonly Dictionary<string, (WebView2 WebView, MonacoBridge Bridge)> _tabBridges = 
            new Dictionary<string, (WebView2 WebView, MonacoBridge Bridge)>();

        // Timer for debouncing live preview renders
        private readonly DispatcherTimer _previewDebounceTimer;
        private OpenedTab? _activeTabForPreview = null;

        // Autosave timer
        private readonly DispatcherTimer _autoSaveTimer;
        private bool _autoSaveEnabled = false;

        // Git auto-refresh timer
        private readonly DispatcherTimer _gitAutoRefreshTimer;

        // Custom Splitter state variables
        private bool _isDraggingLeftSplitter = false;
        private double _leftSplitterStartExplorerWidth = 0;
        private double _leftSplitterStartPointerX = 0;

        private bool _isDraggingRightSplitter = false;
        private double _rightSplitterStartPreviewWidth = 0;
        private double _rightSplitterStartPointerX = 0;
        private bool _isDraggingTerminalSplitter = false;
        private double _terminalSplitterStartHeight = 0;
        private double _terminalSplitterStartPointerY = 0;
        private double _lastExplorerWidth = 260;
        private double _lastPreviewWidth = 400;
        private double _lastTerminalHeight = 220;
        private const double ExplorerPanelMinWidth = 150;
        private const double PreviewPanelMinWidth = 150;

        // Split Editor State
        private TabView? _activeTabView;
        private bool _isDraggingEditorSplitter = false;
        private double _editorSplitterStartWidth = 0;
        private double _editorSplitterStartHeight = 0;
        private double _editorSplitterStartPointerX = 0;
        private double _editorSplitterStartPointerY = 0;
        private bool _isVerticalSplit = true;
        private enum SplitMode { None, Vertical, Horizontal }
        private SplitMode _currentSplitMode = SplitMode.None;

        private ToggleButton LeftPanelToggle => StatusBarPane.LeftPanelToggleButton;
        private ToggleButton RightPanelToggle => StatusBarPane.RightPanelToggleButton;
        private TextBlock StatusLine => StatusBarPane.LineText;
        private TextBlock StatusLineLabel => StatusBarPane.LineLabelText;
        private TextBlock StatusCol => StatusBarPane.ColumnText;
        private TextBlock StatusColumnLabel => StatusBarPane.ColumnLabelText;
        private TextBlock StatusFileStats => StatusBarPane.FileStatsText;
        private TextBlock StatusGitBranch => StatusBarPane.GitBranchText;
        private TextBlock StatusMode => StatusBarPane.ModeText;
        private TextBlock StatusLanguage => StatusBarPane.LanguageText;
        private ComboBox StatusEncodingCombo => StatusBarPane.EncodingCombo;
        private Grid ExplorerSidebarPage => LeftSidebarTabView.ExplorerPage;
        private Grid FavoritesSidebarPage => LeftSidebarTabView.FavoritesPage;
        private Grid SnippetsSidebarPage => LeftSidebarTabView.SnippetsPage;
        private Grid GitSidebarPage => LeftSidebarTabView.GitPage;
        private Grid SearchSidebarPage => LeftSidebarTabView.SearchPage;
        private Grid RecentSidebarPage => LeftSidebarTabView.RecentPage;
        private ToggleButton ExplorerActivityButton => LeftSidebarTabView.ExplorerActivity;
        private ToggleButton FavoritesActivityButton => LeftSidebarTabView.FavoritesActivity;
        private ToggleButton SnippetsActivityButton => LeftSidebarTabView.SnippetsActivity;
        private ToggleButton GitActivityButton => LeftSidebarTabView.GitActivity;
        private ToggleButton SearchActivityButton => LeftSidebarTabView.SearchActivity;
        private ToggleButton RecentActivityButton => LeftSidebarTabView.RecentActivity;
        private TextBlock ExplorerStatusText => LeftSidebarTabView.ExplorerStatus;
        private TextBlock FavoritesHeaderText => LeftSidebarTabView.FavoritesHeader;
        private TextBlock SnippetsHeaderText => LeftSidebarTabView.SnippetsHeader;
        private Button AddSnippetButton => LeftSidebarTabView.AddSnippet;
        private ListView FileListView => LeftSidebarTabView.FileList;
        private ListView FavoritesListView => LeftSidebarTabView.FavoritesList;
        private ListView RecentFilesListView => LeftSidebarTabView.RecentFilesList;
        private ListView SnippetsListView => LeftSidebarTabView.SnippetsList;
        private ListView GitChangedFilesList => LeftSidebarTabView.GitChangedFiles;
        private ListView GitHistoryList => LeftSidebarTabView.GitHistory;
        private ListView SearchResultsList => LeftSidebarTabView.SearchResults;
        private TextBlock GitPanelBranchText => LeftSidebarTabView.GitPanelBranch;
        private ComboBox GitBranchesCombo => LeftSidebarTabView.GitBranches;
        private TextBox GitCommitMessageInput => LeftSidebarTabView.GitCommitMessage;
        private TextBox SearchQueryInput => LeftSidebarTabView.SearchQuery;
        private TextBox ReplaceQueryInput => LeftSidebarTabView.ReplaceQuery;
        private ToggleButton SearchMatchCaseToggle => LeftSidebarTabView.SearchMatchCase;
        private ToggleButton SearchWholeWordToggle => LeftSidebarTabView.SearchWholeWord;
        private ToggleButton SearchRegexToggle => LeftSidebarTabView.SearchRegex;
        private TextBlock SearchHeaderText => LeftSidebarTabView.SearchHeaderLabel;
        private Button SearchAllButton => LeftSidebarTabView.SearchAllFilesBtn;
        private Button ReplaceAllButton => LeftSidebarTabView.ReplaceAllFilesBtn;
        private TextBlock RecentFilesHeaderText => LeftSidebarTabView.RecentFilesHeaderLabel;
        private TextBlock GitHeaderText => LeftSidebarTabView.GitHeaderLabel;
        private Button GitCommitButton => LeftSidebarTabView.GitCommitBtn;
        private Button GitStageAllButton => LeftSidebarTabView.GitStageAllBtn;
        private Button GitRestoreAllButton => LeftSidebarTabView.GitRestoreAllBtn;
        private Button GitPushButton => LeftSidebarTabView.GitPushBtn;
        private Button GitRefreshButton => LeftSidebarTabView.GitRefreshBtn;
        private TextBlock GitHistoryHeader => LeftSidebarTabView.GitHistoryHeaderLabel;
        private Button ExplorerUpButton => LeftSidebarTabView.ExplorerUpBtn;
        private Button ExplorerSelectFolderButton => LeftSidebarTabView.ExplorerSelectFolderBtn;
        private Button ExplorerTerminalButton => LeftSidebarTabView.ExplorerTerminalBtn;
        private TabView RightTabView => PreviewGrid.RightTabs;
        private ComboBox PreviewModeCombo => PreviewGrid.PreviewMode;
        private WebView2 PreviewWebView => PreviewGrid.PreviewWebViewControl;
        private TextBlock LlmOutputText => PreviewGrid.LlmOutput;
        private TextBlock SelectionStatsText => PreviewGrid.SelectionStats;
        private TextBox LlmFileContextInput => PreviewGrid.LlmFileContext;
        private TextBox LlmCustomPromptInput => PreviewGrid.LlmCustomPrompt;

        public MainWindow()
        {
            this.InitializeComponent();
            _activeTabView = EditorTabView;
            SetWindowIcon();

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

            if (Content is FrameworkElement rootElement)
            {
                rootElement.DataContext = _viewModel;
            }

            // Bind Left Sidebar Tab items
            FileListView.ItemsSource = _viewModel.ExplorerItems;
            FavoritesListView.ItemsSource = _viewModel.Favorites;
            RecentFilesListView.ItemsSource = _viewModel.RecentFiles;
            SnippetsListView.ItemsSource = _viewModel.Snippets;
            GitChangedFilesList.ItemsSource = _viewModel.GitFiles;
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
            WireLeftSidebarEvents();
            WireRightSidebarEvents();
            TerminalPane.AttachOwner(this);
            TerminalPane.WorkingDirectoryProvider = GetTerminalWorkingDirectory;
            TerminalPane.SessionsEmptied += OnTerminalPaneSessionsEmptied;

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

            // Initialize Git Auto-Refresh Timer (30 second interval)
            _gitAutoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _gitAutoRefreshTimer.Tick += OnGitAutoRefreshTimerTick;

            // Load local configurations and boot initial states
            // Setup custom title bar
            SetupCustomTitleBar();

            this.Activated += OnWindowActivated;
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
            LeftSidebarTabView.AddFileToFavoritesClick += OnAddFileToFavoritesClick;
            LeftSidebarTabView.AddFolderToFavoritesClick += OnAddFolderToFavoritesClick;
            LeftSidebarTabView.FavoriteItemDoubleTapped += OnFavoriteItemDoubleTapped;
            LeftSidebarTabView.RemoveFavoriteClick += OnRemoveFavoriteClick;
            LeftSidebarTabView.FavoritePinClick += OnFavoritePinClick;
            LeftSidebarTabView.FavoritesTabClick += OnFavoritesTabClick;
            LeftSidebarTabView.GitHistoryItemDoubleTapped += OnGitHistoryItemDoubleTapped;
            LeftSidebarTabView.SnippetItemDoubleTapped += OnSnippetItemDoubleTapped;
            LeftSidebarTabView.DeleteSnippetClick += OnDeleteSnippetClick;
            LeftSidebarTabView.AddSnippetClick += OnAddSnippetClick;
            LeftSidebarTabView.GitFileDoubleTapped += OnGitFileDoubleTapped;
            LeftSidebarTabView.GitStageToggleClick += OnGitStageToggleClick;
            LeftSidebarTabView.GitRestoreFileClick += OnGitRestoreFileClick;
            LeftSidebarTabView.GitCommitClick += OnGitCommitClick;
            LeftSidebarTabView.GitStageAllClick += OnGitStageAllClick;
            LeftSidebarTabView.GitRestoreAllClick += OnGitRestoreAllClick;
            LeftSidebarTabView.GitPushClick += OnGitPushClick;
            LeftSidebarTabView.GitRefreshClick += OnGitRefreshClick;
            LeftSidebarTabView.SearchQueryInputKeyDown += OnSearchQueryInputKeyDown;
            LeftSidebarTabView.SearchAllFilesClick += OnSearchAllFilesClick;
            LeftSidebarTabView.ReplaceAllClick += OnReplaceAllClick;
            LeftSidebarTabView.SearchResultDoubleTapped += OnSearchResultDoubleTapped;
            LeftSidebarTabView.RecentFileItemDoubleTapped += OnRecentFileItemDoubleTapped;
            LeftSidebarTabView.RemoveRecentFileClick += OnRemoveRecentFileClick;
        }

        private void WireRightSidebarEvents()
        {
            PreviewGrid.PreviewModeSelectionChanged += OnPreviewModeComboSelectionChanged;
            PreviewGrid.OpenPreviewInBrowserClick += OnOpenPreviewInBrowserClick;
            PreviewGrid.LlmAddFileContextClick += OnLlmAddFileContextClick;
            PreviewGrid.LlmExplainClick += OnLlmExplainClick;
            PreviewGrid.LlmSummarizeClick += OnLlmSummarizeClick;
            PreviewGrid.LlmTranslateClick += OnLlmTranslateClick;
            PreviewGrid.LlmImproveClick += OnLlmImproveClick;
            PreviewGrid.LlmCustomClick += OnLlmCustomClick;
            PreviewGrid.LlmInsertOutputClick += OnLlmInsertOutputClick;
        }

        private void SetupCustomTitleBar()
        {
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            TerminalPane.StopAllSessions();
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

        private void ApplySavedWindowPlacement(EditorSettings settings)
        {
            try
            {
                if (settings.WindowWidth < 400 || settings.WindowHeight < 300)
                {
                    return;
                }

                var size = new SizeInt32(settings.WindowWidth, settings.WindowHeight);
                if (settings.WindowX >= 0 && settings.WindowY >= 0)
                {
                    AppWindow.MoveAndResize(new RectInt32(settings.WindowX, settings.WindowY, size.Width, size.Height));
                }
                else
                {
                    AppWindow.Resize(size);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to restore window placement: {ex.Message}");
            }
        }

        private async Task SaveUiLayoutSettingsAsync()
        {
            try
            {
                var settings = _settingsService.CurrentSettings;
                var position = AppWindow.Position;
                var size = AppWindow.Size;

                var overlappedPresenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
                bool isRestored = overlappedPresenter == null || overlappedPresenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Restored;

                if (isRestored && size.Width >= 400 && size.Height >= 300)
                {
                    settings.WindowX = position.X;
                    settings.WindowY = position.Y;
                    settings.WindowWidth = size.Width;
                    settings.WindowHeight = size.Height;
                }

                if (TerminalPane.Visibility == Visibility.Visible && TerminalPanelRow.Height.Value > 0)
                {
                    settings.TerminalPanelHeight = TerminalPanelRow.Height.Value;
                }
                else
                {
                    settings.TerminalPanelHeight = _lastTerminalHeight;
                }

                settings.LeftSidebarVisible = LeftSidebarTabView.Visibility == Visibility.Visible;
                settings.RightSidebarVisible = PreviewGrid.Visibility == Visibility.Visible;

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
                settings.LeftSidebarVisible = LeftSidebarTabView.Visibility == Visibility.Visible;
                settings.RightSidebarVisible = PreviewGrid.Visibility == Visibility.Visible;
                await _settingsService.SaveSettingsAsync(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save sidebar visibility settings: {ex.Message}");
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
            ApplySavedWindowPlacement(_settingsService.CurrentSettings);
            _lastTerminalHeight = Math.Clamp(_settingsService.CurrentSettings.TerminalPanelHeight, 120, 600);

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

                    long thresholdBytes = _settingsService.CurrentSettings.LargeFileThresholdMB * 1024 * 1024;
                    if (fileSizeBytes >= thresholdBytes)
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
                                var readResult = await _fileService.ReadTextFileAsync(filePath, "Auto");
                                this.DispatcherQueue.TryEnqueue(async () =>
                                {
                                    var tab = _viewModel.Tabs.FirstOrDefault(t => t.FilePath == filePath);
                                    if (tab != null)
                                    {
                                        tab.Content = readResult.Content;
                                        tab.EncodingName = readResult.EncodingName;
                                        tab.EncodingWasAutoDetected = readResult.WasAutoDetected;
                                        if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                                        {
                                            await bridgeGroup.Bridge.SetTextAsync(readResult.Content);
                                        }
                                        SyncEncodingCombo(tab);
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

            // 2. Apply settings to UI and initialize preview panel WebView2 in the background
            WordWrapToggle.IsChecked = _settingsService.CurrentSettings.WordWrap;
            LeftPanelToggle.IsChecked = _settingsService.CurrentSettings.LeftSidebarVisible;
            ApplyLeftSidebarVisibility(_settingsService.CurrentSettings.LeftSidebarVisible);
            bool rightPanelVisible = _settingsService.CurrentSettings.RightSidebarVisible && _settingsService.CurrentSettings.DefaultMarkdownEnabled;
            RightPanelToggle.IsChecked = rightPanelVisible;
            ApplyPreviewVisibility(rightPanelVisible);
            MarkdownToolbarToggle.IsChecked = _settingsService.CurrentSettings.DefaultMarkdownToolbarEnabled;
            MarkdownToolbar.Visibility = _settingsService.CurrentSettings.DefaultMarkdownToolbarEnabled ? Visibility.Visible : Visibility.Collapsed;
            PreviewModeCombo.SelectedIndex = _settingsService.CurrentSettings.PreviewMode switch
            {
                "HTML" => 1,
                "LaTeX" => 2,
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
            await _snippetService.LoadSnippetsAsync();
            RefreshSnippetsUI();
            RefreshFavoritesUI();
            _recentFilesService.LoadInto(_viewModel.RecentFiles);
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

        private void OpenNewTab(string? filePath = null, string content = "", bool isLargeFileMode = false, bool isMonacoLimitedMode = false, bool isReadOnly = false, string encodingName = "UTF-8", bool encodingWasAutoDetected = true)
        {
            var tab = new OpenedTab();
            tab.IsLargeFileMode = isLargeFileMode;
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
                    AddRecentFile(filePath);
                }
            }
            else
            {
                tab.Title = "제목 없음";
                tab.Content = "";
            }

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
                                ToggleTerminal();
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
                    if (GetActiveTab() == tab)
                    {
                        StatusLine.Text = line.ToString();
                        StatusCol.Text = col.ToString();
                        _ = bridge.RequestSelectionAsync(); // Auto sync selection on cursor move
                    }
                };

                bridge.SelectionReceived += (selectedText) =>
                {
                    _lastSelectionText = selectedText;
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

                // Initialize editor inside WebView2 using virtual host mappings
                InitializeEditorWebView(editorWebView, bridge);
            }

            targetTabView.TabItems.Add(tabItem);
            targetTabView.SelectedItem = tabItem;

            UpdateStatusFileStats(tab);
            SyncEncodingCombo(tab);
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

                    StatusMode.Text = GetLocalizedString("StatusModeLargeFile", "대용량 모드");
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
                        StatusMode.Text = GetLocalizedString("StatusModeLargeFile", "대용량 모드");
                        OpenNewTab(filePath, "", isLargeFileMode: true);
                        return;
                    }
                    else if (result == ContentDialogResult.Secondary)
                    {
                        StatusMode.Text = GetLocalizedString("StatusModeLimited", "일반 모드 (제한)");
                        var readResult = await _fileService.ReadTextFileAsync(filePath, "Auto");
                        OpenNewTab(filePath, readResult.Content, isLargeFileMode: false, isMonacoLimitedMode: true, encodingName: readResult.EncodingName, encodingWasAutoDetected: readResult.WasAutoDetected);
                        return;
                    }
                    else
                    {
                        return; // Canceled
                    }
                }

                StatusMode.Text = GetLocalizedString("StatusModeNormal", "일반 모드");
                var normalReadResult = await _fileService.ReadTextFileAsync(filePath, "Auto");
                OpenNewTab(filePath, normalReadResult.Content, encodingName: normalReadResult.EncodingName, encodingWasAutoDetected: normalReadResult.WasAutoDetected);
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

        private void OnTopMostToggleClick(object sender, RoutedEventArgs e)
        {
            bool isTopMost = TopMostToggleButton?.IsChecked == true;
            _stickyNoteService.ApplyTopMost(this, isTopMost);
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
            UIElement[] pages =
            {
                ExplorerSidebarPage,
                FavoritesSidebarPage,
                RecentSidebarPage,
                SearchSidebarPage,
                GitSidebarPage,
                SnippetsSidebarPage
            };

            Microsoft.UI.Xaml.Controls.Primitives.ToggleButton[] buttons =
            {
                ExplorerActivityButton,
                FavoritesActivityButton,
                RecentActivityButton,
                SearchActivityButton,
                GitActivityButton,
                SnippetsActivityButton
            };

            int safeIndex = Math.Clamp(index, 0, pages.Length - 1);
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Visibility = i == safeIndex ? Visibility.Visible : Visibility.Collapsed;
                buttons[i].IsChecked = i == safeIndex;
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
            if (LeftPanelToggle.IsChecked == true && LeftSidebarTabView.Visibility == Visibility.Visible)
            {
                return;
            }

            LeftPanelToggle.IsChecked = true;
            ApplyLeftSidebarVisibility(true);
            _ = SaveSidebarVisibilitySettingsAsync();
        }

        private void ApplyLeftSidebarVisibility(bool show)
        {
            ExplorerColumn.MinWidth = ExplorerPanelMinWidth;
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

        private string GetLocalizedString(string key, string fallback)
        {
            string reswValue = GetLocalizedStringFromResw(key);
            if (!string.IsNullOrEmpty(reswValue))
            {
                return reswValue;
            }

            try
            {
                EnsureResourceContext();
                string value = _resourceContext == null
                    ? new ResourceLoader().GetString(key)
                    : _resourceManager.MainResourceMap.GetSubtree("Resources").GetValue(key, _resourceContext).ValueAsString;
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Missing localized string '{key}': {ex.Message}");
                return fallback;
            }
        }

        private string GetLocalizedStringFromResw(string key)
        {
            try
            {
                string language = GetActiveLanguage();
                var strings = GetReswStrings(language);
                if (strings.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
                {
                    return value;
                }

                if (!language.Equals("en-US", StringComparison.OrdinalIgnoreCase) &&
                    GetReswStrings("en-US").TryGetValue(key, out value) &&
                    !string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read localized resw string '{key}': {ex.Message}");
            }

            return string.Empty;
        }

        private Dictionary<string, string> GetReswStrings(string language)
        {
            if (_reswStringCache.TryGetValue(language, out var strings))
            {
                return strings;
            }

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Strings", language, "Resources.resw");
            if (!File.Exists(path))
            {
                path = Path.Combine(AppContext.BaseDirectory, "Strings", language, "Resources.resw");
            }

            strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path))
            {
                var doc = XDocument.Load(path);
                foreach (var data in doc.Root?.Elements("data") ?? Enumerable.Empty<XElement>())
                {
                    string? name = data.Attribute("name")?.Value;
                    string? value = data.Element("value")?.Value;
                    if (!string.IsNullOrWhiteSpace(name) && value != null)
                    {
                        strings[name] = value;
                    }
                }
            }

            _reswStringCache[language] = strings;
            return strings;
        }

        private string GetActiveLanguage()
        {
            var lang = _settingsService?.CurrentSettings?.Language;
            if (string.IsNullOrEmpty(lang) || lang.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    lang = System.Globalization.CultureInfo.CurrentUICulture.Name;
                }
                catch
                {
                    lang = "en-US";
                }
            }

            if (lang != null)
            {
                if (lang.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko-KR";
                if (lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja-JP";
            }
            return "en-US";
        }

        private void ApplyResourceLanguage()
        {
            try
            {
                string configuredLanguage = _settingsService?.CurrentSettings?.Language ?? "Default";
                string activeLanguage = GetActiveLanguage();
                ApplicationLanguages.PrimaryLanguageOverride = configuredLanguage.Equals("Default", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : activeLanguage;

                var culture = new System.Globalization.CultureInfo(activeLanguage);
                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;

                _resourceManager = new ResourceManager();
                _resourceContext = _resourceManager.CreateResourceContext();
                _resourceContext.QualifierValues["Language"] = activeLanguage;
                _reswStringCache.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply resource language: {ex.Message}");
            }
        }

        private void EnsureResourceContext()
        {
            if (_resourceContext == null)
            {
                ApplyResourceLanguage();
            }
        }
        private void LocalizeUi()
        {
            try
            {
                ApplyResourceLanguage();
                string GetString(string key, string fallback) => GetLocalizedString(key, fallback);

                // 1. Top Toolbar Buttons
                if (OpenFileButton != null)
                {
                    OpenFileButton.Label = GetString("OpenFile", "파일 열기");
                    ToolTipService.SetToolTip(OpenFileButton, GetString("OpenFile", "파일 열기") + " (Ctrl+O)");
                }
                if (SaveFileButton != null)
                {
                    SaveFileButton.Label = GetString("SaveFile", "저장");
                    ToolTipService.SetToolTip(SaveFileButton, GetString("SaveFile", "저장") + " (Ctrl+S)");
                }
                if (CompareButton != null)
                {
                    CompareButton.Label = GetString("Compare", "비교");
                    ToolTipService.SetToolTip(CompareButton, GetString("Compare", "비교") + " (Diff)");
                }
                if (TerminalToggleButton != null)
                {
                    TerminalToggleButton.Label = GetString("Terminal", "터미널");
                    ToolTipService.SetToolTip(TerminalToggleButton, GetString("Terminal", "터미널") + " (Ctrl+`)");
                }
                if (TopMostToggleButton != null)
                {
                    TopMostToggleButton.Label = GetString("TopMost", "항상위");
                    ToolTipService.SetToolTip(TopMostToggleButton, GetString("TopMost", "항상 위"));
                }
                if (StickyNoteButton != null)
                {
                    StickyNoteButton.Label = GetString("StickyNote", "스티커");
                    ToolTipService.SetToolTip(StickyNoteButton, GetString("StickyNote", "스티커 노트"));
                }
                if (WordWrapToggle != null)
                {
                    WordWrapToggle.Label = GetString("WordWrap", "Word Wrap");
                }
                if (SearchButton != null)
                {
                    SearchButton.Label = GetString("Search", "검색");
                    ToolTipService.SetToolTip(SearchButton, GetString("Search", "검색") + " (Ctrl+F)");
                }
                if (MarkdownToolbarToggle != null)
                {
                    MarkdownToolbarToggle.Label = GetString("Markdown", "Markdown");
                    ToolTipService.SetToolTip(MarkdownToolbarToggle, GetString("Markdown", "마크다운 툴바 토글"));
                }
                if (ThemeButton != null)
                {
                    ThemeButton.Label = GetString("Theme", "테마");
                }
                if (SplitButton != null)
                {
                    SplitButton.Label = GetString("Split", "분할");
                    ToolTipService.SetToolTip(SplitButton, GetString("Split", "에디터 화면 분할"));
                }
                if (SettingsButton != null)
                {
                    SettingsButton.Label = GetString("Settings", "설정");
                }
                if (PrintButton != null)
                {
                    PrintButton.Label = GetString("Print", "인쇄");
                    ToolTipService.SetToolTip(PrintButton, GetString("Print", "인쇄") + " (Ctrl+P)");
                }

                // 2. Split Menu Flyouts
                if (SplitNoneItem != null) SplitNoneItem.Text = GetString("SplitNone", "분할 없음 (단일)");
                if (SplitVerticalItem != null) SplitVerticalItem.Text = GetString("SplitVertical", "좌우 분할");
                if (SplitHorizontalItem != null) SplitHorizontalItem.Text = GetString("SplitHorizontal", "상하 분할");

                // 3. Left Panel Tooltips
                if (ExplorerActivityButton != null) ToolTipService.SetToolTip(ExplorerActivityButton, GetString("Explorer", "탐색기"));
                if (FavoritesActivityButton != null) ToolTipService.SetToolTip(FavoritesActivityButton, GetString("Favorites", "즐겨찾기"));
                if (SnippetsActivityButton != null) ToolTipService.SetToolTip(SnippetsActivityButton, GetString("Snippets", "스니펫"));
                if (GitActivityButton != null) ToolTipService.SetToolTip(GitActivityButton, GetString("Git", "Git"));
                if (SearchActivityButton != null) ToolTipService.SetToolTip(SearchActivityButton, GetString("Search", "검색"));
                if (RecentActivityButton != null) ToolTipService.SetToolTip(RecentActivityButton, GetString("RecentFiles", "최근 파일"));

                // 4. Folder Select and Status
                if (ExplorerStatusText != null && string.IsNullOrEmpty(_currentFolderPath))
                {
                    ExplorerStatusText.Text = GetString("NoFolderSelected", "폴더를 선택하세요.");
                }

                // 5. Sidebar Headers
                if (FavoritesHeaderText != null) FavoritesHeaderText.Text = GetString("FavoritesHeader", "즐겨찾기 목록");
                if (SnippetsHeaderText != null) SnippetsHeaderText.Text = GetString("SnippetsHeader", "코드 및 수식 템플릿");
                if (AddSnippetButton != null) AddSnippetButton.Content = GetString("AddSnippet", "스니펫 추가...");

                // 6a. Explorer folder buttons
                if (ExplorerUpButton != null) ToolTipService.SetToolTip(ExplorerUpButton, GetString("ExplorerUpTooltip", "상위 폴더"));
                if (ExplorerSelectFolderButton != null) ExplorerSelectFolderButton.Content = GetString("ExplorerSelectFolder", "폴더 선택...");
                if (ExplorerTerminalButton != null) ToolTipService.SetToolTip(ExplorerTerminalButton, GetString("ExplorerOpenTerminalTooltip", "현재 폴더에서 터미널 열기"));

                // 6b. Favorites tabs and pins
                if (LeftSidebarTabView.FavoritesFileTabButton != null) LeftSidebarTabView.FavoritesFileTabButton.Content = GetString("FavoritesFileTab", "파일");
                if (LeftSidebarTabView.FavoritesFolderTabButton != null) LeftSidebarTabView.FavoritesFolderTabButton.Content = GetString("FavoritesFolderTab", "폴더");
                if (LeftSidebarTabView.FavoritesPinIndicatorText != null) ToolTipService.SetToolTip(LeftSidebarTabView.FavoritesPinIndicatorText, GetString("FavoritesPinTooltip", "고정"));

                // 6c. Recent Files header
                if (RecentFilesHeaderText != null) RecentFilesHeaderText.Text = GetString("RecentFilesHeader", "최근 파일");

                // 6d. Search panel
                if (SearchHeaderText != null) SearchHeaderText.Text = GetString("SearchHeader", "폴더 전체 검색 및 치환");
                if (SearchQueryInput != null) SearchQueryInput.PlaceholderText = GetString("SearchPlaceholder", "검색어 입력...");
                if (ReplaceQueryInput != null) ReplaceQueryInput.PlaceholderText = GetString("ReplacePlaceholder", "치환할 단어 입력...");
                if (SearchMatchCaseToggle != null) ToolTipService.SetToolTip(SearchMatchCaseToggle, GetString("SearchMatchCaseTooltip", "대소문자 구분"));
                if (SearchWholeWordToggle != null) ToolTipService.SetToolTip(SearchWholeWordToggle, GetString("SearchWholeWordTooltip", "단어 단위"));
                if (SearchRegexToggle != null) ToolTipService.SetToolTip(SearchRegexToggle, GetString("SearchRegexTooltip", "정규식 검색"));
                if (SearchAllButton != null) SearchAllButton.Content = GetString("SearchAllFiles", "전체 검색");
                if (ReplaceAllButton != null) ReplaceAllButton.Content = GetString("ReplaceAllFiles", "모두 치환");

                // 6e. Git panel
                if (GitHeaderText != null) GitHeaderText.Text = GetString("GitRepoHeader", "Git 저장소 관리");
                if (GitBranchesCombo != null) GitBranchesCombo.PlaceholderText = GetString("GitBranchPlaceholder", "브랜치 목록");
                if (GitCommitMessageInput != null) GitCommitMessageInput.PlaceholderText = GetString("GitCommitPlaceholder", "커밋 메시지 입력...");
                if (GitCommitButton != null) GitCommitButton.Content = GetString("GitCommit", "커밋 (Commit)");
                if (GitStageAllButton != null) GitStageAllButton.Content = GetString("GitStageAll", "전체 Stage");
                if (GitRestoreAllButton != null) GitRestoreAllButton.Content = GetString("GitRestoreAll", "전체 Restore");
                if (GitPushButton != null) GitPushButton.Content = GetString("GitPush", "Push");
                if (GitRefreshButton != null) GitRefreshButton.Content = GetString("GitRefresh", "새로고침");
                if (GitHistoryHeader != null) GitHistoryHeader.Text = GetString("GitHistory", "과거 기록");

                // 6f. Markdown Toolbar Buttons
                MarkdownToolbar.LocalizeTooltips(GetString);

                if (StatusLineLabel != null) StatusLineLabel.Text = GetString("StatusLineLabel", "줄");
                if (StatusColumnLabel != null) StatusColumnLabel.Text = GetString("StatusColumnLabel", "열");
                if (StatusMode != null && IsNormalModeText(StatusMode.Text)) StatusMode.Text = GetString("StatusModeNormal", "일반 모드");
                if (StatusGitBranch != null && IsGitNotDetectedText(StatusGitBranch.Text)) StatusGitBranch.Text = GetString("GitNotDetected", "Git: 감지 안됨");
                if (GitPanelBranchText != null && IsGitNotDetectedText(GitPanelBranchText.Text)) GitPanelBranchText.Text = GetString("GitNotDetected", "Git: 감지 안됨");
                ToolTipService.SetToolTip(LeftPanelToggle, GetString("StatusLeftPanelTooltip", "좌측 패널"));
                ToolTipService.SetToolTip(RightPanelToggle, GetString("StatusRightPanelTooltip", "우측 패널"));
                ToolTipService.SetToolTip(StatusBarPane.LineNumberButtonControl, GetString("StatusGoToLineTooltip", "클릭하여 줄 이동"));
                ToolTipService.SetToolTip(StatusBarPane.LineEndingButtonControl, GetString("StatusLineEndingTooltip", "클릭하여 줄 끝 방식 변경"));
                ToolTipService.SetToolTip(StatusEncodingCombo, GetString("StatusEncodingTooltip", "파일 인코딩 선택"));
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

        private static bool IsNormalModeText(string text)
        {
            return text.Equals("일반 모드", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("Normal Mode", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("通常モード", StringComparison.OrdinalIgnoreCase);
        }

        private async void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            // Suspend native terminal windows so settings dialog is not hidden behind them
            bool terminalWasVisible = TerminalPane.Visibility == Visibility.Visible;
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
                LlmOutputText.Text = result.ApiKeyStatusMessage;
            }

            await _settingsService.SaveSettingsAsync(settings);
            ApplyResourceLanguage();
            ApplyPreviewVisibility(settings.DefaultMarkdownEnabled);
            MarkdownToolbarToggle.IsChecked = settings.DefaultMarkdownToolbarEnabled;
            MarkdownToolbar.Visibility = settings.DefaultMarkdownToolbarEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Enable auto-save if setting is on and git is available
            _autoSaveEnabled = settings.AutoSave && !string.IsNullOrEmpty(_currentRepoPath);
            if (_autoSaveEnabled) _autoSaveTimer.Start();
            else _autoSaveTimer.Stop();
            WordWrapToggle.IsChecked = settings.WordWrap;
            ApplyUiPersonalization(settings);
            LocalizeUi();
            ApplyToolbarSettings(settings);

            if (oldLanguage != settings.Language && await ConfirmRestartForLanguageChangeAsync(GetSettingsString))
            {
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

        private void OnTerminalSplitterPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is UIElement splitter && TerminalPane.Visibility == Visibility.Visible)
            {
                _isDraggingTerminalSplitter = true;
                _terminalSplitterStartHeight = TerminalPanelRow.Height.Value > 0 ? TerminalPanelRow.Height.Value : _lastTerminalHeight;
                var pt = e.GetCurrentPoint(MainWorkGrid).Position;
                _terminalSplitterStartPointerY = pt.Y;
                splitter.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnTerminalSplitterPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isDraggingTerminalSplitter)
            {
                var pt = e.GetCurrentPoint(MainWorkGrid).Position;
                double deltaY = _terminalSplitterStartPointerY - pt.Y;
                double maxHeight = Math.Max(160, MainWorkGrid.ActualHeight - 180);
                double newHeight = Math.Clamp(_terminalSplitterStartHeight + deltaY, 120, maxHeight);
                _lastTerminalHeight = newHeight;
                TerminalPanelRow.Height = new GridLength(newHeight);
                TerminalPane.ResizeEmbeddedTerminal();
                e.Handled = true;
            }
        }

        private async void OnTerminalSplitterPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isDraggingTerminalSplitter && sender is UIElement splitter)
            {
                _isDraggingTerminalSplitter = false;
                splitter.ReleasePointerCapture(e.Pointer);
                await SaveUiLayoutSettingsAsync();
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
            _viewModel.ExplorerItems.Clear();
            _currentFolderPath = folderPath;

            foreach (var item in CreateDirectoryItems(folderPath))
            {
                _viewModel.ExplorerItems.Add(item);
            }

            ExplorerStatusText.Text = $"{folderPath}\n{_viewModel.ExplorerItems.Count:N0}개 항목";
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

        private void EnsureTerminalPanelVisible()
        {
            TerminalPane.Visibility = Visibility.Visible;
            TerminalSplitter.Visibility = Visibility.Visible;
            TerminalSplitterRow.Height = new GridLength(4);
            TerminalPanelRow.Height = new GridLength(Math.Clamp(_lastTerminalHeight, 120, Math.Max(160, MainWorkGrid.ActualHeight - 180)));
        }

        private void ToggleTerminal()
        {
            if (TerminalPane.Visibility == Visibility.Visible)
            {
                if (TerminalPane.HasSessions)
                    TerminalPane.StopAllSessions();
                TerminalPane.Visibility = Visibility.Collapsed;
                TerminalSplitter.Visibility = Visibility.Collapsed;
                TerminalSplitterRow.Height = new GridLength(0);
                TerminalPanelRow.Height = new GridLength(0);
                if (TerminalToggleButton != null)
                    TerminalToggleButton.IsChecked = false;
            }
            else
            {
                string workingDirectory = GetTerminalWorkingDirectory();
                if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
                    workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                EnsureTerminalPanelVisible();
                TerminalPane.OpenTerminal(workingDirectory);
                if (TerminalToggleButton != null) TerminalToggleButton.IsChecked = true;
            }
        }

        private void OnTerminalPaneSessionsEmptied(object? sender, EventArgs e)
        {
            HideTerminalPanel();
        }

        private void HideTerminalPanel()
        {
            if (TerminalPane.HasSessions)
            {
                return;
            }

            TerminalPane.Visibility = Visibility.Collapsed;
            TerminalSplitter.Visibility = Visibility.Collapsed;
            TerminalSplitterRow.Height = new GridLength(0);
            TerminalPanelRow.Height = new GridLength(0);
            if (TerminalToggleButton != null)
            {
                TerminalToggleButton.IsChecked = false;
            }
        }

        #endregion

        #region Split Editor Layout

        private void OnTabViewGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TabView tabView)
            {
                _activeTabView = tabView;
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
            return _activeTabView ?? EditorTabView;
        }

        private void OnSplitNoneClick(object sender, RoutedEventArgs e) => SetSplitMode(SplitMode.None);
        private void OnSplitVerticalClick(object sender, RoutedEventArgs e) => SetSplitMode(SplitMode.Vertical);
        private void OnSplitHorizontalClick(object sender, RoutedEventArgs e) => SetSplitMode(SplitMode.Horizontal);

        private void SetSplitMode(SplitMode mode)
        {
            _currentSplitMode = mode;

            if (mode == SplitMode.None)
            {
                EditorRow1.Height = new GridLength(1, GridUnitType.Star);
                EditorRow2.Height = new GridLength(0);
                EditorSplitterRow.Height = new GridLength(0);

                EditorColumn1.Width = new GridLength(1, GridUnitType.Star);
                EditorColumn2.Width = new GridLength(0);
                EditorSplitterColumn.Width = new GridLength(0);

                EditorSplitter.Visibility = Visibility.Collapsed;
                EditorTabView2.Visibility = Visibility.Collapsed;

                while (EditorTabView2.TabItems.Count > 0)
                {
                    var item = (TabViewItem)EditorTabView2.TabItems[0];
                    EditorTabView2.TabItems.RemoveAt(0);
                    EditorTabView.TabItems.Add(item);
                }

                _activeTabView = EditorTabView;
                if (EditorTabView.SelectedItem == null && EditorTabView.TabItems.Count > 0)
                {
                    EditorTabView.SelectedIndex = 0;
                }
            }
            else if (mode == SplitMode.Vertical)
            {
                _isVerticalSplit = true;

                EditorRow1.Height = new GridLength(1, GridUnitType.Star);
                EditorRow2.Height = new GridLength(0);
                EditorSplitterRow.Height = new GridLength(0);

                double totalWidth = EditorSplitGrid.ActualWidth;
                double halfWidth = (totalWidth - 4) / 2;
                if (halfWidth < 100) halfWidth = 300;
                EditorColumn1.Width = new GridLength(halfWidth);
                EditorColumn2.Width = new GridLength(halfWidth);
                EditorSplitterColumn.Width = new GridLength(4);

                Grid.SetRow(EditorSplitter, 0);
                Grid.SetRowSpan(EditorSplitter, 1);
                Grid.SetColumn(EditorSplitter, 1);
                Grid.SetColumnSpan(EditorSplitter, 1);
                EditorSplitter.Width = 4;
                EditorSplitter.Height = double.NaN;
                EditorSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
                EditorSplitter.VerticalAlignment = VerticalAlignment.Stretch;

                Grid.SetRow(EditorTabView2, 0);
                Grid.SetColumn(EditorTabView2, 2);

                EditorSplitter.Visibility = Visibility.Visible;
                EditorTabView2.Visibility = Visibility.Visible;

                if (EditorTabView2.TabItems.Count == 0)
                {
                    if (EditorTabView.TabItems.Count > 1)
                    {
                        var activeItem = (TabViewItem)EditorTabView.SelectedItem;
                        if (activeItem != null)
                        {
                            EditorTabView.TabItems.Remove(activeItem);
                            EditorTabView2.TabItems.Add(activeItem);
                            EditorTabView2.SelectedItem = activeItem;
                        }
                    }
                    else
                    {
                        _activeTabView = EditorTabView2;
                        OpenNewTab();
                    }
                }
            }
            else if (mode == SplitMode.Horizontal)
            {
                _isVerticalSplit = false;

                double totalHeight = EditorSplitGrid.ActualHeight;
                double halfHeight = (totalHeight - 4) / 2;
                if (halfHeight < 100) halfHeight = 300;
                EditorRow1.Height = new GridLength(halfHeight);
                EditorRow2.Height = new GridLength(halfHeight);
                EditorSplitterRow.Height = new GridLength(4);

                EditorColumn1.Width = new GridLength(1, GridUnitType.Star);
                EditorColumn2.Width = new GridLength(0);
                EditorSplitterColumn.Width = new GridLength(0);

                Grid.SetRow(EditorSplitter, 1);
                Grid.SetRowSpan(EditorSplitter, 1);
                Grid.SetColumn(EditorSplitter, 0);
                Grid.SetColumnSpan(EditorSplitter, 1);
                EditorSplitter.Width = double.NaN;
                EditorSplitter.Height = 4;
                EditorSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
                EditorSplitter.VerticalAlignment = VerticalAlignment.Stretch;

                Grid.SetRow(EditorTabView2, 2);
                Grid.SetColumn(EditorTabView2, 0);

                EditorSplitter.Visibility = Visibility.Visible;
                EditorTabView2.Visibility = Visibility.Visible;

                if (EditorTabView2.TabItems.Count == 0)
                {
                    if (EditorTabView.TabItems.Count > 1)
                    {
                        var activeItem = (TabViewItem)EditorTabView.SelectedItem;
                        if (activeItem != null)
                        {
                            EditorTabView.TabItems.Remove(activeItem);
                            EditorTabView2.TabItems.Add(activeItem);
                            EditorTabView2.SelectedItem = activeItem;
                        }
                    }
                    else
                    {
                        _activeTabView = EditorTabView2;
                        OpenNewTab();
                    }
                }
            }
        }

        private void OnEditorSplitterPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is UIElement splitter)
            {
                _isDraggingEditorSplitter = true;
                _editorSplitterStartWidth = EditorColumn1.Width.Value;
                _editorSplitterStartHeight = EditorRow1.Height.Value;
                var pt = e.GetCurrentPoint(EditorSplitGrid).Position;
                _editorSplitterStartPointerX = pt.X;
                _editorSplitterStartPointerY = pt.Y;
                splitter.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnEditorSplitterPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isDraggingEditorSplitter && sender is UIElement splitter)
            {
                var pt = e.GetCurrentPoint(EditorSplitGrid).Position;
                if (_isVerticalSplit)
                {
                    double deltaX = pt.X - _editorSplitterStartPointerX;
                    double newWidth = _editorSplitterStartWidth + deltaX;
                    double totalWidth = EditorSplitGrid.ActualWidth;
                    newWidth = Math.Clamp(newWidth, 100, totalWidth - 100);
                    EditorColumn1.Width = new GridLength(newWidth);
                    EditorColumn2.Width = new GridLength(totalWidth - newWidth - 4);
                }
                else
                {
                    double deltaY = pt.Y - _editorSplitterStartPointerY;
                    double newHeight = _editorSplitterStartHeight + deltaY;
                    double totalHeight = EditorSplitGrid.ActualHeight;
                    newHeight = Math.Clamp(newHeight, 100, totalHeight - 100);
                    EditorRow1.Height = new GridLength(newHeight);
                    EditorRow2.Height = new GridLength(totalHeight - newHeight - 4);
                }
                e.Handled = true;
            }
        }

        private void OnEditorSplitterPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isDraggingEditorSplitter && sender is UIElement splitter)
            {
                _isDraggingEditorSplitter = false;
                splitter.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnEditorTabView2AddTabClick(TabView sender, object args)
        {
            _activeTabView = sender;
            OpenNewTab();
        }

        private void OnEditorTabView2TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            _activeTabView = sender;
            OnEditorTabViewTabCloseRequested(sender, args);
        }

        private async void OnEditorTabView2SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _activeTabView = EditorTabView2;
            if (EditorTabView2.SelectedItem is TabViewItem activeTabItem)
            {
                await HandleTabViewSelectionChangedAsync(activeTabItem);
            }
        }

        private async Task HandleTabViewSelectionChangedAsync(TabViewItem activeTabItem)
        {
            _lastSelectionText = string.Empty;
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

        #endregion

        #region Explorer Directory Items

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
                bridgeGroup.WebView.Close(); // Dispose webview resource
                _tabBridges.Remove(tab.Id);
            }

            if (EditorTabView.TabItems.Count == 0 && EditorTabView2.TabItems.Count == 0)
            {
                OpenNewTab();
            }
        }

        private async void OnEditorTabViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _activeTabView = EditorTabView;
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
                    await File.WriteAllTextAsync(targetPath, tab.Content ?? string.Empty, Encoding.UTF8);
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
            bool show = MarkdownToolbarToggle?.IsChecked == true;
            MarkdownToolbar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region Sidebar Favorite Commands

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

            if (tab.IsLargeFileMode)
            {
                tab.EncodingName = selectedEncoding == "Auto" ? tab.EncodingName : selectedEncoding;
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

                var readResult = await _fileService.ReadTextFileAsync(tab.FilePath, encodingName);
                tab.Content = readResult.Content;
                tab.EncodingName = readResult.EncodingName;
                tab.EncodingWasAutoDetected = readResult.WasAutoDetected;
                tab.IsDirty = false;

                var tabItem = EditorTabView.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id)
                           ?? EditorTabView2.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id);
                if (tabItem != null)
                {
                    tabItem.Header = tab.DisplayTitle;
                }

                if (_tabBridges.TryGetValue(tab.Id, out var bridgeGroup) && bridgeGroup.Bridge != null)
                {
                    await bridgeGroup.Bridge.SetTextAsync(readResult.Content);
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
                return _languageDetectionService.GetMonacoLanguageName(activeTab.FilePath);
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
            string context = BuildLlmContext(_lastSelectionText);
            await PreflightCheckAndRunAsync("선택 영역 설명 (Explain)", context,
                () => _llmService.ExplainCodeAsync(context, language));
        }

        private async void OnLlmSummarizeClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText))
            {
                ShowErrorMessage("AI 오류", "선택된 텍스트가 없습니다. 요약할 범위를 드래그하십시오.");
                return;
            }
            string context = BuildLlmContext(_lastSelectionText);
            await PreflightCheckAndRunAsync("선택 영역 요약 (Summarize)", context,
                () => _llmService.SummarizeTextAsync(context));
        }

        private async void OnLlmTranslateClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText))
            {
                ShowErrorMessage("AI 오류", "선택된 텍스트가 없습니다. 번역할 범위를 드래그하십시오.");
                return;
            }

            string context = BuildLlmContext(_lastSelectionText);
            await PreflightCheckAndRunAsync("선택 영역 번역 (Translate)", context,
                () => _llmService.TranslateTextAsync(context));
        }

        private async void OnLlmImproveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText))
            {
                ShowErrorMessage("AI 오류", "선택된 텍스트가 없습니다. 개선할 범위를 드래그하십시오.");
                return;
            }
            string context = BuildLlmContext(_lastSelectionText);
            await PreflightCheckAndRunAsync("수식 및 마크다운 개선", context,
                () => _llmService.CustomPromptAsync("제공된 텍스트의 가독성, 마크다운 형식, 또는 LaTeX 수학 공식을 표준 문법에 맞게 개선하여 한글로 정제해 주십시오.", context));
        }

        private async void OnLlmCustomClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSelectionText) && string.IsNullOrEmpty(_llmFileContextText))
            {
                ShowErrorMessage("AI 오류", "선택 영역이나 파일 맥락이 없습니다. 텍스트를 선택하거나 파일 맥락을 추가하십시오.");
                return;
            }
            string prompt = LlmCustomPromptInput.Text;
            if (string.IsNullOrEmpty(prompt))
            {
                ShowErrorMessage("AI 오류", "커스텀 지시사항 입력란이 비어 있습니다.");
                return;
            }

            string context = BuildLlmContext(_lastSelectionText);
            await PreflightCheckAndRunAsync("커스텀 지시사항 실행", context,
                () => _llmService.CustomPromptAsync(prompt, context));
        }

        private void OnLlmAddFileContextClick(object sender, RoutedEventArgs e)
        {
            var tab = GetActiveTab();
            if (tab == null)
            {
                ShowErrorMessage("AI 파일 맥락", "파일 맥락으로 추가할 활성 탭이 없습니다.");
                return;
            }

            string title = string.IsNullOrWhiteSpace(tab.FilePath) ? tab.Title : tab.FilePath;
            string content = tab.Content ?? string.Empty;
            if (tab.IsLargeFileMode)
            {
                ShowErrorMessage("AI 파일 맥락", "대용량 모드 파일은 전체 본문을 LLM 맥락으로 넣지 않습니다. 필요한 줄을 선택해서 사용하십시오.");
                return;
            }

            const int maxChars = 120_000;
            if (content.Length > maxChars)
            {
                content = content.Substring(0, maxChars) + "\n\n[파일 맥락이 길어 앞부분만 포함됨]";
            }

            _llmFileContextText = $"[파일 맥락: {title}]\n{content}";
            LlmFileContextInput.Text = $"{Path.GetFileName(title)} · {_llmFileContextText.Length:N0} 글자";
        }

        private async void OnLlmInsertOutputClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LlmOutputText.Text) || LlmOutputText.Text.StartsWith("대기 중", StringComparison.Ordinal))
            {
                ShowErrorMessage("AI 응답 입력", "입력할 AI 응답이 없습니다.");
                return;
            }

            if (EditorTabView.SelectedItem is TabViewItem activeTabItem &&
                activeTabItem.Tag is string tabId &&
                _tabBridges.TryGetValue(tabId, out var bridgeGroup) &&
                bridgeGroup.Bridge != null)
            {
                await bridgeGroup.Bridge.InsertTextAsync(LlmOutputText.Text);
            }
        }

        private string BuildLlmContext(string selectedText)
        {
            if (string.IsNullOrEmpty(_llmFileContextText))
            {
                return selectedText;
            }

            if (string.IsNullOrEmpty(selectedText))
            {
                return _llmFileContextText;
            }

            return $"{_llmFileContextText}\n\n[선택 영역]\n{selectedText}";
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
            StatusGitBranch.Text = IsGitNotDetectedText(branch) ? GetLocalizedString("GitNotDetected", "Git: 감지 안됨") : branch;
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
            RefreshFavoritesUI(null);
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

        private void AddRecentFile(string filePath)
        {
            DispatcherQueue.TryEnqueue(() => _recentFilesService.Add(_viewModel.RecentFiles, filePath));
        }

        private void OnRemoveRecentFileClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                _recentFilesService.Remove(_viewModel.RecentFiles, path);
            }
        }

        private async void OnRecentFileItemDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            var item = GetDataContextFromOriginalSource<RecentFileItem>(e.OriginalSource) ?? RecentFilesListView.SelectedItem as RecentFileItem;
            if (item != null)
            {
                if (File.Exists(item.Path))
                {
                    string? folderPath = Path.GetDirectoryName(item.Path);
                    if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
                    {
                        await NavigateExplorerToFolderAsync(folderPath);
                    }

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
            _viewModel.Snippets.Clear();
            var list = _snippetService.GetSnippets();
            foreach (var item in list)
            {
                _viewModel.Snippets.Add(item);
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
            _uiPersonalizationService.Apply(
                settings,
                AppWindow,
                Content as FrameworkElement,
                MarkdownToolbar.SetToolbarBackground);
        }

        private void ApplyToolbarSettings(EditorSettings settings)
        {
            bool showLabels = settings.ToolbarShowLabels;
            var hiddenSet = new HashSet<string>(
                (settings.ToolbarHiddenButtons ?? new List<string>()).Select(ToolbarButtonCatalog.NormalizeId),
                StringComparer.OrdinalIgnoreCase);
            var buttonsById = GetToolbarButtonsById();
            var orderedIds = NormalizeToolbarOrder(settings.ToolbarButtonOrder);

            TopCommandBar.PrimaryCommands.Clear();
            AddToolbarCommandsInOriginalGroups(orderedIds, buttonsById, hiddenSet);

            foreach (var (id, entry) in buttonsById)
            {
                bool isSettings = id.Equals("settings", StringComparison.OrdinalIgnoreCase);
                entry.Button.Visibility = isSettings || !hiddenSet.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
                string label = GetLocalizedString(entry.ResourceKey, id);
                string labelText = showLabels ? label : string.Empty;
                if (entry.Button is AppBarButton abb) abb.Label = labelText;
                else if (entry.Button is AppBarToggleButton atb) atb.Label = labelText;
            }
        }

        private void AddToolbarCommandsInOriginalGroups(
            IReadOnlyList<string> orderedIds,
            IReadOnlyDictionary<string, (FrameworkElement Button, string ResourceKey)> buttonsById,
            ISet<string> hiddenSet)
        {
            var groupLookup = ToolbarButtonCatalog.DefaultGroups
                .SelectMany((group, groupIndex) => group.Select(id => (id, groupIndex)))
                .ToDictionary(item => item.id, item => item.groupIndex, StringComparer.OrdinalIgnoreCase);
            int? lastGroupIndex = null;

            foreach (string id in orderedIds.Concat(new[] { "settings" }))
            {
                if (!buttonsById.TryGetValue(id, out var entry))
                {
                    continue;
                }

                bool isSettings = id.Equals("settings", StringComparison.OrdinalIgnoreCase);
                if (!isSettings && hiddenSet.Contains(id))
                {
                    continue;
                }

                int groupIndex = groupLookup.TryGetValue(id, out int index) ? index : 0;
                if (lastGroupIndex.HasValue && groupIndex != lastGroupIndex.Value)
                {
                    TopCommandBar.PrimaryCommands.Add(new AppBarSeparator());
                }

                TopCommandBar.PrimaryCommands.Add((ICommandBarElement)entry.Button);
                lastGroupIndex = groupIndex;
            }
        }

        private Dictionary<string, (FrameworkElement Button, string ResourceKey)> GetToolbarButtonsById()
        {
            return new Dictionary<string, (FrameworkElement Button, string ResourceKey)>(StringComparer.OrdinalIgnoreCase)
            {
                ["openFile"] = (OpenFileButton, "OpenFile"),
                ["saveFile"] = (SaveFileButton, "SaveFile"),
                ["compare"] = (CompareButton, "Compare"),
                ["terminal"] = (TerminalToggleButton, "Terminal"),
                ["print"] = (PrintButton, "Print"),
                ["topMost"] = (TopMostToggleButton, "TopMost"),
                ["stickyNote"] = (StickyNoteButton, "StickyNote"),
                ["wordWrap"] = (WordWrapToggle, "WordWrap"),
                ["search"] = (SearchButton, "Search"),
                ["markdown"] = (MarkdownToolbarToggle, "Markdown"),
                ["theme"] = (ThemeButton, "Theme"),
                ["split"] = (SplitButton, "Split"),
                ["settings"] = (SettingsButton, "Settings")
            };
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
        #endregion

        #region Advanced Git Handlers

        private async Task RefreshGitStatusUIAsync()
        {
            if (string.IsNullOrEmpty(_currentRepoPath))
            {
                GitPanelBranchText.Text = GetLocalizedString("GitNotDetected", "Git: 감지 안됨");
                StatusGitBranch.Text = GetLocalizedString("GitNotDetected", "Git: 감지 안됨");
                _viewModel.GitFiles.Clear();
                GitBranchesCombo.Items.Clear();
                GitHistoryList.Items.Clear();
                return;
            }

            string branch = await _gitService.GetCurrentBranchAsync(_currentRepoPath);
            string localizedBranch = IsGitNotDetectedText(branch) ? GetLocalizedString("GitNotDetected", "Git: 감지 안됨") : branch;
            GitPanelBranchText.Text = localizedBranch;
            StatusGitBranch.Text = localizedBranch;
            _gitAutoRefreshTimer.Start();

            _viewModel.GitFiles.Clear();
            GitBranchesCombo.Items.Clear();
            foreach (var branchName in await _gitService.GetBranchesAsync(_currentRepoPath))
            {
                GitBranchesCombo.Items.Add(branchName.Trim());
            }

            GitHistoryList.Items.Clear();
            foreach (var history in await _gitService.GetRecentHistoryAsync(_currentRepoPath))
            {
                GitHistoryList.Items.Add(history);
            }

            var fileStatuses = await _gitService.GetFileStatusesAsync(_currentRepoPath);
            foreach (var kvp in fileStatuses)
            {
                string fullPath = kvp.Key;
                string status = kvp.Value; // e.g. "M ", " M", "A ", "??", "D "

                bool isStaged = status.Length > 0 && status[0] != ' ' && status != "??";
                bool isUnstaged = status.Length > 1 && status[1] != ' ';
                string statusDesc = isStaged ? "Staged" : "Unstaged";
                if (status == "??") statusDesc = "Untracked";
                else if (status.Contains("D")) statusDesc = isStaged ? "Deleted staged" : "Deleted";
                else if (status.Contains("R")) statusDesc = "Renamed";
                else if (status.Contains("A")) statusDesc = isStaged ? "Added staged" : "Added";
                else if (isStaged && isUnstaged) statusDesc = "Staged + Unstaged";

                string actionGlyph = isStaged ? "\xE108" : "\xE109"; // Minus (Unstage) or Plus (Stage) in Segoe MDL2

                _viewModel.GitFiles.Add(new GitFileItem
                {
                    Name = Path.GetFileName(fullPath),
                    Path = fullPath,
                    StatusText = $"{statusDesc} ({status.Trim()})",
                    ActionGlyph = actionGlyph,
                    IsStaged = isStaged
                });
            }
        }

        private async void OnGitStageAllClick(object sender, RoutedEventArgs e)
        {
            bool success = await _gitService.StageAllAsync(_currentRepoPath);
            if (success)
            {
                await RefreshGitStatusUIAsync();
            }
            else
            {
                ShowErrorMessage("Git Stage 실패", "전체 Stage 처리에 실패했습니다.");
            }
        }

        private async void OnGitStageToggleClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath)
            {
                var item = _viewModel.GitFiles.FirstOrDefault(f => f.Path == filePath);
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
                string originalContent = await _gitService.GetGitFileContentAsync(_currentRepoPath, item.Path);
                string currentContent = "";
                if (File.Exists(item.Path))
                {
                    currentContent = await _fileService.ReadTextFileAsync(item.Path);
                }

                string fileName = Path.GetFileName(item.Path);
                string customTitle = $"Git 비교: {fileName}";
                string labelA = $"{fileName} (이전 버전)";
                string labelB = $"{fileName} (현재 변경 사항)";

                await OpenCompareTabAsync(item.Path, item.Path, originalContent, currentContent, customTitle, labelA, labelB);
            }
        }

        private async void OnGitRestoreFileClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath)
            {
                var dialog = new ContentDialog
                {
                    Title = "Git 파일 복원",
                    Content = $"{Path.GetFileName(filePath)} 변경 사항을 복원합니다. Untracked 파일은 삭제됩니다.",
                    PrimaryButtonText = "복원",
                    CloseButtonText = "취소",
                    XamlRoot = this.Content.XamlRoot
                };

                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                bool success = await _gitService.RestoreFileAsync(_currentRepoPath, filePath);
                if (success)
                {
                    await RefreshGitStatusUIAsync();
                }
                else
                {
                    ShowErrorMessage("Git Restore 실패", "파일 복원 처리에 실패했습니다.");
                }
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

        private async void OnGitPushClick(object sender, RoutedEventArgs e)
        {
            bool success = await _gitService.PushAsync(_currentRepoPath);
            if (success)
            {
                await RefreshGitStatusUIAsync();
                ShowErrorMessage("Git Push", "Push가 완료되었습니다.");
            }
            else
            {
                ShowErrorMessage("Git Push 실패", "Push 처리에 실패했습니다. 원격 저장소/인증/업스트림 설정을 확인하십시오.");
            }
        }

        private async void OnGitRestoreAllClick(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Git 전체 복원",
                Content = "모든 변경 사항을 복원합니다. Untracked 파일은 삭제됩니다.",
                PrimaryButtonText = "전체 복원",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            bool success = await _gitService.RestoreAllAsync(_currentRepoPath);
            if (success)
            {
                await RefreshGitStatusUIAsync();
            }
            else
            {
                ShowErrorMessage("Git Restore 실패", "전체 복원 처리에 실패했습니다.");
            }
        }

        private async void OnGitRefreshClick(object sender, RoutedEventArgs e)
        {
            await RefreshGitStatusUIAsync();
        }

        #endregion

        #region Advanced Search & Replace Handlers

        private FileSearchOptions GetSearchOptions()
        {
            return new FileSearchOptions
            {
                IsRegex = SearchRegexToggle.IsChecked == true,
                MatchCase = SearchMatchCaseToggle.IsChecked == true,
                WholeWord = SearchWholeWordToggle.IsChecked == true
            };
        }

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
            _viewModel.SearchResults.Clear();

            string searchRoot = !string.IsNullOrEmpty(_currentFolderPath) ? _currentFolderPath : _currentRepoPath;
            var options = GetSearchOptions();
            long thresholdBytes = _settingsService.CurrentSettings.LargeFileThresholdMB * 1024L * 1024L;
            FileSearchSummary summary;

            try
            {
                summary = await _fileSearchService.SearchAsync(searchRoot, query, thresholdBytes, options, PublishSearchResults);
            }
            catch (ArgumentException ex)
            {
                ShowErrorMessage("검색 실패", $"정규식이 올바르지 않습니다.\n{ex.Message}");
                return;
            }

            if (summary.FoundCount == 0 && summary.SkippedFiles > 0)
            {
                ShowErrorMessage("검색 완료", $"검색 결과가 없습니다.\n읽을 수 없어 건너뛴 파일: {summary.SkippedFiles:N0}개");
            }
            else if (summary.FoundCount > 0)
            {
                DispatcherQueue.TryEnqueue(async () =>
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

        private void PublishSearchResults(IReadOnlyList<SearchResultItem> results)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                foreach (var item in results)
                {
                    _viewModel.SearchResults.Add(item);
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

        private async void OnReplaceAllClick(object sender, RoutedEventArgs e)
        {
            string query = SearchQueryInput.Text;
            string replace = ReplaceQueryInput.Text;
            if (string.IsNullOrEmpty(query) || _viewModel.SearchResults.Count == 0) return;

            var options = GetSearchOptions();
            try
            {
                _fileSearchService.BuildSearchRegex(query, options);
            }
            catch (ArgumentException ex)
            {
                ShowErrorMessage("치환 실패", $"정규식이 올바르지 않습니다.\n{ex.Message}");
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "전체 치환 경고",
                Content = $"{_viewModel.SearchResults.Count}개의 일치 항목을 '{replace}'(으)로 일괄 치환하시겠습니까?",
                PrimaryButtonText = "치환 실행",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            long thresholdBytes = _settingsService.CurrentSettings.LargeFileThresholdMB * 1024L * 1024L;
            var grouped = _viewModel.SearchResults.GroupBy(r => r.Path).ToList();
            foreach (var group in grouped)
            {
                string filePath = group.Key;
                try
                {
                    var info = new FileInfo(filePath);
                    if (info.Length > thresholdBytes)
                    {
                        await _fileSearchService.ReplaceInLargeFileAsync(filePath, group.ToList(), query, replace, options);
                    }
                    else
                    {
                        var lines = File.ReadAllLines(filePath).ToList();
                        foreach (int lineNumber in group.Select(r => r.LineNumber).Distinct())
                        {
                            int index = lineNumber - 1;
                            if (index >= 0 && index < lines.Count)
                            {
                                lines[index] = _fileSearchService.ReplaceSearchMatches(lines[index], query, replace, options);
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

            _viewModel.SearchResults.Clear();
            ShowErrorMessage("치환 완료", "모든 매칭 항목의 치환 처리가 완료되었습니다.");
            await RefreshGitStatusUIAsync();
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
            var tabItem = EditorTabView.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id)
                       ?? EditorTabView2.TabItems.Cast<TabViewItem>().FirstOrDefault(t => t.Tag as string == tab.Id);
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
                    tab.Language = _languageDetectionService.GetMonacoLanguageName(file.Path);
                    if (string.IsNullOrWhiteSpace(tab.EncodingName))
                    {
                        tab.EncodingName = "UTF-8";
                    }
                }
                else
                {
                    return false; // Canceled
                }
            }

            try
            {
                await _fileService.SaveTextFileAsync(tab.FilePath, tab.Content, tab.EncodingName);
                tab.IsDirty = false;
                tabItem.Header = tab.DisplayTitle;
                UpdateStatusFileStats(tab);
                UpdateLanguageUI(tab);
                SyncEncodingCombo(tab);
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
                detected = _languageDetectionService.DetectLanguageFromContent(tab.Content, "plaintext");
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

                if (_viewModel.SearchResults.Count == 0 || query != _lastSearchQuery)
                {
                    OnSearchAllFilesClick(this, new RoutedEventArgs());
                }
                else
                {
                    int nextIndex = 0;
                    if (SearchResultsList.SelectedIndex >= 0)
                    {
                        nextIndex = (SearchResultsList.SelectedIndex + 1) % _viewModel.SearchResults.Count;
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
                    var tab = _viewModel.Tabs.FirstOrDefault(t => t.Id == tabId);
                    if (tab != null) UpdateLivePreview(tab);
                }
            }
        }

        private async void OnCompareFilesClick(object sender, RoutedEventArgs e)
        {
            var panel = new StackPanel { Spacing = 12, Width = 400 };
            
            // Build tab list for ComboBoxes
            var tabChoices = new List<string> { "직접 파일 선택..." };
            foreach (var t in _viewModel.Tabs)
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
                    tabA = _viewModel.Tabs[originalCombo.SelectedIndex - 1];
                }
                if (modifiedCombo.SelectedIndex > 0)
                {
                    tabB = _viewModel.Tabs[modifiedCombo.SelectedIndex - 1];
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
            var settings = _settingsService.CurrentSettings;
            if (!settings.FavoritePaths.Contains(tab.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                settings.FavoritePaths.Add(tab.FilePath);
                _ = _settingsService.SaveSettingsAsync(settings);
                RefreshFavoritesUI();
            }
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
                _tabBridges.TryGetValue(tabId, out var bridgeGroup) &&
                bridgeGroup.WebView.CoreWebView2 != null)
            {
                await bridgeGroup.WebView.CoreWebView2.ExecuteScriptAsync("window.print()");
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

        private async void OnGitHistoryItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (GitHistoryList.SelectedItem is string historyItem && !string.IsNullOrEmpty(_currentRepoPath))
            {
                string hash = historyItem.Split(' ')[0];
                string output = await _gitService.RunGitCommandAsync(_currentRepoPath, $"show --stat --format=fuller {hash}");
                var dialog = new ContentDialog
                {
                    Title = $"커밋: {hash}",
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock { Text = output, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap },
                        MaxHeight = 500,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    },
                    CloseButtonText = "닫기",
                    XamlRoot = this.Content.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

#pragma warning disable CS0414
        private static string? _currentLineEnding = "LF";
#pragma warning restore CS0414

        private async void OnFavoritePinClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton btn && btn.Tag is string path)
            {
                var settings = _settingsService.CurrentSettings;
                bool isPinned = btn.IsChecked == true;
                if (isPinned) { if (!settings.PinnedFavoritePaths.Contains(path)) settings.PinnedFavoritePaths.Add(path); }
                else { settings.PinnedFavoritePaths.Remove(path); }
                await _settingsService.SaveSettingsAsync(settings);
                RefreshFavoritesUI();
            }
        }

        private void OnFavoritesTabClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton btn)
            {
                bool showFiles = btn == LeftSidebarTabView.FavoritesFileTabButton;
                LeftSidebarTabView.FavoritesFileTabButton.IsChecked = showFiles;
                LeftSidebarTabView.FavoritesFolderTabButton.IsChecked = !showFiles;
                RefreshFavoritesUI(showFiles);
            }
        }

        private void RefreshFavoritesUI(bool? filterFiles = null)
        {
            _viewModel.Favorites.Clear();
            var settings = _settingsService.CurrentSettings;
            var items = new List<FavoriteItem>();
            foreach (var path in settings.FavoritePaths)
            {
                bool isFolder = Directory.Exists(path);
                bool isFile = !isFolder && File.Exists(path);
                if (isFolder || isFile)
                    items.Add(new FavoriteItem { Name = isFolder ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : Path.GetFileName(path), Path = path, IsFolder = isFolder, IsPinned = settings.PinnedFavoritePaths.Contains(path) });
            }
            var sorted = items.OrderByDescending(i => i.IsPinned).ThenBy(i => i.Name).ToList();
            if (filterFiles.HasValue) sorted = sorted.Where(i => i.IsFolder == !filterFiles.Value).ToList();
            foreach (var item in sorted) _viewModel.Favorites.Add(item);
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
                else if ((int)e.Key == 192)
                {
                    e.Handled = true;
                    ToggleTerminal();
                }
            }
        }

        #endregion
    }

}
