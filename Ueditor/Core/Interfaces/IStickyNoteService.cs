using Microsoft.UI.Xaml;

namespace Ueditor.Core.Interfaces
{
    public interface IStickyNoteService
    {
        void ShowOrActivate(Window ownerWindow);
        void ApplyTopMost(Window window, bool topMost);
    }
}
