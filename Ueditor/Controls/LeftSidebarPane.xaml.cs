using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Ueditor.Controls
{
    public sealed partial class LeftSidebarPane : UserControl
    {
        public LeftSidebarPane()
        {
            InitializeComponent();
        }

        public event RoutedEventHandler? LeftActivityClick;
        public event RoutedEventHandler? ExplorerUpClick;
        public event RoutedEventHandler? SelectFolderClick;
        public event RoutedEventHandler? OpenTerminalClick;
        public event DoubleTappedEventHandler? FileListViewDoubleTapped;
        public event RightTappedEventHandler? FileListViewItemRightTapped;
        public event RoutedEventHandler? AddFileToFavoritesClick;
        public event RoutedEventHandler? AddFolderToFavoritesClick;
        public event DoubleTappedEventHandler? FavoriteItemDoubleTapped;
        public event RoutedEventHandler? RemoveFavoriteClick;
        public event RoutedEventHandler? FavoritePinClick;
        public event RoutedEventHandler? FavoritesTabClick;
        public event DoubleTappedEventHandler? SnippetItemDoubleTapped;
        public event RoutedEventHandler? DeleteSnippetClick;
        public event RoutedEventHandler? AddSnippetClick;
        public event DoubleTappedEventHandler? GitFileDoubleTapped;
        public event RoutedEventHandler? GitStageToggleClick;
        public event RoutedEventHandler? GitRestoreFileClick;
        public event RoutedEventHandler? GitCommitClick;
        public event RoutedEventHandler? GitStageAllClick;
        public event RoutedEventHandler? GitRestoreAllClick;
        public event RoutedEventHandler? GitPushClick;
        public event RoutedEventHandler? GitRefreshClick;
        public event DoubleTappedEventHandler? GitHistoryItemDoubleTapped;
        public event KeyEventHandler? SearchQueryInputKeyDown;
        public event RoutedEventHandler? SearchAllFilesClick;
        public event RoutedEventHandler? ReplaceAllClick;
        public event DoubleTappedEventHandler? SearchResultDoubleTapped;
        public event DoubleTappedEventHandler? RecentFileItemDoubleTapped;
        public event RoutedEventHandler? RemoveRecentFileClick;

        public Grid ExplorerPage => ExplorerSidebarPage;
        public Grid FavoritesPage => FavoritesSidebarPage;
        public Grid SnippetsPage => SnippetsSidebarPage;
        public Grid GitPage => GitSidebarPage;
        public Grid SearchPage => SearchSidebarPage;
        public Grid RecentPage => RecentSidebarPage;

        public ToggleButton ExplorerActivity => ExplorerActivityButton;
        public ToggleButton FavoritesActivity => FavoritesActivityButton;
        public ToggleButton SnippetsActivity => SnippetsActivityButton;
        public ToggleButton GitActivity => GitActivityButton;
        public ToggleButton SearchActivity => SearchActivityButton;
        public ToggleButton RecentActivity => RecentActivityButton;

        public TextBlock ExplorerStatus => ExplorerStatusText;
        public TextBlock FavoritesHeader => FavoritesHeaderText;
        public TextBlock SnippetsHeader => SnippetsHeaderText;
        public Button AddSnippet => AddSnippetButton;

        public TextBlock SearchHeaderLabel => SearchHeaderText;
        public Button SearchAllFilesBtn => SearchAllButton;
        public Button ReplaceAllFilesBtn => ReplaceAllButton;
        public TextBlock RecentFilesHeaderLabel => RecentFilesHeaderText;
        public TextBlock GitHeaderLabel => GitHeaderText;
        public Button GitCommitBtn => GitCommitButton;
        public Button GitStageAllBtn => GitStageAllButton;
        public Button GitRestoreAllBtn => GitRestoreAllButton;
        public Button GitPushBtn => GitPushButton;
        public Button GitRefreshBtn => GitRefreshButton;
        public TextBlock GitHistoryHeaderLabel => GitHistoryHeader;
        public Button ExplorerUpBtn => ExplorerUpButton;
        public Button ExplorerSelectFolderBtn => ExplorerSelectFolderButton;
        public Button ExplorerTerminalBtn => ExplorerTerminalButton;

        public ListView FileList => FileListView;
        public ListView FavoritesList => FavoritesListView;
        public ListView RecentFilesList => RecentFilesListView;
        public ListView SnippetsList => SnippetsListView;
        public ListView GitChangedFiles => GitChangedFilesList;
        public ListView GitHistory => GitHistoryList;
        public ListView SearchResults => SearchResultsList;

        public TextBlock GitPanelBranch => GitPanelBranchText;
        public ComboBox GitBranches => GitBranchesCombo;
        public TextBox GitCommitMessage => GitCommitMessageInput;

        public TextBox SearchQuery => SearchQueryInput;
        public TextBox ReplaceQuery => ReplaceQueryInput;
        public ToggleButton SearchMatchCase => SearchMatchCaseToggle;
        public ToggleButton SearchWholeWord => SearchWholeWordToggle;
        public ToggleButton SearchRegex => SearchRegexToggle;

        public ToggleButton FavoritesFileTabButton => FavoritesFileTab;
        public ToggleButton FavoritesFolderTabButton => FavoritesFolderTab;
        public TextBlock FavoritesPinIndicatorText => FavoritesPinIndicator;

        public void Localize(Func<string, string, string> getString, bool updateEmptyFolderStatus, Func<string, bool> isGitNotDetected)
        {
            ToolTipService.SetToolTip(ExplorerActivityButton, getString("Explorer", "탐색기"));
            ToolTipService.SetToolTip(FavoritesActivityButton, getString("Favorites", "즐겨찾기"));
            ToolTipService.SetToolTip(SnippetsActivityButton, getString("Snippets", "스니펫"));
            ToolTipService.SetToolTip(GitActivityButton, getString("Git", "Git"));
            ToolTipService.SetToolTip(SearchActivityButton, getString("Search", "검색"));
            ToolTipService.SetToolTip(RecentActivityButton, getString("RecentFiles", "최근 파일"));

            if (updateEmptyFolderStatus)
            {
                ExplorerStatusText.Text = getString("NoFolderSelected", "폴더를 선택하세요.");
            }

            FavoritesHeaderText.Text = getString("FavoritesHeader", "즐겨찾기 목록");
            SnippetsHeaderText.Text = getString("SnippetsHeader", "코드 및 수식 템플릿");
            AddSnippetButton.Content = getString("AddSnippet", "스니펫 추가...");

            ToolTipService.SetToolTip(ExplorerUpButton, getString("ExplorerUpTooltip", "상위 폴더"));
            ExplorerSelectFolderButton.Content = getString("ExplorerSelectFolder", "폴더 선택...");
            ToolTipService.SetToolTip(ExplorerTerminalButton, getString("ExplorerOpenTerminalTooltip", "현재 폴더에서 터미널 열기"));

            FavoritesFileTab.Content = getString("FavoritesFileTab", "파일");
            FavoritesFolderTab.Content = getString("FavoritesFolderTab", "폴더");
            ToolTipService.SetToolTip(FavoritesPinIndicator, getString("FavoritesPinTooltip", "고정"));

            RecentFilesHeaderText.Text = getString("RecentFilesHeader", "최근 파일");

            SearchHeaderText.Text = getString("SearchHeader", "폴더 전체 검색 및 바꾸기");
            SearchQueryInput.PlaceholderText = getString("SearchPlaceholder", "검색어 입력...");
            ReplaceQueryInput.PlaceholderText = getString("ReplacePlaceholder", "바꿀 단어 입력...");
            ToolTipService.SetToolTip(SearchMatchCaseToggle, getString("SearchMatchCaseTooltip", "대소문자 구분"));
            ToolTipService.SetToolTip(SearchWholeWordToggle, getString("SearchWholeWordTooltip", "단어 단위"));
            ToolTipService.SetToolTip(SearchRegexToggle, getString("SearchRegexTooltip", "정규식 검색"));
            SearchAllButton.Content = getString("SearchAllFiles", "전체 검색");
            ReplaceAllButton.Content = getString("ReplaceAllFiles", "모두 바꾸기");

            GitHeaderText.Text = getString("GitRepoHeader", "Git 저장소 관리");
            GitBranchesCombo.PlaceholderText = getString("GitBranchPlaceholder", "브랜치 목록");
            GitCommitMessageInput.PlaceholderText = getString("GitCommitPlaceholder", "커밋 메시지 입력...");
            GitCommitButton.Content = getString("GitCommit", "커밋 (Commit)");
            GitStageAllButton.Content = getString("GitStageAll", "전체 Stage");
            GitRestoreAllButton.Content = getString("GitRestoreAll", "전체 Restore");
            GitPushButton.Content = getString("GitPush", "Push");
            GitRefreshButton.Content = getString("GitRefresh", "새로고침");
            GitHistoryHeader.Text = getString("GitHistory", "과거 기록");

            if (isGitNotDetected(GitPanelBranchText.Text))
            {
                GitPanelBranchText.Text = getString("GitNotDetected", "Git: 감지 안됨");
            }
        }

        public int ShowPage(int index)
        {
            Grid[] pages =
            {
                ExplorerSidebarPage,
                FavoritesSidebarPage,
                RecentSidebarPage,
                SearchSidebarPage,
                GitSidebarPage,
                SnippetsSidebarPage
            };

            ToggleButton[] buttons =
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

            return safeIndex;
        }

        private void OnLeftActivityClick(object sender, RoutedEventArgs e) => LeftActivityClick?.Invoke(sender, e);
        private void OnExplorerUpClick(object sender, RoutedEventArgs e) => ExplorerUpClick?.Invoke(sender, e);
        private void OnSelectFolderClick(object sender, RoutedEventArgs e) => SelectFolderClick?.Invoke(sender, e);
        private void OnOpenTerminalClick(object sender, RoutedEventArgs e) => OpenTerminalClick?.Invoke(sender, e);
        private void OnFileListViewDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => FileListViewDoubleTapped?.Invoke(sender, e);
        private void OnFileListViewItemRightTapped(object sender, RightTappedRoutedEventArgs e) => FileListViewItemRightTapped?.Invoke(sender, e);
        private void OnAddFileToFavoritesClick(object sender, RoutedEventArgs e) => AddFileToFavoritesClick?.Invoke(sender, e);
        private void OnAddFolderToFavoritesClick(object sender, RoutedEventArgs e) => AddFolderToFavoritesClick?.Invoke(sender, e);
        private void OnFavoriteItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => FavoriteItemDoubleTapped?.Invoke(sender, e);
        private void OnRemoveFavoriteClick(object sender, RoutedEventArgs e) => RemoveFavoriteClick?.Invoke(sender, e);
        private void OnFavoritePinClick(object sender, RoutedEventArgs e) => FavoritePinClick?.Invoke(sender, e);
        private void OnFavoritesTabClick(object sender, RoutedEventArgs e) => FavoritesTabClick?.Invoke(sender, e);
        private void OnSnippetItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => SnippetItemDoubleTapped?.Invoke(sender, e);
        private void OnDeleteSnippetClick(object sender, RoutedEventArgs e) => DeleteSnippetClick?.Invoke(sender, e);
        private void OnAddSnippetClick(object sender, RoutedEventArgs e) => AddSnippetClick?.Invoke(sender, e);
        private void OnGitFileDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => GitFileDoubleTapped?.Invoke(sender, e);
        private void OnGitStageToggleClick(object sender, RoutedEventArgs e) => GitStageToggleClick?.Invoke(sender, e);
        private void OnGitRestoreFileClick(object sender, RoutedEventArgs e) => GitRestoreFileClick?.Invoke(sender, e);
        private void OnGitCommitClick(object sender, RoutedEventArgs e) => GitCommitClick?.Invoke(sender, e);
        private void OnGitStageAllClick(object sender, RoutedEventArgs e) => GitStageAllClick?.Invoke(sender, e);
        private void OnGitRestoreAllClick(object sender, RoutedEventArgs e) => GitRestoreAllClick?.Invoke(sender, e);
        private void OnGitPushClick(object sender, RoutedEventArgs e) => GitPushClick?.Invoke(sender, e);
        private void OnGitRefreshClick(object sender, RoutedEventArgs e) => GitRefreshClick?.Invoke(sender, e);
        private void OnGitHistoryItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => GitHistoryItemDoubleTapped?.Invoke(sender, e);
        private void OnSearchQueryInputKeyDown(object sender, KeyRoutedEventArgs e) => SearchQueryInputKeyDown?.Invoke(sender, e);
        private void OnSearchAllFilesClick(object sender, RoutedEventArgs e) => SearchAllFilesClick?.Invoke(sender, e);
        private void OnReplaceAllClick(object sender, RoutedEventArgs e) => ReplaceAllClick?.Invoke(sender, e);
        private void OnSearchResultDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => SearchResultDoubleTapped?.Invoke(sender, e);
        private void OnRecentFileItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => RecentFileItemDoubleTapped?.Invoke(sender, e);
        private void OnRemoveRecentFileClick(object sender, RoutedEventArgs e) => RemoveRecentFileClick?.Invoke(sender, e);
    }
}
