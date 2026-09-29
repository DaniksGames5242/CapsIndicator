using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CapsIndicator
{
    internal sealed class HotkeyBinding
    {
        public bool Control;
        public bool Shift;
        public bool Alt;
        public uint VirtualKey; // 0 = unassigned

        public bool IsAssigned { get { return VirtualKey != 0; } }

        public HotkeyBinding Clone()
        {
            HotkeyBinding b = new HotkeyBinding();
            b.Control = Control; b.Shift = Shift; b.Alt = Alt; b.VirtualKey = VirtualKey;
            return b;
        }

        public void Clear()
        {
            Control = false; Shift = false; Alt = false; VirtualKey = 0;
        }

        public uint Modifiers()
        {
            uint m = Native.MOD_NOREPEAT;
            if (Control) m |= Native.MOD_CONTROL;
            if (Shift) m |= Native.MOD_SHIFT;
            if (Alt) m |= Native.MOD_ALT;
            return m;
        }

        public string DisplayText()
        {
            if (!IsAssigned) return "не задано";
            string s = "";
            if (Control) s += "Ctrl+";
            if (Shift) s += "Shift+";
            if (Alt) s += "Alt+";
            s += KeyName(VirtualKey);
            return s;
        }

        public static string KeyName(uint vk)
        {
            System.Windows.Forms.Keys k = (System.Windows.Forms.Keys)vk;
            return k.ToString();
        }

        public string Serialize()
        {
            return (Control ? 1 : 0) + "," + (Shift ? 1 : 0) + "," + (Alt ? 1 : 0) + "," + VirtualKey;
        }

        public static HotkeyBinding Deserialize(string s)
        {
            HotkeyBinding b = new HotkeyBinding();
            try
            {
                string[] parts = s.Split(',');
                if (parts.Length == 4)
                {
                    b.Control = parts[0] == "1";
                    b.Shift = parts[1] == "1";
                    b.Alt = parts[2] == "1";
                    b.VirtualKey = uint.Parse(parts[3]);
                }
            }
            catch
            {
                b.Clear();
            }
            return b;
        }
    }

    // The full catalogue of assignable actions. Every one of them is fully
    // optional — an unassigned entry simply never fires, nothing else in the
    // app depends on any of them being set.
    internal static class HotkeyCatalog
    {
        public const string ToggleVisibility = "toggleVisibility";
        public const string OpenSettings = "openSettings";
        public const string CloseApp = "closeApp";
        public const string ToggleAutostart = "toggleAutostart";
        public const string SelectNext = "selectNext";
        public const string SelectPrev = "selectPrev";
        public const string DeselectAll = "deselectAll";
        public const string CycleStyleSelected = "cycleStyleSelected";
        public const string CycleStyleSelectedBack = "cycleStyleSelectedBack";
        public const string CycleSourceSelected = "cycleSourceSelected";
        public const string ToggleVisibilitySelected = "toggleVisibilitySelected";
        public const string ToggleCaseSelectedText = "toggleCaseSelectedText";
        public const string FixLayoutSelectedText = "fixLayoutSelectedText";
        public const string ToggleLayoutFixMode = "toggleLayoutFixMode";
        public const string ToggleCapsLockLock = "toggleCapsLockLock";
        public const string ToggleLayoutSwitchLock = "toggleLayoutSwitchLock";
        public const string ToggleMute = "toggleMute";

        public struct Entry
        {
            public string Id;
            public string Name;
            public Entry(string id, string name) { Id = id; Name = name; }
        }

        public static readonly Entry[] All = new Entry[]
        {
            new Entry(ToggleVisibility, "Показать/скрыть все индикаторы"),
            new Entry(OpenSettings, "Открыть настройки"),
            new Entry(CloseApp, "Закрыть приложение"),
            new Entry(ToggleAutostart, "Вкл/выкл автозапуск"),
            new Entry(SelectNext, "Выбрать следующий индикатор"),
            new Entry(SelectPrev, "Выбрать предыдущий индикатор"),
            new Entry(DeselectAll, "Снять выделение"),
            new Entry(CycleStyleSelected, "Следующий стиль выбранного"),
            new Entry(CycleStyleSelectedBack, "Предыдущий стиль выбранного"),
            new Entry(CycleSourceSelected, "Сменить источник выбранного"),
            new Entry(ToggleVisibilitySelected, "Показать/скрыть выбранный"),
            new Entry(ToggleCaseSelectedText, "Исправить регистр выделенного текста"),
            new Entry(FixLayoutSelectedText, "Исправить раскладку выделенного текста"),
            new Entry(ToggleLayoutFixMode, "Переключить режим исправления раскладки"),
            new Entry(ToggleCapsLockLock, "Вкл/выкл блокировку Caps Lock"),
            new Entry(ToggleLayoutSwitchLock, "Вкл/выкл блокировку смены раскладки"),
            new Entry(ToggleMute, "Вкл/выкл звук всех индикаторов"),
        };
    }

    internal sealed class WindowConfig
    {
        public IndicatorSource Source = IndicatorSource.CapsLock;
        public Style Style = Style.Full;
        public int X = -1;
        public int Y = -1;
        public int W = 190;
        public int H = 74;
        public bool ShowCapsLockLockBadge;
        public bool ShowLayoutSwitchLockBadge;
        public int SoundKind;
        public string SoundBuiltinId = "";
        public string SoundFilePath = "";
    }

    internal sealed class AppConfig
    {
        public bool Autostart;
        public bool SaveWindowPositions = true;
        public bool SaveStyles = true;
        public bool SaveSizes = true;
        public bool ShowDragFrame = true;
        public bool IndicatorsTopmost = true;
        public bool AutoStartIndicatorsWithApp = true;
        public int TargetFps = 60;
        public bool LayoutFixUniversal = true;
        public bool CapsLockLockEnabled;
        public bool LayoutSwitchLockEnabled;
        public bool SoundMuted;
        public int SoundVolume = 100;

        // All hotkeys start unassigned — nothing is auto-bound on first run,
        // and every one of them can be cleared back to that state at any time.
        public readonly Dictionary<string, HotkeyBinding> Hotkeys = new Dictionary<string, HotkeyBinding>();

        public List<WindowConfig> Windows = new List<WindowConfig>();

        public AppConfig()
        {
            foreach (HotkeyCatalog.Entry entry in HotkeyCatalog.All)
            {
                Hotkeys[entry.Id] = new HotkeyBinding();
            }
        }

        public HotkeyBinding GetHotkey(string id)
        {
            HotkeyBinding b;
            if (Hotkeys.TryGetValue(id, out b)) return b;
            b = new HotkeyBinding();
            Hotkeys[id] = b;
            return b;
        }

        public static string DefaultPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.cfg");
        }

        public static AppConfig CreateDefault()
        {
            AppConfig c = new AppConfig();
            WindowConfig w = new WindowConfig();
            w.Source = IndicatorSource.CapsLock;
            w.Style = Style.Full;
            c.Windows.Add(w);
            return c;
        }

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path)) return null;

            AppConfig c = new AppConfig();
            c.Windows.Clear();
            string section = "";

            try
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).ToLowerInvariant();
                        continue;
                    }

                    if (section == "general")
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 0) continue;
                        string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string val = line.Substring(eq + 1).Trim();
                        if (key == "targetfps")
                        {
                            int fps;
                            if (int.TryParse(val, out fps) && fps > 0) c.TargetFps = fps;
                            continue;
                        }
                        if (key == "soundvolume")
                        {
                            int vol;
                            if (int.TryParse(val, out vol)) c.SoundVolume = Math.Max(0, Math.Min(100, vol));
                            continue;
                        }
                        bool b = val == "1";
                        switch (key)
                        {
                            case "autostart": c.Autostart = b; break;
                            case "savewindowpositions": c.SaveWindowPositions = b; break;
                            case "savestyles": c.SaveStyles = b; break;
                            case "savesizes": c.SaveSizes = b; break;
                            case "showdragframe": c.ShowDragFrame = b; break;
                            case "indicatorstopmost": c.IndicatorsTopmost = b; break;
                            case "autostartindicatorswithapp": c.AutoStartIndicatorsWithApp = b; break;
                            case "layoutfixuniversal": c.LayoutFixUniversal = b; break;
                            case "capslocklockenabled": c.CapsLockLockEnabled = b; break;
                            case "layoutswitchlockenabled": c.LayoutSwitchLockEnabled = b; break;
                            case "soundmuted": c.SoundMuted = b; break;
                        }
                    }
                    else if (section == "hotkeys")
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 0) continue;
                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();
                        // Unknown ids (e.g. from an older/newer version) are
                        // just stored as-is so they round-trip harmlessly;
                        // known ids get parsed for the app to actually use.
                        c.Hotkeys[key] = HotkeyBinding.Deserialize(val);
                    }
                    else if (section == "windows")
                    {
                        // A single stale/unrecognized line (e.g. a source type
                        // removed in a later version) must not take down the
                        // rest of the config — skip just that line.
                        try
                        {
                            string[] parts = line.Split('|');
                            if (parts.Length != 6 && parts.Length != 8 && parts.Length != 11) continue;
                            WindowConfig w = new WindowConfig();
                            w.Source = (IndicatorSource)Enum.Parse(typeof(IndicatorSource), parts[0]);
                            w.Style = (Style)int.Parse(parts[1], CultureInfo.InvariantCulture);
                            w.X = int.Parse(parts[2], CultureInfo.InvariantCulture);
                            w.Y = int.Parse(parts[3], CultureInfo.InvariantCulture);
                            w.W = int.Parse(parts[4], CultureInfo.InvariantCulture);
                            w.H = int.Parse(parts[5], CultureInfo.InvariantCulture);
                            if (parts.Length >= 8)
                            {
                                w.ShowCapsLockLockBadge = parts[6] == "1";
                                w.ShowLayoutSwitchLockBadge = parts[7] == "1";
                            }
                            if (parts.Length == 11)
                            {
                                int kind;
                                int.TryParse(parts[8], out kind);
                                w.SoundKind = kind;
                                w.SoundBuiltinId = parts[9];
                                // '|' is not a legal character in a Windows path, so it can
                                // never appear inside a real file path and split it further.
                                w.SoundFilePath = parts[10];
                            }
                            c.Windows.Add(w);
                        }
                        catch
                        {
                            // skip malformed/unrecognized window entry
                        }
                    }
                }
            }
            catch
            {
                return null;
            }

            return c;
        }

        public bool Save(string path)
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("[general]");
                sb.AppendLine("autostart=" + (Autostart ? 1 : 0));
                sb.AppendLine("saveWindowPositions=" + (SaveWindowPositions ? 1 : 0));
                sb.AppendLine("saveStyles=" + (SaveStyles ? 1 : 0));
                sb.AppendLine("saveSizes=" + (SaveSizes ? 1 : 0));
                sb.AppendLine("showDragFrame=" + (ShowDragFrame ? 1 : 0));
                sb.AppendLine("indicatorsTopmost=" + (IndicatorsTopmost ? 1 : 0));
                sb.AppendLine("autoStartIndicatorsWithApp=" + (AutoStartIndicatorsWithApp ? 1 : 0));
                sb.AppendLine("layoutFixUniversal=" + (LayoutFixUniversal ? 1 : 0));
                sb.AppendLine("capsLockLockEnabled=" + (CapsLockLockEnabled ? 1 : 0));
                sb.AppendLine("layoutSwitchLockEnabled=" + (LayoutSwitchLockEnabled ? 1 : 0));
                sb.AppendLine("soundMuted=" + (SoundMuted ? 1 : 0));
                sb.AppendLine("soundVolume=" + SoundVolume);
                sb.AppendLine("targetFps=" + TargetFps);
                sb.AppendLine();
                sb.AppendLine("[hotkeys]");
                foreach (KeyValuePair<string, HotkeyBinding> kv in Hotkeys)
                {
                    sb.AppendLine(kv.Key + "=" + kv.Value.Serialize());
                }
                sb.AppendLine();
                sb.AppendLine("[windows]");
                foreach (WindowConfig w in Windows)
                {
                    sb.AppendLine(w.Source + "|" + (int)w.Style + "|" + w.X + "|" + w.Y + "|" + w.W + "|" + w.H
                        + "|" + (w.ShowCapsLockLockBadge ? 1 : 0) + "|" + (w.ShowLayoutSwitchLockBadge ? 1 : 0)
                        + "|" + w.SoundKind + "|" + w.SoundBuiltinId + "|" + w.SoundFilePath);
                }

                File.WriteAllText(path, sb.ToString());
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
