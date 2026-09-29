using System;
using System.IO;

namespace CapsIndicator
{
    // Toggle-sound codes stored per window (WindowConfig.SoundKind).
    internal static class SoundKind
    {
        public const int None = 0;
        public const int Builtin = 1;
        public const int File = 2;
    }

    // Plays the per-window toggle sound, honoring a global mute flag and
    // volume level shared by every indicator. Built on MCI (winmm) rather
    // than System.Media.SoundPlayer so that non-wav files the user might pick
    // (mp3, wma) also play — MCI picks a driver from the file extension.
    internal static class SoundService
    {
        private const string Alias = "capsIndicatorSound";

        public static bool Muted;
        public static int Volume = 100; // 0..100

        public struct BuiltinSound
        {
            public readonly string Id;
            public readonly string Name;
            public readonly string FileName;
            public BuiltinSound(string id, string name, string fileName)
            {
                Id = id; Name = name; FileName = fileName;
            }
        }

        // A handful of the notification .wav files Windows has shipped under
        // %WINDIR%\Media since the 9x days and still carries today, purely so
        // "built-in" sounds need no assets of our own.
        public static readonly BuiltinSound[] Builtins = new BuiltinSound[]
        {
            new BuiltinSound("ding", "Дзинь", "ding.wav"),
            new BuiltinSound("chord", "Аккорд", "chord.wav"),
            new BuiltinSound("chimes", "Колокольчики", "chimes.wav"),
            new BuiltinSound("notify", "Уведомление", "notify.wav"),
            new BuiltinSound("tada", "Та-да", "tada.wav"),
            new BuiltinSound("ringout", "Звонок", "ringout.wav"),
            new BuiltinSound("recycle", "Клик", "recycle.wav"),
        };

        public static string NameFor(string builtinId)
        {
            foreach (BuiltinSound b in Builtins)
            {
                if (b.Id == builtinId) return b.Name;
            }
            return builtinId;
        }

        public static string BuiltinPath(string builtinId)
        {
            foreach (BuiltinSound b in Builtins)
            {
                if (b.Id == builtinId)
                {
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", b.FileName);
                }
            }
            return null;
        }

        public static string ResolvePath(int kind, string builtinId, string filePath)
        {
            if (kind == SoundKind.Builtin) return BuiltinPath(builtinId);
            if (kind == SoundKind.File) return filePath;
            return null;
        }

        // Fired on every indicator toggle — respects Mute/Volume.
        public static void Play(int kind, string builtinId, string filePath)
        {
            if (Muted || Volume <= 0) return;
            PlayInternal(kind, builtinId, filePath);
        }

        // Used by the Settings "Test" button — always audible so picking a
        // sound while muted (or at 0%) doesn't look broken.
        public static void PlayPreview(int kind, string builtinId, string filePath)
        {
            PlayInternal(kind, builtinId, filePath);
        }

        private static void PlayInternal(int kind, string builtinId, string filePath)
        {
            if (kind == SoundKind.None) return;
            string path = ResolvePath(kind, builtinId, filePath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                uint level = (uint)Math.Max(0, Math.Min(100, Volume)) * 0xFFFFu / 100u;
                Native.waveOutSetVolume(IntPtr.Zero, level | (level << 16));
            }
            catch { }

            try { Native.mciSendString("close " + Alias, null, 0, IntPtr.Zero); }
            catch { }

            try
            {
                int rc = Native.mciSendString("open \"" + path + "\" alias " + Alias, null, 0, IntPtr.Zero);
                if (rc != 0) return;
                // Only digitalvideo/mpegvideo-backed MCI drivers (typically
                // used for mp3/wma) honor this — waveaudio (plain .wav)
                // ignores it, which is why waveOutSetVolume above is set too.
                int vol1000 = Math.Max(0, Math.Min(1000, Volume * 10));
                Native.mciSendString("setaudio " + Alias + " volume to " + vol1000, null, 0, IntPtr.Zero);
                Native.mciSendString("play " + Alias, null, 0, IntPtr.Zero);
            }
            catch { }
        }

        public static void Shutdown()
        {
            try { Native.mciSendString("close " + Alias, null, 0, IntPtr.Zero); }
            catch { }
        }
    }
}
