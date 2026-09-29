namespace CapsIndicator
{
    internal static class StyleCatalog
    {
        public static readonly string[] Names = new string[]
        {
            "Полная панель",   // Full
            "Компактная",      // Compact
            "Капсула",         // Pill
            "Кольцо",          // Ring
            "Точка",           // Dot
            "Контур",          // Outline
            "Терминал",        // Terminal
            "Радар",           // Radar
            "Жидкость",        // Liquid
            "Орбита",          // Orbit
            "Матрица",         // Matrix
            "Эквалайзер",      // Equalizer
            "Неон",            // Neon
            "Спидометр",       // Gauge
            "Лампочка",        // Led
            "Только текст",    // Text
            "Значок 3D",       // Badge3D
            "Куб 3D",          // Cube3D
            "Сфера 3D",        // Sphere3D
            "Прожектор",       // Chip3D (mouse-tracking spotlight)
            "HUD",             // Hud
            "Рябь",            // Ripple
            "Наклон жидкости", // TiltLiquid
            "Печать",          // Typing
            "Пульс",           // Heartbeat
            "Конфетти",        // Confetti
            "Компас",          // Compass
            "Осциллограф",     // Oscilloscope
            "Кристалл",        // Crystal
            "Звёзды"           // Starfield
        };

        public static string Name(Style s)
        {
            int i = (int)s;
            if (i < 0 || i >= Names.Length) return s.ToString();
            return Names[i];
        }

        // Styles that render no text at all (pure shape/icon/animation) can't
        // usefully show a keyboard-layout code, so sources with a meaningful
        // value to display (like Language) restrict their style picker to
        // text-capable styles only.
        private static readonly bool[] TextCapable = new bool[]
        {
            true,  // Full
            true,  // Compact
            true,  // Pill
            true,  // Ring (falls back to drawing the glyph/code as text)
            false, // Dot
            true,  // Outline
            true,  // Terminal
            false, // Radar
            true,  // Liquid
            false, // Orbit
            false, // Matrix
            false, // Equalizer
            true,  // Neon
            true,  // Gauge
            false, // Led
            true,  // Text
            true,  // Badge3D
            false, // Cube3D
            false, // Sphere3D
            true,  // Chip3D
            true,  // Hud
            true,  // Ripple
            true,  // TiltLiquid
            false, // Typing
            false, // Heartbeat
            true,  // Confetti
            false, // Compass
            false, // Oscilloscope
            false, // Crystal
            true   // Starfield
        };

        public static bool HasText(Style s)
        {
            int i = (int)s;
            if (i < 0 || i >= TextCapable.Length) return true;
            return TextCapable[i];
        }
    }
}
