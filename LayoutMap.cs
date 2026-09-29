using System.Collections.Generic;
using System.Text;

namespace CapsIndicator
{
    // Standard US-QWERTY <-> Russian-ЙЦУКЕН physical key-position mapping —
    // the same correspondence Punto Switcher and similar "fix the layout"
    // tools use. EnToRu is the source of truth; RuToEn is its exact inverse.
    internal static class LayoutMap
    {
        public static readonly Dictionary<char, char> EnToRu = BuildEnToRu();
        public static readonly Dictionary<char, char> RuToEn = Invert(EnToRu);

        public static bool IsLatinLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        public static bool IsCyrillicLetter(char c)
        {
            return (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё';
        }

        private static Dictionary<char, char> BuildEnToRu()
        {
            Dictionary<char, char> m = new Dictionary<char, char>();

            // Letters (lowercase); uppercase pairs are derived below.
            string enLower = "qwertyuiop" + "asdfghjkl" + "zxcvbnm";
            string ruLower = "йцукенгшщз" + "фывапролд" + "ячсмить";
            for (int i = 0; i < enLower.Length; i++)
            {
                m[enLower[i]] = ruLower[i];
                m[char.ToUpperInvariant(enLower[i])] = char.ToUpperInvariant(ruLower[i]);
            }

            // Punctuation keys around the letters (unshifted / shifted pairs).
            m['`'] = 'ё'; m['~'] = 'Ё';
            m['['] = 'х'; m['{'] = 'Х';
            m[']'] = 'ъ'; m['}'] = 'Ъ';
            m[';'] = 'ж'; m[':'] = 'Ж';
            m['\''] = 'э'; m['"'] = 'Э';
            m[','] = 'б'; m['<'] = 'Б';
            m['.'] = 'ю'; m['>'] = 'Ю';
            m['/'] = '.'; m['?'] = ',';

            // Shifted number row differs on the Russian layout.
            m['@'] = '"';
            m['#'] = '№';
            m['$'] = ';';
            m['^'] = ':';
            m['&'] = '?';

            return m;
        }

        private static Dictionary<char, char> Invert(Dictionary<char, char> src)
        {
            Dictionary<char, char> inv = new Dictionary<char, char>();
            foreach (KeyValuePair<char, char> kv in src)
            {
                inv[kv.Value] = kv.Key;
            }
            return inv;
        }

        // Every character taken independently, based on its own script —
        // handles a selection mixing both layouts, but a standalone symbol
        // with no adjacent letters (its own "word") can't be resolved and is
        // left untouched.
        public static string ConvertUniversal(string text)
        {
            StringBuilder result = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    result.Append(text[i]);
                    i++;
                    continue;
                }
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                result.Append(ConvertRun(text.Substring(start, i - start)));
            }
            return result.ToString();
        }

        private static string ConvertRun(string run)
        {
            int cyr = 0, lat = 0;
            foreach (char c in run)
            {
                if (IsCyrillicLetter(c)) cyr++;
                else if (IsLatinLetter(c)) lat++;
            }
            if (cyr == 0 && lat == 0) return run;
            Dictionary<char, char> map = cyr >= lat ? RuToEn : EnToRu;
            return ConvertWith(run, map);
        }

        // One direction for the whole selection — correctly converts
        // standalone punctuation too, but can't handle a selection that
        // mixes both layouts.
        public static string ConvertUniform(string text)
        {
            int cyr = 0, lat = 0;
            foreach (char c in text)
            {
                if (IsCyrillicLetter(c)) cyr++;
                else if (IsLatinLetter(c)) lat++;
            }
            if (cyr == 0 && lat == 0) return text;
            Dictionary<char, char> map = cyr >= lat ? RuToEn : EnToRu;
            return ConvertWith(text, map);
        }

        private static string ConvertWith(string text, Dictionary<char, char> map)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                char mapped;
                sb.Append(map.TryGetValue(c, out mapped) ? mapped : c);
            }
            return sb.ToString();
        }

        public static string InvertCase(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsUpper(c)) sb.Append(char.ToLowerInvariant(c));
                else if (char.IsLower(c)) sb.Append(char.ToUpperInvariant(c));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
