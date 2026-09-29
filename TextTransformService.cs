using System;
using System.Threading;
using System.Windows.Forms;

namespace CapsIndicator
{
    // Simulates Ctrl+C, transforms whatever landed on the clipboard, writes
    // it back, then simulates Ctrl+V — the same technique Punto Switcher and
    // similar "fix the selected text" utilities use, since there is no
    // cross-application way to read/replace a text selection directly.
    internal static class TextTransformService
    {
        public static void ApplyCaseToggleToSelection()
        {
            Apply(LayoutMap.InvertCase);
        }

        public static void ApplyLayoutFixToSelection(bool universal)
        {
            Apply(universal ? (Func<string, string>)LayoutMap.ConvertUniversal : LayoutMap.ConvertUniform);
        }

        private static void Apply(Func<string, string> transform)
        {
            object original = null;
            try { original = Clipboard.GetDataObject(); }
            catch { }

            int seqBefore = Native.GetClipboardSequenceNumber();
            Native.SendCtrlCombo(Native.VK_C);

            if (!WaitForClipboardChange(seqBefore, 400))
            {
                RestoreClipboard(original);
                return; // nothing was selected, or the target app didn't respond
            }

            string selected = SafeGetClipboardText();
            if (string.IsNullOrEmpty(selected))
            {
                RestoreClipboard(original);
                return;
            }

            string result = transform(selected);
            if (result == selected)
            {
                RestoreClipboard(original);
                return; // nothing to change
            }

            if (!SafeSetClipboardText(result))
            {
                RestoreClipboard(original);
                return;
            }

            Thread.Sleep(30);
            Native.SendCtrlCombo(Native.VK_V);

            // Give the target app a moment to actually consume the paste
            // before we put the user's original clipboard content back —
            // there's no cross-app "paste completed" signal to wait on.
            Thread.Sleep(180);
            RestoreClipboard(original);
        }

        private static bool WaitForClipboardChange(int seqBefore, int timeoutMs)
        {
            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                Thread.Sleep(15);
                Application.DoEvents();
                elapsed += 15;
                if (Native.GetClipboardSequenceNumber() != seqBefore) return true;
            }
            return false;
        }

        private static string SafeGetClipboardText()
        {
            for (int i = 0; i < 5; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
                catch { Thread.Sleep(20); }
            }
            return "";
        }

        private static bool SafeSetClipboardText(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetText(text); return true; }
                catch { Thread.Sleep(20); }
            }
            return false;
        }

        private static void RestoreClipboard(object original)
        {
            try
            {
                if (original != null) Clipboard.SetDataObject(original, true);
            }
            catch
            {
                // best-effort — the user's clipboard may just stay as our
                // transformed text if this particular restore attempt fails
            }
        }
    }
}
