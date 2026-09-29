using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapsIndicator
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(255, 24, 25, 29);
        public static readonly Color Panel = Color.FromArgb(255, 32, 34, 39);
        public static readonly Color PanelLight = Color.FromArgb(255, 40, 42, 48);
        public static readonly Color Border = Color.FromArgb(255, 54, 57, 64);
        public static readonly Color TextPrimary = Color.FromArgb(255, 230, 232, 236);
        public static readonly Color TextSecondary = Color.FromArgb(255, 150, 154, 163);
        public static readonly Color Accent = Color.FromArgb(255, 52, 211, 153);
        public static readonly Color AccentDim = Color.FromArgb(255, 40, 130, 100);
        public static readonly Color Danger = Color.FromArgb(255, 244, 113, 113);

        public static readonly Font FontRegular = new Font("Segoe UI", 9.5f);
        public static readonly Font FontBold = new Font("Segoe UI Semibold", 10f);
        public static readonly Font FontTitle = new Font("Segoe UI Semibold", 13f);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.5f);

        public static void ApplyDark(Control root)
        {
            root.BackColor = Background;
            root.ForeColor = TextPrimary;
        }

        // Switches the native title bar/border to the dark variant (same API
        // VSCode, Explorer, and every other modern dark-mode app on Windows
        // 10 20H1+/11 use) instead of the default white system frame.
        public static void ApplyDarkTitleBar(IntPtr handle)
        {
            try
            {
                int use = 1;
                Native.DwmSetWindowAttribute(handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref use, sizeof(int));
                Native.DwmSetWindowAttribute(handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref use, sizeof(int));
            }
            catch
            {
                // older Windows without the attribute — the frame just stays default
            }
        }

        // Recolours a scrollable control's native scrollbar to the dark
        // Explorer style instead of the light system one.
        public static void ApplyDarkScrollbar(Control c)
        {
            try
            {
                Native.SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
            }
            catch
            {
            }
        }

        // Removing the visual-styles theme from a TabControl makes it fall
        // back to classic flat rendering, which is the only way to get its
        // background/border to actually respect BackColor instead of always
        // painting the light system chrome around the owner-drawn headers.
        public static void StripNativeTheme(Control c)
        {
            try
            {
                Native.SetWindowTheme(c.Handle, "", "");
            }
            catch
            {
            }
        }

        public static void StyleCombo(ComboBox cb)
        {
            cb.FlatStyle = FlatStyle.Flat;
            cb.BackColor = PanelLight;
            cb.ForeColor = TextPrimary;
            cb.Font = FontRegular;
            cb.DrawMode = DrawMode.OwnerDrawFixed;

            // Sized from the font's actual measured height (plus padding)
            // instead of a hardcoded 20px — at the font/DPI this app
            // actually renders at, 20px was shorter than a line of Segoe UI
            // 9.5pt, so every dropdown row (and the closed box itself, which
            // draws through the same path) clipped the tops/bottoms of
            // letters instead of framing them.
            using (Graphics measure = cb.CreateGraphics())
            {
                cb.ItemHeight = (int)Math.Ceiling(measure.MeasureString("Ёg", FontRegular).Height) + 6;
            }

            cb.DrawItem += ComboBox_DrawItem;
        }

        private static void ComboBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            ComboBox cb = (ComboBox)sender;
            if (e.Index < 0) return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color bg = selected ? AccentDim : PanelLight;
            using (SolidBrush b = new SolidBrush(bg))
            {
                e.Graphics.FillRectangle(b, e.Bounds);
            }

            // Centred against the row's actual height rather than a fixed
            // +2px offset, so the text lands correctly whether this is a
            // dropdown-list row or the combo's own (possibly differently
            // sized) closed box — that mismatch was the visible shift
            // between the combo's text baseline and its neighbouring
            // labels/spinners in each window row.
            string text = cb.Items[e.Index].ToString();
            SizeF textSize = e.Graphics.MeasureString(text, FontRegular);
            float ty = e.Bounds.Y + (e.Bounds.Height - textSize.Height) / 2f;
            using (SolidBrush fg = new SolidBrush(TextPrimary))
            {
                e.Graphics.DrawString(text, FontRegular, fg, e.Bounds.X + 4, ty);
            }
        }
    }

    // A checkbox drawn entirely by us — no native white glyph — with a
    // rounded dark box and a smooth check-mark fade instead of an instant
    // system tick.
    internal sealed class DarkCheckBox : CheckBox
    {
        private float _checkT;
        private readonly Timer _timer;

        public DarkCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ForeColor = Theme.TextPrimary;
            Font = Theme.FontRegular;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            _checkT = Checked ? 1f : 0f;

            _timer = new Timer();
            _timer.Interval = 15;
            _timer.Tick += Timer_Tick;
            CheckedChanged += delegate { _timer.Start(); };
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            float target = Checked ? 1f : 0f;
            float step = 0.22f;
            if (_checkT < target) _checkT = Math.Min(target, _checkT + step);
            else if (_checkT > target) _checkT = Math.Max(target, _checkT - step);
            Invalidate();
            if (_checkT == target) _timer.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);

            int boxSize = 16;
            int boxY = (Height - boxSize) / 2;
            Rectangle box = new Rectangle(0, boxY, boxSize, boxSize);

            Color boxColor = Color.FromArgb(
                Lerp(Theme.PanelLight.R, Theme.Accent.R, _checkT),
                Lerp(Theme.PanelLight.G, Theme.Accent.G, _checkT),
                Lerp(Theme.PanelLight.B, Theme.Accent.B, _checkT));

            using (var path = RoundedRect(box, 4))
            using (SolidBrush b = new SolidBrush(boxColor))
            {
                g.FillPath(b, path);
            }
            using (var path = RoundedRect(box, 4))
            using (Pen p = new Pen(Theme.Border, 1f))
            {
                g.DrawPath(p, path);
            }

            if (_checkT > 0.01f)
            {
                using (Pen check = new Pen(Color.FromArgb((int)(255 * _checkT), Color.Black), 2f))
                {
                    check.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    check.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    PointF p1 = new PointF(box.X + 3.5f, box.Y + 8.5f);
                    PointF p2 = new PointF(box.X + 6.5f, box.Y + 11.5f);
                    PointF p3 = new PointF(box.X + 12.5f, box.Y + 4.5f);
                    g.DrawLine(check, p1, p2);
                    g.DrawLine(check, p2, p3);
                }
            }

            using (SolidBrush textBrush = new SolidBrush(Enabled ? ForeColor : Theme.TextSecondary))
            {
                StringFormat fmt = new StringFormat();
                fmt.LineAlignment = StringAlignment.Center;
                g.DrawString(Text, Font, textBrush,
                    new RectangleF(boxSize + 8, 0, Width - boxSize - 8, Height), fmt);
                fmt.Dispose();
            }
        }

        private static int Lerp(int a, int b, float t)
        {
            return a + (int)((b - a) * t);
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int d = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // A flat button with a smoothly animated hover-highlight instead of the
    // stock WinForms flat-button flash.
    internal sealed class FlatButton : Button
    {
        private float _hoverT;
        private readonly Timer _timer;
        private Color _baseColor;
        private Color _hoverColor;

        public FlatButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = Theme.Border;
            BackColor = Theme.Panel;
            ForeColor = Theme.TextPrimary;
            Font = Theme.FontRegular;
            Cursor = Cursors.Hand;
            _baseColor = Theme.Panel;
            _hoverColor = Theme.PanelLight;

            _timer = new Timer();
            _timer.Interval = 15;
            _timer.Tick += Timer_Tick;

            MouseEnter += delegate { _timer.Start(); };
            MouseLeave += delegate { _timer.Start(); };
        }

        public void SetPalette(Color baseColor, Color hoverColor)
        {
            _baseColor = baseColor;
            _hoverColor = hoverColor;
            BackColor = baseColor;
        }

        private void Timer_Tick(object sender, System.EventArgs e)
        {
            bool hot = ClientRectangle.Contains(PointToClient(Cursor.Position));
            float target = hot ? 1f : 0f;
            float step = 0.18f;
            if (_hoverT < target) _hoverT = System.Math.Min(target, _hoverT + step);
            else if (_hoverT > target) _hoverT = System.Math.Max(target, _hoverT - step);

            BackColor = Lerp(_baseColor, _hoverColor, _hoverT);

            if (_hoverT == target) _timer.Stop();
        }

        private static Color Lerp(Color a, Color b, float t)
        {
            int r = a.R + (int)((b.R - a.R) * t);
            int g = a.G + (int)((b.G - a.G) * t);
            int bl = a.B + (int)((b.B - a.B) * t);
            return Color.FromArgb(r, g, bl);
        }
    }

    // A numeric stepper drawn entirely by us — NumericUpDown's spin buttons
    // are a separate native child control that ignores BackColor/theme calls
    // and always renders as a light system control, which is exactly the
    // white artifact this replaces.
    internal sealed class DarkSpinner : Control
    {
        private int _value;
        private int _min;
        private int _max;
        private TextBox _editBox;

        public int Step = 1;
        public event EventHandler ValueChanged;

        public int Minimum { get { return _min; } set { _min = value; } }
        public int Maximum { get { return _max; } set { _max = value; } }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(_min, Math.Min(_max, value));
                if (clamped == _value) return;
                _value = clamped;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public DarkSpinner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable, true);
            Font = Theme.FontRegular;
            ForeColor = Theme.TextPrimary;
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(Theme.PanelLight))
            {
                g.FillRectangle(bg, ClientRectangle);
            }
            using (Pen border = new Pen(Focused ? Theme.Accent : Theme.Border, 1f))
            {
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }

            const int arrowW = 16;
            if (_editBox == null)
            {
                Rectangle textRect = new Rectangle(5, 0, Width - arrowW - 8, Height);
                StringFormat fmt = new StringFormat();
                fmt.LineAlignment = StringAlignment.Center;
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                using (SolidBrush fg = new SolidBrush(ForeColor))
                {
                    g.DrawString(_value.ToString(), Font, fg, textRect, fmt);
                }
                fmt.Dispose();
            }

            Rectangle upRect = new Rectangle(Width - arrowW, 0, arrowW, Height / 2);
            Rectangle downRect = new Rectangle(Width - arrowW, Height / 2, arrowW, Height - Height / 2);
            DrawArrow(g, upRect, true);
            DrawArrow(g, downRect, false);
            using (Pen sep = new Pen(Theme.Border, 1f))
            {
                g.DrawLine(sep, Width - arrowW, 1, Width - arrowW, Height - 2);
            }
        }

        private static void DrawArrow(Graphics g, Rectangle r, bool up)
        {
            float cx = r.X + r.Width / 2f;
            float cy = r.Y + r.Height / 2f;
            float s = 3f;
            PointF p1, p2, p3;
            if (up)
            {
                p1 = new PointF(cx - s, cy + s * 0.5f);
                p2 = new PointF(cx + s, cy + s * 0.5f);
                p3 = new PointF(cx, cy - s * 0.7f);
            }
            else
            {
                p1 = new PointF(cx - s, cy - s * 0.5f);
                p2 = new PointF(cx + s, cy - s * 0.5f);
                p3 = new PointF(cx, cy + s * 0.7f);
            }
            using (SolidBrush b = new SolidBrush(Theme.TextSecondary))
            {
                g.FillPolygon(b, new PointF[] { p1, p2, p3 });
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            const int arrowW = 16;
            if (e.X >= Width - arrowW)
            {
                Value = e.Y < Height / 2 ? Value + Step : Value - Step;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Value = e.Delta > 0 ? Value + Step : Value - Step;
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            BeginEdit();
        }

        private void BeginEdit()
        {
            if (_editBox != null) return;
            _editBox = new TextBox();
            _editBox.Text = _value.ToString();
            _editBox.BackColor = Theme.PanelLight;
            _editBox.ForeColor = Theme.TextPrimary;
            _editBox.BorderStyle = BorderStyle.None;
            _editBox.Left = 4;
            _editBox.Width = Width - 22;
            _editBox.KeyDown += EditBox_KeyDown;
            _editBox.LostFocus += delegate { CommitEdit(); };
            Controls.Add(_editBox);
            _editBox.Top = (Height - _editBox.Height) / 2;
            _editBox.BringToFront();
            _editBox.Focus();
            _editBox.SelectAll();
            Invalidate();
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { CommitEdit(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { CancelEdit(); e.Handled = true; e.SuppressKeyPress = true; }
        }

        private void CommitEdit()
        {
            if (_editBox == null) return;
            int v;
            if (int.TryParse(_editBox.Text, out v)) Value = v;
            Controls.Remove(_editBox);
            _editBox.Dispose();
            _editBox = null;
            Invalidate();
        }

        private void CancelEdit()
        {
            if (_editBox == null) return;
            Controls.Remove(_editBox);
            _editBox.Dispose();
            _editBox = null;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up) Value = Value + Step;
            else if (e.KeyCode == Keys.Down) Value = Value - Step;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    // A custom tab-header button (no native TabControl chrome at all — that
    // was the source of the persistent white border/selection look).
    internal sealed class TabHeaderButton : Control
    {
        private bool _selected;
        public bool Selected
        {
            get { return _selected; }
            set { if (_selected == value) return; _selected = value; Invalidate(); }
        }

        public TabHeaderButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.FontBold;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(_selected ? Theme.Panel : Theme.Background))
            {
                g.FillRectangle(bg, ClientRectangle);
            }
            if (_selected)
            {
                using (Pen accentPen = new Pen(Theme.Accent, 2f))
                {
                    g.DrawLine(accentPen, 6, Height - 2, Width - 6, Height - 2);
                }
            }
            StringFormat fmt = new StringFormat();
            fmt.Alignment = StringAlignment.Center;
            fmt.LineAlignment = StringAlignment.Center;
            using (SolidBrush fg = new SolidBrush(_selected ? Theme.TextPrimary : Theme.TextSecondary))
            {
                g.DrawString(Text, Font, fg, ClientRectangle, fmt);
            }
            fmt.Dispose();
        }
    }
}
