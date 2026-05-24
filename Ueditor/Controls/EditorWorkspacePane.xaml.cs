using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Ueditor.Controls
{
    public enum EditorSplitMode
    {
        None,
        Vertical,
        Horizontal
    }

    public sealed partial class EditorWorkspacePane : UserControl
    {
        private readonly TerminalPane _terminalPane = new TerminalPane();
        private bool _isDraggingEditorSplitter = false;
        private double _editorSplitterStartWidth = 0;
        private double _editorSplitterStartHeight = 0;
        private double _editorSplitterStartPointerX = 0;
        private double _editorSplitterStartPointerY = 0;
        private bool _isVerticalSplit = true;

        private bool _isDraggingTerminalSplitter = false;
        private double _terminalSplitterStartHeight = 0;
        private double _terminalSplitterStartPointerY = 0;

        public EditorWorkspacePane()
        {
            InitializeComponent();
            TerminalPaneHost.Content = _terminalPane;
            ActiveTabView = EditorTabView;
        }

        public event TypedEventHandler<TabView, object>? PrimaryAddTabButtonClick;
        public event TypedEventHandler<TabView, TabViewTabCloseRequestedEventArgs>? PrimaryTabCloseRequested;
        public event SelectionChangedEventHandler? PrimarySelectionChanged;
        public event TypedEventHandler<TabView, object>? SecondaryAddTabButtonClick;
        public event TypedEventHandler<TabView, TabViewTabCloseRequestedEventArgs>? SecondaryTabCloseRequested;
        public event SelectionChangedEventHandler? SecondarySelectionChanged;
        public event RoutedEventHandler? TabViewGotFocus;
        public event RoutedEventHandler? MoveTabLeftClick;
        public event RoutedEventHandler? MoveTabRightClick;
        public event EventHandler? TerminalPanelHeightChanged;

        public TabView? ActiveTabView { get; set; }
        public EditorSplitMode CurrentSplitMode { get; private set; } = EditorSplitMode.None;
        public double LastTerminalHeight { get; set; } = 220;

        public TabView EditorTabViewControl => EditorTabView;
        public TabView EditorTabView2Control => EditorTabView2;
        public TerminalPane TerminalPaneControl => _terminalPane;
        private TerminalPane TerminalPane => _terminalPane;

        public bool IsTerminalVisible => TerminalPaneHost.Visibility == Visibility.Visible;

        public double PersistedTerminalPanelHeight =>
            IsTerminalVisible && TerminalPanelRow.Height.Value > 0
                ? TerminalPanelRow.Height.Value
                : LastTerminalHeight;

        public TabView GetCurrentActiveTabView()
        {
            return ActiveTabView ?? EditorTabView;
        }

        public void RefreshSplitters()
        {
            foreach (var child in EditorSplitGrid.Children)
            {
                if (child is CustomSplitter cs)
                    cs.RefreshTheme();
            }
            if (TerminalSplitter is CustomSplitter terminalSplitter)
                terminalSplitter.RefreshTheme();
        }

        public void Localize(Func<string, string, string> getString)
        {
            string leftTooltip = getString("MoveTabLeftTooltip", "왼쪽 탭으로 이동 (Ctrl/Shift 누르고 클릭하면 탭 위치 이동)");
            string rightTooltip = getString("MoveTabRightTooltip", "오른쪽 탭으로 이동 (Ctrl/Shift 누르고 클릭하면 탭 위치 이동)");

            ToolTipService.SetToolTip(MoveTabLeftBtn, leftTooltip);
            ToolTipService.SetToolTip(MoveTabRightBtn, rightTooltip);
            ToolTipService.SetToolTip(MoveTab2LeftBtn, leftTooltip);
            ToolTipService.SetToolTip(MoveTab2RightBtn, rightTooltip);
        }

        public void SetSplitMode(EditorSplitMode mode, Action openNewTab)
        {
            CurrentSplitMode = mode;

            if (mode == EditorSplitMode.None)
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

                ActiveTabView = EditorTabView;
                if (EditorTabView.SelectedItem == null && EditorTabView.TabItems.Count > 0)
                {
                    EditorTabView.SelectedIndex = 0;
                }
            }
            else if (mode == EditorSplitMode.Vertical)
            {
                _isVerticalSplit = true;

                EditorRow1.Height = new GridLength(1, GridUnitType.Star);
                EditorRow2.Height = new GridLength(0);
                EditorSplitterRow.Height = new GridLength(0);

                double totalWidth = EditorSplitGrid.ActualWidth;
                double halfWidth = (totalWidth - 4) / 2;
                if (halfWidth < 100)
                {
                    halfWidth = 300;
                }

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

                EnsureSecondPaneHasTab(openNewTab);
            }
            else if (mode == EditorSplitMode.Horizontal)
            {
                _isVerticalSplit = false;

                double totalHeight = EditorSplitGrid.ActualHeight;
                double halfHeight = (totalHeight - 4) / 2;
                if (halfHeight < 100)
                {
                    halfHeight = 300;
                }

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

                EnsureSecondPaneHasTab(openNewTab);
            }
        }

        public bool ToggleTerminal(Func<string> workingDirectoryProvider)
        {
            if (TerminalPane.Visibility == Visibility.Visible)
            {
                HideTerminalPanel();
                return false;
            }

            EnsureTerminalPanelVisible();
            if (TerminalPane.HasSessions)
            {
                TerminalPane.ResumeNativeWindows();
                TerminalPane.ResizeEmbeddedTerminal();
            }
            else
            {
                string workingDirectory = workingDirectoryProvider();
                if (string.IsNullOrWhiteSpace(workingDirectory) || !System.IO.Directory.Exists(workingDirectory))
                {
                    workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                TerminalPane.OpenTerminal(workingDirectory);
            }

            return true;
        }

        public bool HideTerminalPanelIfEmpty()
        {
            if (TerminalPane.HasSessions)
            {
                return false;
            }

            CollapseTerminalPanel();
            return true;
        }

        public void StopAllTerminalSessions()
        {
            TerminalPane.StopAllSessions();
        }

        private void EnsureSecondPaneHasTab(Action openNewTab)
        {
            if (EditorTabView2.TabItems.Count > 0)
            {
                return;
            }

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
                ActiveTabView = EditorTabView2;
                openNewTab();
            }
        }

        private void EnsureTerminalPanelVisible()
        {
            _terminalPane.Visibility = Visibility.Visible;
            TerminalPaneHost.Visibility = Visibility.Visible;
            TerminalSplitter.Visibility = Visibility.Visible;
            TerminalSplitterRow.Height = new GridLength(4);
            TerminalPanelRow.Height = new GridLength(Math.Clamp(LastTerminalHeight, 120, Math.Max(160, ActualHeight - 180)));
        }

        private void HideTerminalPanel()
        {
            if (TerminalPane.HasSessions)
            {
                TerminalPane.SuspendNativeWindows();
            }

            CollapseTerminalPanel();
        }

        private void CollapseTerminalPanel()
        {
            _terminalPane.Visibility = Visibility.Collapsed;
            TerminalPaneHost.Visibility = Visibility.Collapsed;
            TerminalSplitter.Visibility = Visibility.Collapsed;
            TerminalSplitterRow.Height = new GridLength(0);
            TerminalPanelRow.Height = new GridLength(0);
        }

        private void OnEditorSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
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

        private void OnEditorSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDraggingEditorSplitter && sender is UIElement)
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

        private void OnEditorSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDraggingEditorSplitter && sender is UIElement splitter)
            {
                _isDraggingEditorSplitter = false;
                splitter.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnTerminalSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is UIElement splitter && TerminalPaneHost.Visibility == Visibility.Visible)
            {
                _isDraggingTerminalSplitter = true;
                _terminalSplitterStartHeight = TerminalPanelRow.Height.Value > 0 ? TerminalPanelRow.Height.Value : LastTerminalHeight;
                var pt = e.GetCurrentPoint(this).Position;
                _terminalSplitterStartPointerY = pt.Y;
                splitter.CapturePointer(e.Pointer);
                e.Handled = true;
            }
        }

        private void OnTerminalSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDraggingTerminalSplitter)
            {
                var pt = e.GetCurrentPoint(this).Position;
                double deltaY = pt.Y - _terminalSplitterStartPointerY;
                double maxHeight = Math.Max(160, ActualHeight * 0.6);
                double newHeight = Math.Clamp(_terminalSplitterStartHeight + deltaY, 120, maxHeight);
                LastTerminalHeight = newHeight;
                TerminalPanelRow.Height = new GridLength(newHeight);
                TerminalPane.ResizeEmbeddedTerminal();
                e.Handled = true;
            }
        }

        private void OnTerminalSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDraggingTerminalSplitter && sender is UIElement splitter)
            {
                _isDraggingTerminalSplitter = false;
                splitter.ReleasePointerCapture(e.Pointer);
                TerminalPanelHeightChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void OnEditorTabViewAddTabClick(TabView sender, object args) => PrimaryAddTabButtonClick?.Invoke(sender, args);

        private void OnEditorTabViewTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) =>
            PrimaryTabCloseRequested?.Invoke(sender, args);

        private void OnEditorTabViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ActiveTabView = EditorTabView;
            PrimarySelectionChanged?.Invoke(sender, e);
        }

        private void OnEditorTabView2AddTabClick(TabView sender, object args)
        {
            ActiveTabView = sender;
            SecondaryAddTabButtonClick?.Invoke(sender, args);
        }

        private void OnEditorTabView2TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            ActiveTabView = sender;
            SecondaryTabCloseRequested?.Invoke(sender, args);
        }

        private void OnEditorTabView2SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ActiveTabView = EditorTabView2;
            SecondarySelectionChanged?.Invoke(sender, e);
        }

        private void OnTabViewGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TabView tabView)
            {
                ActiveTabView = tabView;
            }

            TabViewGotFocus?.Invoke(sender, e);
        }

        private void OnMoveTabLeftClick(object sender, RoutedEventArgs e) => MoveTabLeftClick?.Invoke(sender, e);
        private void OnMoveTabRightClick(object sender, RoutedEventArgs e) => MoveTabRightClick?.Invoke(sender, e);
    }
}
