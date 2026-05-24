using System;
using System.Diagnostics;
using System.Text;

namespace Ueditor.Core.Models
{
    public class TerminalSession
    {
        private static int _nextNumber = 1;

        public TerminalSession(string workingDirectory)
        {
            Number = _nextNumber++;
            WorkingDirectory = workingDirectory;
            WindowTitle = $"Ueditor_Console_{Process.GetCurrentProcess().Id}_{Guid.NewGuid():N}";
        }

        public int Number { get; }
        public string WorkingDirectory { get; }
        public string WindowTitle { get; }
        public string DisplayTitle => $"p{Number}";
        public Process? Process { get; set; }
        public IntPtr WindowHandle { get; set; } = IntPtr.Zero;
        public bool IsNative { get; set; }
        public StringBuilder Output { get; } = new StringBuilder();
    }
}
