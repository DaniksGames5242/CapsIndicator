using System;
using System.Globalization;
using System.Windows.Forms;

namespace CapsIndicator
{
    internal enum IndicatorSource
    {
        CapsLock = 0,
        NumLock = 1,
        ScrollLock = 2,
        Language = 3
    }

    internal struct IndicatorLabels
    {
        public string Caption; // long form, e.g. "CAPS LOCK"
        public string Short;   // short form, e.g. "CAPS", or a language code
        public string Primary; // main state word, e.g. "ON" / "OFF" / "EN"
        public string Glyph;   // 1-2 char symbol for tight/round styles
    }

    internal static class SourceCatalog
    {
        public static readonly IndicatorSource[] All = new IndicatorSource[]
        {
            IndicatorSource.CapsLock, IndicatorSource.NumLock, IndicatorSource.ScrollLock,
            IndicatorSource.Language
        };

        public static string Name(IndicatorSource s)
        {
            switch (s)
            {
                case IndicatorSource.CapsLock: return "Caps Lock";
                case IndicatorSource.NumLock: return "Num Lock";
                case IndicatorSource.ScrollLock: return "Scroll Lock";
                case IndicatorSource.Language: return "Раскладка клавиатуры";
            }
            return s.ToString();
        }

        public static bool IsBinary(IndicatorSource s)
        {
            return s != IndicatorSource.Language;
        }

        // Returns whether the source is in its "active/lit" state. For Language
        // (which has no on/off), dynamicValue carries the current code and the
        // return is always true so it always renders in the "lit" style variant.
        public static bool GetActive(IndicatorSource s, out string dynamicValue)
        {
            dynamicValue = null;
            switch (s)
            {
                case IndicatorSource.CapsLock:
                    return Control.IsKeyLocked(Keys.CapsLock);
                case IndicatorSource.NumLock:
                    return Control.IsKeyLocked(Keys.NumLock);
                case IndicatorSource.ScrollLock:
                    return Control.IsKeyLocked(Keys.Scroll);
                case IndicatorSource.Language:
                    dynamicValue = GetCurrentLanguageCode();
                    return true;
            }
            return false;
        }

        public static IndicatorLabels GetLabels(IndicatorSource s, bool active, string dynamicValue)
        {
            IndicatorLabels l = new IndicatorLabels();
            switch (s)
            {
                case IndicatorSource.CapsLock:
                    l.Caption = "CAPS LOCK"; l.Short = "CAPS"; l.Primary = active ? "ON" : "OFF"; l.Glyph = "⇪";
                    break;
                case IndicatorSource.NumLock:
                    l.Caption = "NUM LOCK"; l.Short = "NUM"; l.Primary = active ? "ON" : "OFF"; l.Glyph = "#";
                    break;
                case IndicatorSource.ScrollLock:
                    l.Caption = "SCROLL LOCK"; l.Short = "SCRL"; l.Primary = active ? "ON" : "OFF"; l.Glyph = "⇳";
                    break;
                case IndicatorSource.Language:
                    string code = string.IsNullOrEmpty(dynamicValue) ? "??" : dynamicValue;
                    l.Caption = "РАСКЛАДКА"; l.Short = code; l.Primary = code; l.Glyph = code;
                    break;
                default:
                    l.Caption = ""; l.Short = ""; l.Primary = ""; l.Glyph = "";
                    break;
            }
            return l;
        }

        private static string GetCurrentLanguageCode()
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                uint pid;
                uint tid = Native.GetWindowThreadProcessId(fg, out pid);
                IntPtr hkl = Native.GetKeyboardLayout(tid);
                int langId = (int)((long)hkl & 0xFFFF);
                CultureInfo culture = new CultureInfo(langId);
                return culture.TwoLetterISOLanguageName.ToUpperInvariant();
            }
            catch
            {
                return "??";
            }
        }
    }
}
