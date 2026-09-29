using System.Windows.Forms;
using Microsoft.Win32;

namespace CapsIndicator
{
    internal static class AutostartHelper
    {
        private const string ValueName = "CapsIndicator";
        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return false;
                    if (enabled)
                    {
                        key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --autostart");
                    }
                    else
                    {
                        if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
