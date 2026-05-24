using System.Collections.Generic;
using System.Collections.ObjectModel;
using Ueditor.Core.Models;

namespace Ueditor.Core.Interfaces
{
    public interface IRecentFilesService
    {
        void LoadInto(ObservableCollection<RecentFileItem> recentFiles);
        void Save(IEnumerable<RecentFileItem> recentFiles);
        void Add(ObservableCollection<RecentFileItem> recentFiles, string filePath);
        bool Remove(ObservableCollection<RecentFileItem> recentFiles, string path);
    }
}
