using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ueditor.Core.Models;
using WinRT.Interop;

namespace Ueditor.Controls
{
    public sealed partial class TerminalPane : UserControl
    {
        private readonly ObservableCollection<TerminalSession> _terminalSessions = new ObservableCollection<TerminalSession>();
        private TerminalSession? _activeTerminalSession;
        private Window? _ownerWindow;
        private IntPtr _parentHwnd = IntPtr.Zero;
        private SubclassProc? _subclassCallback;
        private bool _resizeQueued = false;
        private Func<string, string, string>? _getString;

        public TerminalPane()
        {
            InitializeComponent();
            TerminalSessionsList.ItemsSource = _terminalSessions;
            KeyDown += OnTerminalPaneKeyDown;
            Unloaded += OnUnloaded;
            ActualThemeChanged += OnActualThemeChanged;
            Loaded += (s, e) => {
                UpdateAllTerminalThemes();
            };
        }

        public void Localize(Func<string, string, string> getString)
        {
            _getString = getString;

            if (_activeTerminalSession == null)
            {
                TerminalTitleText.Text = getString("TerminalTitle", "터미널");
            }
            else
            {
                TerminalTitleText.Text = $"{getString("TerminalTitle", "터미널")} - {_activeTerminalSession.WorkingDirectory}";
            }

            NewTerminalButton.Content = getString("NewTerminal", "새 터미널");
            CloseTerminalButton.Content = getString("CloseTerminal", "닫기");
            TerminalInputBox.PlaceholderText = getString("TerminalPrompt", "명령 입력 후 Enter");
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            UpdateAllTerminalThemes();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_parentHwnd != IntPtr.Zero && _subclassCallback != null)
            {
                RemoveWindowSubclass(_parentHwnd, _subclassCallback, ParentWindowSubclassId);
                _subclassCallback = null;
                _parentHwnd = IntPtr.Zero;
            }
        }

        public event EventHandler? SessionsEmptied;
        public event EventHandler? CloseRequested;

        public Func<string>? WorkingDirectoryProvider { get; set; }

        public bool HasSessions => _terminalSessions.Count > 0;

        public void AttachOwner(Window ownerWindow)
        {
            _ownerWindow = ownerWindow;
            _parentHwnd = WindowNative.GetWindowHandle(_ownerWindow);
            if (_parentHwnd != IntPtr.Zero)
            {
                _subclassCallback = new SubclassProc(TerminalWindowSubclass);
                SetWindowSubclass(_parentHwnd, _subclassCallback, ParentWindowSubclassId, IntPtr.Zero);
            }
        }

        public void OpenTerminal(string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                return;
            }

            _ = StartEmbeddedTerminalAsync(workingDirectory);
        }

        public void SuspendNativeWindows()
        {
            foreach (var session in _terminalSessions)
            {
                if (session.IsNative && session.WindowHandle != IntPtr.Zero)
                {
                    ShowWindow(session.WindowHandle, SW_HIDE);
                }
            }
        }

        public void ResumeNativeWindows()
        {
            foreach (var session in _terminalSessions)
            {
                if (session.IsNative && session.WindowHandle != IntPtr.Zero)
                {
                    ShowWindow(session.WindowHandle, SW_SHOW);
                }
            }
            QueueEmbeddedTerminalResize();
        }

        private void HideNativeTerminalScrollBars(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            try
            {
                int style = GetWindowLong(hwnd, GWL_STYLE);
                style &= ~WS_VSCROLL;
                style &= ~WS_HSCROLL;
                SetWindowLong(hwnd, GWL_STYLE, style);

                SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE |
                    SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOOWNERZORDER);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to hide native terminal scrollbars: {ex.Message}");
            }
        }

        private void ApplyNativeTerminalClipRegion(IntPtr hwnd, int visibleWidth, int visibleHeight)
        {
            if (hwnd == IntPtr.Zero || visibleWidth <= 0 || visibleHeight <= 0)
            {
                return;
            }

            IntPtr region = IntPtr.Zero;
            try
            {
                // Do not mutate the console buffer.  Instead, make the native console
                // window slightly larger and clip its visible region to the WinUI host
                // size. The native scrollbar remains outside the clipped region, which
                // avoids crashes in TUI apps such as opencode that restore console
                // buffers/alternate-screen state on exit.
                region = CreateRectRgn(0, 0, visibleWidth, visibleHeight);
                if (region == IntPtr.Zero)
                {
                    return;
                }

                if (SetWindowRgn(hwnd, region, true) != 0)
                {
                    // On success, the OS owns the HRGN. Do not delete it.
                    region = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to clip native terminal region: {ex.Message}");
            }
            finally
            {
                if (region != IntPtr.Zero)
                {
                    DeleteObject(region);
                }
            }
        }

        public void StopAllSessions()
        {
            foreach (var session in _terminalSessions.ToList())
            {
                StopTerminalSession(session);
            }

            _terminalSessions.Clear();
            _activeTerminalSession = null;
            ShowEmptyState();
        }

        [System.Runtime.InteropServices.DllImport("comctl32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc callback, IntPtr id, IntPtr refData);

        [System.Runtime.InteropServices.DllImport("comctl32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc callback, IntPtr id);

        [System.Runtime.InteropServices.DllImport("comctl32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr refData);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint WM_PARENTNOTIFY = 0x0210;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int VK_OEM_3 = 0xC0;
        private const int BACKQUOTE_SCAN_CODE = 0x29;
        private static readonly IntPtr ParentWindowSubclassId = new IntPtr(1001);

        private IntPtr TerminalWindowSubclass(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr refData)
        {
            if (uMsg == WM_PARENTNOTIFY)
            {
                int eventCode = (int)(wParam.ToInt64() & 0xFFFF);
                if (eventCode == WM_LBUTTONDOWN || eventCode == WM_RBUTTONDOWN || eventCode == WM_MBUTTONDOWN || eventCode == WM_XBUTTONDOWN)
                {
                    var session = _activeTerminalSession;
                    if (session != null && session.IsNative && session.WindowHandle != IntPtr.Zero)
                    {
                        if (GetCursorPos(out POINT pt) && GetWindowRect(session.WindowHandle, out RECT rect))
                        {
                            if (pt.X >= rect.Left && pt.X <= rect.Right && pt.Y >= rect.Top && pt.Y <= rect.Bottom)
                            {
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    FocusActiveTerminal();
                                });
                            }
                        }
                    }
                }
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void RequestClose()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#133")]
        private static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_CHILD = 0x40000000;
        private const int WS_HSCROLL = 0x00100000;
        private const int WS_VSCROLL = 0x00200000;
        private const int NATIVE_TERMINAL_SCROLLBAR_CLIP_MARGIN = 30;

        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private async Task StartEmbeddedTerminalAsync(string workingDirectory)
        {
            var session = new TerminalSession(workingDirectory);
            _terminalSessions.Add(session);
            string shellExecutable = ResolvePowerShellExecutable();

            bool useNativeTerminalHost = true;
            if (!useNativeTerminalHost)
            {
                StartRedirectedTerminal(session);
                SetActiveTerminalSession(session);
                TerminalInputBox.Focus(FocusState.Programmatic);
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = shellExecutable,
                    Arguments = $"-NoExit -Command \"$Host.UI.RawUI.WindowTitle = '{session.WindowTitle}'\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                session.Process = Process.Start(startInfo);
                if (session.Process == null)
                {
                    throw new Exception("PowerShell native process failed to start.");
                }

                IntPtr childHwnd = IntPtr.Zero;
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(100);
                    childHwnd = FindWindow("ConsoleWindowClass", session.WindowTitle);
                    if (childHwnd != IntPtr.Zero)
                    {
                        break;
                    }
                }

                if (childHwnd == IntPtr.Zero)
                {
                    session.Process.Refresh();
                    childHwnd = session.Process.MainWindowHandle;
                }

                if (childHwnd == IntPtr.Zero)
                {
                    throw new Exception("Native terminal window handle could not be resolved.");
                }

                session.WindowHandle = childHwnd;
                session.IsNative = true;
                ShowWindow(session.WindowHandle, SW_HIDE);
                ApplyTerminalTheme(session.WindowHandle, this.ActualTheme == ElementTheme.Dark);

                IntPtr parentHwnd = _ownerWindow != null ? WindowNative.GetWindowHandle(_ownerWindow) : IntPtr.Zero;
                if (parentHwnd == IntPtr.Zero)
                {
                    throw new Exception("Owner window handle could not be resolved.");
                }

                SetParent(session.WindowHandle, parentHwnd);

                int style = GetWindowLong(session.WindowHandle, GWL_STYLE);
                style = (style | WS_CHILD) & ~WS_CAPTION & ~WS_THICKFRAME & ~WS_MINIMIZEBOX & ~WS_MAXIMIZEBOX & ~WS_SYSMENU & ~WS_VSCROLL & ~WS_HSCROLL;
                SetWindowLong(session.WindowHandle, GWL_STYLE, style);
                HideNativeTerminalScrollBars(session.WindowHandle);
                SetWindowPos(session.WindowHandle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOOWNERZORDER);

                SetActiveTerminalSession(session);
                await WaitForTerminalHostLayoutAsync();
                ResizeEmbeddedTerminal();
                QueueEmbeddedTerminalResize();
                QueueEmbeddedTerminalResizeAfterDelay(75);
                QueueEmbeddedTerminalResizeAfterDelay(200);

                if (_activeTerminalSession == session)
                {
                    ShowWindow(session.WindowHandle, SW_SHOW);
                    HideNativeTerminalScrollBars(session.WindowHandle);
                    FocusTerminalSession(session);
                }
                ResizeEmbeddedTerminal();
                QueueEmbeddedTerminalResize();
                QueueEmbeddedTerminalResizeAfterDelay(350);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed native terminal hosting, falling back to redirected textbox: {ex.Message}");
                try
                {
                    if (session.Process != null && !session.Process.HasExited)
                    {
                        session.Process.Kill();
                    }
                    session.Process?.Dispose();
                    session.Process = null;
                }
                catch
                {
                }

                TerminalHostBorder.Visibility = Visibility.Collapsed;
                TerminalOutputTextBox.Visibility = Visibility.Visible;
                TerminalInputAreaGrid.Visibility = Visibility.Visible;

                StartRedirectedTerminal(session, shellExecutable);
                SetActiveTerminalSession(session);
            }
        }

        private async Task WaitForTerminalHostLayoutAsync()
        {
            for (int i = 0; i < 10; i++)
            {
                if (TerminalHostBorder.ActualWidth > 8 && TerminalHostBorder.ActualHeight > 8)
                {
                    return;
                }

                await Task.Delay(30);
            }
        }

        private void StartRedirectedTerminal(TerminalSession session, string? shellExecutable = null)
        {
            session.IsNative = false;
            session.WindowHandle = IntPtr.Zero;
            AppendTerminalOutput(session, $"PowerShell 시작: {session.WorkingDirectory}");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = shellExecutable ?? ResolvePowerShellExecutable(),
                    Arguments = "-NoLogo -NoProfile -NoExit -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; [Console]::InputEncoding=[System.Text.Encoding]::UTF8; $OutputEncoding=[System.Text.Encoding]::UTF8\"",
                    WorkingDirectory = session.WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                session.Process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                session.Process.OutputDataReceived += (_, args) => AppendTerminalOutput(session, args.Data);
                session.Process.ErrorDataReceived += (_, args) => AppendTerminalOutput(session, args.Data);
                session.Process.Exited += (_, __) => AppendTerminalOutput(session, $"[{_getString?.Invoke("TerminalExited", "터미널 종료") ?? "터미널 종료"}]");

                session.Process.Start();
                session.Process.BeginOutputReadLine();
                session.Process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                AppendTerminalOutput(session, $"{_getString?.Invoke("TerminalStartFailed", "터미널을 시작하지 못했습니다") ?? "터미널을 시작하지 못했습니다"}: {ex.Message}");
            }
        }

        private void StopTerminalSession(TerminalSession session)
        {
            try
            {
                if (session.WindowHandle != IntPtr.Zero)
                {
                    ShowWindow(session.WindowHandle, SW_HIDE);
                }

                var process = session.Process;
                bool isNative = session.IsNative;

                if (process != null)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                if (!isNative)
                                {
                                    try
                                    {
                                        process.StandardInput.WriteLine("exit");
                                        process.StandardInput.Flush();
                                    }
                                    catch { }
                                }

                                if (!process.WaitForExit(100))
                                {
                                    process.Kill(entireProcessTree: true);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to terminate terminal process: {ex.Message}");
                        }
                        finally
                        {
                            try { process.Dispose(); } catch { }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in StopTerminalSession: {ex.Message}");
            }
            finally
            {
                session.WindowHandle = IntPtr.Zero;
                session.Process = null;
            }
        }


        public void ResizeEmbeddedTerminal()
        {
            if (_activeTerminalSession == null || _activeTerminalSession.WindowHandle == IntPtr.Zero || TerminalHostBorder.Visibility != Visibility.Visible)
            {
                return;
            }

            if (_ownerWindow?.Content is not UIElement ownerContent)
            {
                return;
            }

            try
            {
                UpdateLayout();

                double hostWidth = TerminalHostBorder.ActualWidth;
                double hostHeight = TerminalHostBorder.ActualHeight;
                if (hostWidth < 40 && TerminalRootGrid.ActualWidth > 80)
                {
                    hostWidth = Math.Max(0, TerminalRootGrid.ActualWidth - TerminalSessionsList.ActualWidth);
                }
                if (hostHeight < 40 && TerminalRootGrid.ActualHeight > TerminalHeaderGrid.ActualHeight)
                {
                    hostHeight = Math.Max(0, TerminalRootGrid.ActualHeight - TerminalHeaderGrid.ActualHeight);
                }

                var transform = TerminalHostBorder.TransformToVisual(ownerContent);
                var bounds = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, hostWidth, hostHeight));

                double scale = ownerContent.XamlRoot?.RasterizationScale ?? 1.0;
                int x = (int)Math.Round(bounds.X * scale);
                int y = (int)Math.Round(bounds.Y * scale);
                int width = (int)Math.Round(bounds.Width * scale);
                int height = (int)Math.Round(bounds.Height * scale);

                if (width > 0 && height > 0)
                {
                    int nativeWidth = width + NATIVE_TERMINAL_SCROLLBAR_CLIP_MARGIN;
                    int nativeHeight = height + NATIVE_TERMINAL_SCROLLBAR_CLIP_MARGIN;

                    SetWindowPos(_activeTerminalSession.WindowHandle, IntPtr.Zero, x, y, nativeWidth, nativeHeight, SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER);
                    MoveWindow(_activeTerminalSession.WindowHandle, x, y, nativeWidth, nativeHeight, true);
                    HideNativeTerminalScrollBars(_activeTerminalSession.WindowHandle);
                    ApplyNativeTerminalClipRegion(_activeTerminalSession.WindowHandle, width, height);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to resize native terminal child window: {ex.Message}");
            }
        }

        private static string ResolvePowerShellExecutable()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PowerShell", "7", "pwsh.exe"),
                FindExecutableOnPath("pwsh.exe"),
                "powershell.exe"
            };

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (candidate.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) || File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "powershell.exe";
        }

        private static string FindExecutableOnPath(string fileName)
        {
            try
            {
                string? pathValue = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrWhiteSpace(pathValue))
                {
                    return string.Empty;
                }

                foreach (string directory in pathValue.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        continue;
                    }

                    string candidate = Path.Combine(directory.Trim(), fileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        public void QueueEmbeddedTerminalResize()
        {
            if (_resizeQueued)
            {
                return;
            }

            _resizeQueued = true;
            bool queued = DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    UpdateLayout();
                    ResizeEmbeddedTerminal();
                }
                finally
                {
                    _resizeQueued = false;
                }
            });

            if (!queued)
            {
                _resizeQueued = false;
            }
        }

        private void QueueEmbeddedTerminalResizeAfterDelay(int delayMilliseconds)
        {
            _ = ResizeEmbeddedTerminalAfterDelayAsync(delayMilliseconds);
        }

        private async Task ResizeEmbeddedTerminalAfterDelayAsync(int delayMilliseconds)
        {
            await Task.Delay(delayMilliseconds);
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateLayout();
                ResizeEmbeddedTerminal();
            });
        }

        private void OnTerminalHostBorderSizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueEmbeddedTerminalResize();
        }

        private void OnTerminalHostBorderPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            FocusActiveTerminal();
        }

        private void FocusActiveTerminal()
        {
            if (_activeTerminalSession == null)
            {
                return;
            }

            FocusTerminalSession(_activeTerminalSession);
        }

        private void FocusTerminalSession(TerminalSession session)
        {
            if (!session.IsNative || session.WindowHandle == IntPtr.Zero)
            {
                TerminalInputBox.Focus(FocusState.Programmatic);
                return;
            }

            try
            {
                ShowWindow(session.WindowHandle, SW_SHOW);
                SetForegroundWindow(session.WindowHandle);
                BringWindowToTop(session.WindowHandle);
                SetActiveWindow(session.WindowHandle);
                ResizeEmbeddedTerminal();

                uint currentThread = GetCurrentThreadId();
                uint terminalThread = GetWindowThreadProcessId(session.WindowHandle, IntPtr.Zero);
                bool attached = terminalThread != 0 && terminalThread != currentThread && AttachThreadInput(currentThread, terminalThread, true);
                try
                {
                    BringWindowToTop(session.WindowHandle);
                    SetActiveWindow(session.WindowHandle);
                    SetFocus(session.WindowHandle);
                }
                finally
                {
                    if (attached)
                    {
                        AttachThreadInput(currentThread, terminalThread, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to focus native terminal: {ex.Message}");
            }
        }

        private void AppendTerminalOutput(TerminalSession session, string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            session.Output.AppendLine(text);

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_activeTerminalSession == session && !session.IsNative)
                {
                    TerminalOutputTextBox.Text = session.Output.ToString();
                    TerminalOutputTextBox.Select(TerminalOutputTextBox.Text.Length, 0);
                }
            });
        }

        private void OnTerminalInputKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (HandleTerminalToggleShortcut(e))
            {
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;

            string command = TerminalInputBox.Text;
            TerminalInputBox.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(command)) return;

            TerminalOutputTextBox.Text += $"> {command}{Environment.NewLine}";
            TerminalOutputTextBox.Select(TerminalOutputTextBox.Text.Length, 0);

            try
            {
                if (_activeTerminalSession == null)
                {
                    OpenTerminal(GetWorkingDirectoryOrDefault());
                    return;
                }

                if (_activeTerminalSession.Process == null || _activeTerminalSession.Process.HasExited)
                {
                    AppendTerminalOutput(_activeTerminalSession, $"[{_getString?.Invoke("TerminalHasExited", "터미널이 종료되었습니다") ?? "터미널이 종료되었습니다"}]");
                    return;
                }

                _activeTerminalSession.Process.StandardInput.WriteLine(command);
                _activeTerminalSession.Process.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                if (_activeTerminalSession != null)
                {
                    AppendTerminalOutput(_activeTerminalSession, $"{_getString?.Invoke("TerminalSendFailed", "명령 전송 실패") ?? "명령 전송 실패"}: {ex.Message}");
                }
            }
        }

        private void OnNewTerminalClick(object sender, RoutedEventArgs e)
        {
            OpenTerminal(GetWorkingDirectoryOrDefault());
        }

        private void OnCloseTerminalClick(object sender, RoutedEventArgs e)
        {
            RequestClose();
        }

        private void OnCloseSessionItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TerminalSession session)
            {
                CloseTerminalSession(session);
            }
        }

        private void OnTerminalSessionsListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TerminalSessionsList.SelectedItem is TerminalSession session && _activeTerminalSession != session)
            {
                SetActiveTerminalSession(session);
            }
        }

        private void SetActiveTerminalSession(TerminalSession session)
        {
            if (_activeTerminalSession != null && _activeTerminalSession.WindowHandle != IntPtr.Zero)
            {
                ShowWindow(_activeTerminalSession.WindowHandle, SW_HIDE);
            }

            _activeTerminalSession = session;
            TerminalTitleText.Text = $"{_getString?.Invoke("TerminalTitle", "터미널") ?? "터미널"} - {session.WorkingDirectory}";

            if (TerminalSessionsList.SelectedItem != session)
            {
                TerminalSessionsList.SelectedItem = session;
            }

            if (session.IsNative && session.WindowHandle != IntPtr.Zero)
            {
                TerminalHostBorder.Visibility = Visibility.Visible;
                TerminalOutputTextBox.Visibility = Visibility.Collapsed;
                TerminalInputAreaGrid.Visibility = Visibility.Collapsed;
                ShowWindow(session.WindowHandle, SW_SHOW);
                HideNativeTerminalScrollBars(session.WindowHandle);
                QueueEmbeddedTerminalResize();
                QueueEmbeddedTerminalResizeAfterDelay(75);
                FocusTerminalSession(session);
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(50);
                    if (_activeTerminalSession == session)
                    {
                        ResizeEmbeddedTerminal();
                        QueueEmbeddedTerminalResizeAfterDelay(150);
                        FocusTerminalSession(session);
                    }
                });
            }
            else
            {
                TerminalHostBorder.Visibility = Visibility.Collapsed;
                TerminalOutputTextBox.Visibility = Visibility.Visible;
                TerminalInputAreaGrid.Visibility = Visibility.Visible;
                TerminalOutputTextBox.Text = session.Output.ToString();
                TerminalInputBox.Focus(FocusState.Programmatic);
            }
        }

        private void ApplyTerminalTheme(IntPtr hwnd, bool isDark)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int useDark = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
                AllowDarkModeForWindow(hwnd, isDark);
                SetWindowTheme(hwnd, isDark ? "DarkMode_Explorer" : "Explorer", null);
                
                // Force non-client area redraw (scrollbars)
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOOWNERZORDER);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to apply terminal theme: {ex.Message}");
            }
        }

        public void UpdateAllTerminalThemes()
        {
            bool isDark = this.ActualTheme == ElementTheme.Dark;
            foreach (var session in _terminalSessions)
            {
                if (session.IsNative && session.WindowHandle != IntPtr.Zero)
                {
                    ApplyTerminalTheme(session.WindowHandle, isDark);
                }
            }
        }

        private void CloseTerminalSession(TerminalSession session)
        {
            int index = _terminalSessions.IndexOf(session);
            StopTerminalSession(session);
            _terminalSessions.Remove(session);

            if (_terminalSessions.Count == 0)
            {
                _activeTerminalSession = null;
                ShowEmptyState();
                SessionsEmptied?.Invoke(this, EventArgs.Empty);
                return;
            }

            int nextIndex = Math.Clamp(index, 0, _terminalSessions.Count - 1);
            SetActiveTerminalSession(_terminalSessions[nextIndex]);
        }

        private string GetWorkingDirectoryOrDefault()
        {
            string? workingDirectory = WorkingDirectoryProvider?.Invoke();
            if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            {
                return workingDirectory;
            }

            if (_activeTerminalSession != null && Directory.Exists(_activeTerminalSession.WorkingDirectory))
            {
                return _activeTerminalSession.WorkingDirectory;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private void ShowEmptyState()
        {
            TerminalHostBorder.Visibility = Visibility.Collapsed;
            TerminalOutputTextBox.Visibility = Visibility.Visible;
            TerminalInputAreaGrid.Visibility = Visibility.Visible;
            TerminalTitleText.Text = _getString?.Invoke("TerminalTitle", "터미널") ?? "터미널";
            TerminalOutputTextBox.Text = string.Empty;
        }

        private void OnTerminalPaneKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            HandleTerminalToggleShortcut(e);
        }

        private bool HandleTerminalToggleShortcut(Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            if (ctrl && ((int)e.Key == VK_OEM_3 || (int)e.KeyStatus.ScanCode == BACKQUOTE_SCAN_CODE))
            {
                e.Handled = true;
                RequestClose();
                return true;
            }

            return false;
        }
    }
}
