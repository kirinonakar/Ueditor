using System.Threading.Tasks;
using System.Collections.Generic;

namespace Ueditor.Core.Interfaces
{
    public class SnippetItem
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public interface ISnippetService
    {
        Task LoadSnippetsAsync();
        Task SaveSnippetsAsync();
        List<SnippetItem> GetSnippets();
        Task AddSnippetAsync(SnippetItem item);
        Task DeleteSnippetAsync(string title);
    }
}
