using System;

namespace QuizLock
{
    /// <summary>
    /// Installs a low-level keyboard hook and swallows the specific combos used
    /// to escape a foreground window (Win key, Alt+Tab, Alt+F4, Ctrl+Esc).
    ///
    /// Note: Ctrl+Alt+Delete is handled by Windows below any hook and can never
    /// be intercepted here. That is intentional - it's the built-in escape
    /// hatch Windows itself guarantees, so the user is never truly trapped
    /// even if this app misbehaves.
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        private IntPtr _hookId = IntPtr.Zero;
        // Keep a reference so the delegate is never garbage collected while the hook is live.
        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private bool _disposed;

        public KeyboardHook()
        {
            _proc = HookCallback;
        }

        public void Install()
        {
            if (_hookId != IntPtr.Zero) return;
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _hookId = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _proc,
                NativeMethods.GetModuleHandle(curModule.ModuleName),
                0);

            if (_hookId == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Failed to install keyboard hook. Try running QuizLock as Administrator.");
            }
        }

        public void Uninstall()
        {
            if (_hookId == IntPtr.Zero) return;
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
            {
                var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int vk = data.vkCode;

                bool altDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
                bool ctrlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;

                bool isWinKey = vk == NativeMethods.VK_LWIN || vk == NativeMethods.VK_RWIN;
                bool isAltTab = altDown && vk == NativeMethods.VK_TAB;
                bool isAltF4 = altDown && vk == NativeMethods.VK_F4;
                bool isCtrlEsc = ctrlDown && vk == NativeMethods.VK_ESCAPE;

                if (isWinKey || isAltTab || isAltF4 || isCtrlEsc)
                {
                    // Swallow it: return non-zero without calling CallNextHookEx.
                    return (IntPtr)1;
                }
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Uninstall();
            _disposed = true;
        }
    }
}
