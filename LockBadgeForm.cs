using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CapsIndicator
{
    // A small always-on-top, click-through companion window that floats just
    // above an IndicatorForm and shows whether the Caps Lock / layout-switch
    // key locks (KeyboardLockService) are currently engaged. Purely
    // informational, per indicator window, and fixed in appearance —
    // deliberately independent of whatever Style the owner indicator uses.
    internal sealed class LockBadgeForm : Form
    {
        private const int BadgeSize = 20;
        private const int Gap = 6;
        private const int MarginAboveOwner = 6;

        private static readonly Color ColorOn = Color.FromArgb(255, 52, 211, 153);
        private static readonly Color ColorOff = Color.FromArgb(255, 118, 128, 145);

        private readonly IndicatorForm _owner;
        private readonly Timer _pollTimer;
        private bool _showCaps;
        private bool _showLayout;
        private bool _lastCapsLocked;
        private bool _lastLayoutLocked;

        public LockBadgeForm(IndicatorForm owner)
        {
            _owner = owner;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            TopMost = owner.TopMost;
            Size = new Size(BadgeSize, BadgeSize);

            _pollTimer = new Timer();
            _pollTimer.Interval = 150;
            _pollTimer.Tick += delegate { PollAndMaybeRender(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        // Called whenever the owner's badge checkboxes or Visible state
        // change. Hides (and stops polling) entirely when nothing is enabled
        // or the owner itself isn't visible.
        public void ApplyVisibility(bool showCaps, bool showLayout, bool ownerVisible)
        {
            _showCaps = showCaps;
            _showLayout = showLayout;
            bool anyVisible = ownerVisible && (showCaps || showLayout);
            if (!anyVisible)
            {
                if (Visible) Hide();
                _pollTimer.Stop();
                return;
            }

            Reposition();
            _lastCapsLocked = KeyboardLockService.CapsLockLocked;
            _lastLayoutLocked = KeyboardLockService.LayoutSwitchLocked;
            RenderAndPresent();
            if (!Visible) Show();
            if (!_pollTimer.Enabled) _pollTimer.Start();
        }

        public void Reposition()
        {
            int count = (_showCaps ? 1 : 0) + (_showLayout ? 1 : 0);
            if (count == 0) return;
            int w = count * BadgeSize + (count - 1) * Gap;
            int h = BadgeSize;
            int x = _owner.Left + (_owner.Width - w) / 2;
            int y = _owner.Top - h - MarginAboveOwner;
            Bounds = new Rectangle(x, y, w, h);
            if (Visible) RenderAndPresent();
        }

        public void ApplyTopmost(bool topmost)
        {
            TopMost = topmost;
        }

        private void PollAndMaybeRender()
        {
            bool caps = KeyboardLockService.CapsLockLocked;
            bool layout = KeyboardLockService.LayoutSwitchLocked;
            if (caps != _lastCapsLocked || layout != _lastLayoutLocked)
            {
                _lastCapsLocked = caps;
                _lastLayoutLocked = layout;
                RenderAndPresent();
            }
        }

        private void RenderAndPresent()
        {
            int w = Width;
            int h = Height;
            if (w <= 0 || h <= 0) return;

            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                int index = 0;
                if (_showCaps)
                {
                    DrawBadge(g, index, _lastCapsLocked, "⇪");
                    index++;
                }
                if (_showLayout)
                {
                    DrawBadge(g, index, _lastLayoutLocked, "⇄");
                    index++;
                }
            }

            IndicatorForm.PremultiplyAlpha(bmp);
            Present(bmp);
            bmp.Dispose();
        }

        private static void DrawBadge(Graphics g, int index, bool locked, string glyph)
        {
            float x = index * (BadgeSize + Gap);
            RectangleF rect = new RectangleF(x + 1f, 1f, BadgeSize - 2f, BadgeSize - 2f);
            Color accent = locked ? ColorOn : ColorOff;

            using (GraphicsPath path = IndicatorForm.RoundedRect(rect, rect.Height * 0.32f))
            {
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(235, Darken(accent, 0.35f))))
                {
                    g.FillPath(bg, path);
                }
                using (Pen border = new Pen(Color.FromArgb(190, accent), 1.2f))
                {
                    g.DrawPath(border, path);
                }
            }

            using (Font font = new Font("Segoe UI Symbol", BadgeSize * 0.5f, FontStyle.Regular))
            using (StringFormat fmt = new StringFormat())
            using (SolidBrush fg = new SolidBrush(Color.White))
            {
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                g.DrawString(glyph, font, fg, rect, fmt);
            }
        }

        private static Color Darken(Color c, float factor)
        {
            return Color.FromArgb(c.A, (int)(c.R * factor), (int)(c.G * factor), (int)(c.B * factor));
        }

        private void Present(Bitmap bmp)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
                oldBitmap = Native.SelectObject(memDc, hBitmap);

                SIZE size = new SIZE(bmp.Width, bmp.Height);
                POINT pointSource = new POINT(0, 0);
                POINT topPos = new POINT(Left, Top);
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;

                Native.UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref pointSource,
                    0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    Native.SelectObject(memDc, oldBitmap);
                    Native.DeleteObject(hBitmap);
                }
                Native.DeleteDC(memDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_pollTimer != null) _pollTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
