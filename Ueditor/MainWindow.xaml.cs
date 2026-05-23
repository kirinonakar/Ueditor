using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
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

            // 2. Initialize Preview Panel WebView2
            await InitializePreviewWebViewAsync();

            // 3. Open a default blank tab on startup
            OpenNewTab();
        }

        #region WebView2 Host Resource Mapping & Preview Init

        private async Task InitializePreviewWebViewAsync()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string cacheFolder = Path.Combine(localAppData, "Ueditor", "WebView2Cache");
                var env = await CoreWebView2Environment.CreateWithOptionsAsync((string)null, cacheFolder, (CoreWebView2EnvironmentOptions)null);
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
                var env = await CoreWebView2Environment.CreateWithOptionsAsync((string)null, cacheFolder, (CoreWebView2EnvironmentOptions)null);
                
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
                                
                                var reply = new { action = "receiveLines", startLine = start, lines = lines };
                                string replyJson = System.Text.Json.JsonSerializer.Serialize(reply);
                                wv.CoreWebView2.PostWebMessageAsJson(replyJson);
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

            var stack = new StackPanel { Spacing = 15 };
            stack.Children.Add(new TextBlock { Text = "에디터 테마 설정" });
            stack.Children.Add(themeCombo);
            stack.Children.Add(new TextBlock { Text = $"글자 크기 (현재: {settings.FontSize}pt)" });
            stack.Children.Add(sizeSlider);

            var dialog = new ContentDialog
            {
                Title = "Ueditor 기본 환경 설정",
                Content = stack,
                PrimaryButtonText = "적용 및 저장",
                CloseButtonText = "취소",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                settings.Theme = themeCombo.SelectedIndex == 0 ? "Dark" : "Light";
                settings.FontSize = sizeSlider.Value;

                await _settingsService.SaveSettingsAsync(settings);

                // Update settings for all active Monaco editors
                foreach (var grp in _tabBridges.Values)
                {
                    await grp.Bridge.UpdateOptionsAsync(settings);
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

        #endregion
    }
}
