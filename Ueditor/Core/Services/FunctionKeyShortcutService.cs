using System;
using Microsoft.UI.Xaml;

namespace Ueditor.Core.Services
{
    public sealed class FunctionKeyShortcutService
    {
        public const int F9VirtualKey = 0x78;
        public const int F10VirtualKey = 0x79;
        public const int F12VirtualKey = 0x7B;

        private const short KeyDownMask = unchecked((short)0x8000);

        private readonly DispatcherTimer _pollTimer;
        private readonly IntPtr _ownerHwnd;
        private bool _f9WasDown;
        private bool _f10WasDown;
        private bool _f12WasDown;

        public FunctionKeyShortcutService(IntPtr ownerHwnd)
        {
            _ownerHwnd = ownerHwnd;
            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _pollTimer.Tick += OnPollTimerTick;
        }

        public event EventHandler? TopMostRequested;
        public event EventHandler? ThemeRequested;
        public event EventHandler? StickyNoteRequested;

        public void Start()
        {
            ResetPressedState();
            if (!_pollTimer.IsEnabled)
            {
                _pollTimer.Start();
            }
        }

        public void Stop()
        {
            ResetPressedState();
            if (_pollTimer.IsEnabled)
            {
                _pollTimer.Stop();
            }
        }

        public void SuppressUntilReleased(int virtualKey)
        {
            switch (virtualKey)
            {
                case F9VirtualKey:
                    _f9WasDown = true;
                    break;
                case F10VirtualKey:
                    _f10WasDown = true;
                    break;
                case F12VirtualKey:
                    _f12WasDown = true;
                    break;
            }
        }

        private void OnPollTimerTick(object? sender, object e)
        {
            if (!IsAppForeground())
            {
                ResetPressedState();
                return;
            }

            PollKey(F9VirtualKey, ref _f9WasDown, TopMostRequested);
            PollKey(F10VirtualKey, ref _f10WasDown, ThemeRequested);
            PollKey(F12VirtualKey, ref _f12WasDown, StickyNoteRequested);
        }

        private static void PollKey(int virtualKey, ref bool wasDown, EventHandler? requested)
        {
            bool isDown = IsAsyncKeyDown(virtualKey);
            if (isDown && !wasDown)
            {
                requested?.Invoke(null, EventArgs.Empty);
            }

            wasDown = isDown;
        }

        private void ResetPressedState()
        {
            _f9WasDown = false;
            _f10WasDown = false;
            _f12WasDown = false;
        }

        private bool IsAppForeground()
        {
            if (_ownerHwnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr foregroundHwnd = GetForegroundWindow();
            return foregroundHwnd == _ownerHwnd || IsChild(_ownerHwnd, foregroundHwnd);
        }

        private static bool IsAsyncKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & KeyDownMask) != 0;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
    }
}
