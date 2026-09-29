using System;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace CapsIndicator
{
    internal static class EntryPoint
    {
        [STAThread]
        private static void Main()
        {
            SetDpiAware();

            if (!IsRunningAsAdministrator())
            {
                MessageBox.Show(
                    "CapsIndicator должен быть запущен от имени администратора, иначе он не сможет отслеживать состояние клавиатуры в некоторых приложениях.\n\nЗапустите программу через \"Запуск от имени администратора\".",
                    "Требуются права администратора",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            bool autostart = false;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--autostart", StringComparison.OrdinalIgnoreCase))
                {
                    autostart = true;
                }
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "CapsIndicator.SingleInstance.Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    // Already running: ask the existing instance to surface its
                    // Settings window instead of silently no-op'ing or opening
                    // a second copy.
                    Native.PostMessage(Native.HWND_BROADCAST, Native.WM_SHOW_SETTINGS, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                SettingsForm form = new SettingsForm(autostart);
                Application.Run();
                GC.KeepAlive(form);
            }
        }

        // Must run before any window/handle is created — see the note on
        // SetProcessDpiAwarenessContext in Win32.cs for why this matters.
        private static void SetDpiAware()
        {
            try
            {
                if (Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                {
                    return;
                }
            }
            catch
            {
                // Entry point missing on pre-1703 Windows — fall through.
            }

            try { Native.SetProcessDPIAware(); }
            catch { /* Even the legacy API is unavailable — proceed unaware rather than crash. */ }
        }

        private static bool IsRunningAsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }
}
