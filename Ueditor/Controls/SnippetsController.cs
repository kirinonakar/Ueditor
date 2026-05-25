using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Ueditor.Core.Interfaces;
using Ueditor.ViewModels;

namespace Ueditor.Controls
{
    public sealed class SnippetsController
    {
        private readonly ISnippetService _snippetService;
        private readonly MainWindowViewModel _viewModel;
        private readonly LeftSidebarPane _leftSidebar;
        private readonly Func<XamlRoot> _xamlRootProvider;
        private readonly Func<string, Task<bool>> _insertIntoActiveEditorAsync;
        private readonly Func<Task>? _snippetsChangedAsync;
        private readonly Action<string, string> _showError;

        public SnippetsController(
            ISnippetService snippetService,
            MainWindowViewModel viewModel,
            LeftSidebarPane leftSidebar,
            Func<XamlRoot> xamlRootProvider,
            Func<string, Task<bool>> insertIntoActiveEditorAsync,
            Func<Task>? snippetsChangedAsync,
            Action<string, string> showError)
        {
            _snippetService = snippetService;
            _viewModel = viewModel;
            _leftSidebar = leftSidebar;
            _xamlRootProvider = xamlRootProvider;
            _insertIntoActiveEditorAsync = insertIntoActiveEditorAsync;
            _snippetsChangedAsync = snippetsChangedAsync;
            _showError = showError;

            _leftSidebar.SnippetsList.ItemsSource = _viewModel.Snippets;
            WireEvents();
        }

        public async Task LoadAsync()
        {
            await _snippetService.LoadSnippetsAsync();
            Refresh();
        }

        public void Refresh()
        {
            _viewModel.Snippets.Clear();
            foreach (var item in _snippetService.GetSnippets())
            {
                _viewModel.Snippets.Add(item);
            }
        }

        private void WireEvents()
        {
            _leftSidebar.SnippetItemDoubleTapped += OnSnippetItemDoubleTapped;
            _leftSidebar.DeleteSnippetClick += OnDeleteSnippetClick;
            _leftSidebar.EditSnippetClick += OnEditSnippetClick;
            _leftSidebar.AddSnippetClick += OnAddSnippetClick;
        }

        private async void OnSnippetItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            var item = VisualTreeDataContext.FindFromOriginalSource<SnippetItem>(e.OriginalSource)
                ?? _leftSidebar.SnippetsList.SelectedItem as SnippetItem;
            if (item == null)
            {
                return;
            }

            if (!await _insertIntoActiveEditorAsync(item.Content))
            {
                _showError("스니펫 삽입 오류", "현재 텍스트 에디터 창이 활성화되어 있지 않습니다.");
            }
        }

        private async void OnDeleteSnippetClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string title })
            {
                await _snippetService.DeleteSnippetAsync(title);
                Refresh();
                await NotifySnippetsChangedAsync();
            }
        }

        private async void OnAddSnippetClick(object sender, RoutedEventArgs e)
        {
            var item = await ShowSnippetDialogAsync("새 코드/수식 스니펫 추가", "추가", null);
            if (item == null)
            {
                return;
            }

            await _snippetService.AddSnippetAsync(item);
            Refresh();
            await NotifySnippetsChangedAsync();
        }

        private async void OnEditSnippetClick(object sender, RoutedEventArgs e)
        {
            var item = (sender as Button)?.DataContext as SnippetItem;
            if (item == null)
            {
                return;
            }

            var editedItem = await ShowSnippetDialogAsync("스니펫 수정", "저장", item);
            if (editedItem == null)
            {
                return;
            }

            await _snippetService.UpdateSnippetAsync(item.Title, editedItem);
            Refresh();
            await NotifySnippetsChangedAsync();
        }

        private async Task<SnippetItem?> ShowSnippetDialogAsync(string title, string primaryButtonText, SnippetItem? existing)
        {
            var titleBox = new TextBox
            {
                PlaceholderText = "스니펫 이름 (예: C# Loop)",
                Text = existing?.Title ?? string.Empty,
                Width = 300
            };
            var keywordBox = new TextBox
            {
                PlaceholderText = "키워드 (예: loop)",
                Text = existing?.Keyword ?? string.Empty,
                Width = 300
            };
            var descBox = new TextBox
            {
                PlaceholderText = "간단한 설명",
                Text = existing?.Description ?? string.Empty,
                Width = 300
            };
            var contentBox = new TextBox
            {
                PlaceholderText = "코드 본문 입력...",
                Text = existing?.Content ?? string.Empty,
                AcceptsReturn = true,
                Height = 150,
                Width = 300,
                FontFamily = new FontFamily("Consolas")
            };

            var stack = new StackPanel { Spacing = 10 };
            stack.Children.Add(new TextBlock { Text = "스니펫 이름" });
            stack.Children.Add(titleBox);
            stack.Children.Add(new TextBlock { Text = "자동완성 키워드" });
            stack.Children.Add(keywordBox);
            stack.Children.Add(new TextBlock { Text = "설명" });
            stack.Children.Add(descBox);
            stack.Children.Add(new TextBlock { Text = "템플릿 내용" });
            stack.Children.Add(contentBox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = stack,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "취소",
                XamlRoot = _xamlRootProvider()
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || string.IsNullOrEmpty(titleBox.Text))
            {
                return null;
            }

            return new SnippetItem
            {
                Title = titleBox.Text,
                Keyword = keywordBox.Text,
                Description = descBox.Text,
                Content = contentBox.Text
            };
        }

        private Task NotifySnippetsChangedAsync()
        {
            return _snippetsChangedAsync?.Invoke() ?? Task.CompletedTask;
        }
    }
}
