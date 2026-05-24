using Microsoft.UI.Xaml;

namespace Ueditor.Core.Interfaces
{
    public interface IFileSaveDialogService
    {
        string? ShowSaveDialog(Window owner, string suggestedName, string? initialDirectory);
    }
}
