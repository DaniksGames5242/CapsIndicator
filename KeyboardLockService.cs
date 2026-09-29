using System;
using System.Runtime.InteropServices;

namespace CapsIndicator
{
    // A single low-level keyboard hook backing two independent locks:
    //  - CapsLockLocked: eats every Caps Lock key event, so the physical key
    //    stops toggling the OS state at all while the lock is on.
    //  - LayoutSwitchLocked: eats the second key of the three standard
    //    Windows layout-switch chords (Alt+Shift, Ctrl+Shift, Win+Space) so
    //    the OS never sees the combo form and never switches the layout.
    // Both flags default to off and only take effect once explicitly turned
    // on (settings checkbox or hotkey) — the hook itself is installed for
    // the whole app lifetime but is a no-op while both flags are false.
    internal static class KeyboardLockService
    {
        private static IntPtr _hookId = IntPtr.Zero;
        // Kept as a field so the delegate is never garbage-collected while
        // the native hook still holds a pointer to it.
        private static readonly Native.LowLevelKeyboardProc _proc = HookCallback;

        public static bool CapsLockLocked;
        public static bool LayoutSwitchLocked;

        public static void Install()
        {
            if (_hookId != IntPtr.Zero) return;
            using (System.Diagnostics.Process curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (System.Diagnostics.ProcessModule curModule = curProcess.MainModule)
            {
                _hookId = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc,
                    Native.GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        public static void Uninstall()
        {
            if (_hookId == IntPtr.Zero) return;
            Native.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (CapsLockLocked || LayoutSwitchLocked))
            {
                int msg = wParam.ToInt32();
                bool isDown = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                bool isUp = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;

                if (isDown || isUp)
                {
                    Native.KBDLLHOOKSTRUCT info =
                        (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    int vk = (int)info.vkCode;

                    if (CapsLockLocked && vk == Native.VK_CAPITAL)
                    {
                        return (IntPtr)1;
                    }

                    if (LayoutSwitchLocked && isDown && IsLayoutSwitchChord(vk))
                    {
                        return (IntPtr)1;
                    }
                }
            }

            return Native.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        // Only the key that *completes* a switch chord needs blocking — the
        // OS never recognizes the combo if the second key never arrives.
        private static bool IsLayoutSwitchChord(int vk)
        {
            bool altDown = IsDown(Native.VK_MENU);
            bool ctrlDown = IsDown(Native.VK_CONTROL);
            bool shiftDown = IsDown(Native.VK_SHIFT);
            bool winDown = IsDown(Native.VK_LWIN) || IsDown(Native.VK_RWIN);

            bool isShift = vk == Native.VK_SHIFT || vk == Native.VK_LSHIFT || vk == Native.VK_RSHIFT;
            bool isAlt = vk == Native.VK_MENU || vk == Native.VK_LMENU || vk == Native.VK_RMENU;
            bool isCtrl = vk == Native.VK_CONTROL || vk == Native.VK_LCONTROL || vk == Native.VK_RCONTROL;
            bool isSpace = vk == Native.VK_SPACE;

            if (isShift && (altDown || ctrlDown)) return true;
            if ((isAlt || isCtrl) && shiftDown) return true;
            if (isSpace && winDown) return true;
            return false;
        }

        private static bool IsDown(int vk)
        {
            return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
        }
    }
}
