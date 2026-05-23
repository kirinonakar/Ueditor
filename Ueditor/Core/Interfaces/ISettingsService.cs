using System.Threading.Tasks;
using Ueditor.Core.Models;

namespace Ueditor.Core.Interfaces
{
    public interface ISettingsService
    {
        EditorSettings CurrentSettings { get; }
        Task LoadSettingsAsync();
        Task SaveSettingsAsync(EditorSettings settings);
    }
}
