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
        private readonly Action<string, string> _showError;

        public SnippetsController(
            ISnippetService snippetService,
            MainWindowViewModel viewModel,
            LeftSidebarPane leftSidebar,
            Func<XamlRoot> xamlRootProvider,
            Func<string, Task<bool>> insertIntoActiveEditorAsync,
            Action<string, string> showError)
        {
            _snippetService = snippetService;
            _viewModel = viewModel;
            _leftSidebar = leftSidebar;
            _xamlRootProvider = xamlRootProvider;
            _insertIntoActiveEditorAsync = insertIntoActiveEditorAsync;
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
            }
        }

        private async void OnAddSnippetClick(object sender, RoutedEventArgs e)
        {
            var titleBox = new TextBox { PlaceholderText = "스니펫 이름 (예: C# Loop)", Width = 300 };
            var descBox = new TextBox { PlaceholderText = "간단한 설명", Width = 300 };
            var contentBox = new TextBox
            {
                PlaceholderText = "코드 본문 입력...",
                AcceptsReturn = true,
                Height = 150,
                Width = 300,
                FontFamily = new FontFamily("Consolas")
            };

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
                XamlRoot = _xamlRootProvider()
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || string.IsNullOrEmpty(titleBox.Text))
            {
                return;
            }

            await _snippetService.AddSnippetAsync(new SnippetItem
            {
                Title = titleBox.Text,
                Description = descBox.Text,
                Content = contentBox.Text
            });
            Refresh();
        }
    }
}
