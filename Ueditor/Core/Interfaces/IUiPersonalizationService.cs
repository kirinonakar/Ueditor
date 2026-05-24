using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Ueditor.Core.Models;

namespace Ueditor.Core.Interfaces
{
    public interface IUiPersonalizationService
    {
        void Apply(
            EditorSettings settings,
            AppWindow appWindow,
            FrameworkElement? rootElement,
            Action<Windows.UI.Color> applyMarkdownToolbarBackground);
    }
}
