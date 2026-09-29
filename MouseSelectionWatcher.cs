using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CapsIndicator
{
    // Watches every left/right click system-wide and reports the ones that
    // land neither on an indicator window nor on the Settings window, so the
    // owner can clear the current selection the same way clicking empty
    // desktop space deselects an icon.
    internal static class MouseSelectionWatcher
    {
        private static IntPtr _hookId = IntPtr.Zero;
        private static readonly Native.LowLevelKeyboardProc _proc = HookCallback;

        // Supplied by the owner: true if the given screen point falls on an
        // indicator window or the Settings window (i.e. NOT an "outside" click).
        public static Func<Point, bool> IsProtectedPoint;
        public static event Action OutsideClick;

        public static void Install()
        {
            if (_hookId != IntPtr.Zero) return;
            using (System.Diagnostics.Process curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (System.Diagnostics.ProcessModule curModule = curProcess.MainModule)
            {
                _hookId = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc,
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
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN)
                {
                    Native.MSLLHOOKSTRUCT info =
                        (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    Point pt = new Point(info.pt.x, info.pt.y);

                    Func<Point, bool> isProtected = IsProtectedPoint;
                    if (isProtected != null && !isProtected(pt))
                    {
                        Action handler = OutsideClick;
                        if (handler != null) handler();
                    }
                }
            }

            return Native.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
}
