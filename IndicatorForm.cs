using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapsIndicator
{
    internal enum Style
    {
        Full = 0,
        Compact = 1,
        Pill = 2,
        Ring = 3,
        Dot = 4,
        Outline = 5,
        Terminal = 6,
        Radar = 7,
        Liquid = 8,
        Orbit = 9,
        Matrix = 10,
        Equalizer = 11,
        Neon = 12,
        Gauge = 13,
        Led = 14,
        Text = 15,
        Badge3D = 16,
        Cube3D = 17,
        Sphere3D = 18,
        Chip3D = 19,
        Hud = 20,
        Ripple = 21,
        TiltLiquid = 22,
        Typing = 23,
        Heartbeat = 24,
        Confetti = 25,
        Compass = 26,
        Oscilloscope = 27,
        Crystal = 28,
        Starfield = 29
    }

    internal sealed class IndicatorForm : Form
    {
        private const int WM_NCHITTEST = Native.WM_NCHITTEST;
        private const int WM_SYSCOMMAND = Native.WM_SYSCOMMAND;
        private const int SC_MAXIMIZE = Native.SC_MAXIMIZE;
        private const int SC_MINIMIZE = Native.SC_MINIMIZE;
        private const int SC_RESTORE = Native.SC_RESTORE;
        private const int HTCLIENT = Native.HTCLIENT;
        private const int HTCAPTION = Native.HTCAPTION;
        private const int HTLEFT = Native.HTLEFT;
        private const int HTRIGHT = Native.HTRIGHT;
        private const int HTTOP = Native.HTTOP;
        private const int HTTOPLEFT = Native.HTTOPLEFT;
        private const int HTTOPRIGHT = Native.HTTOPRIGHT;
        private const int HTBOTTOM = Native.HTBOTTOM;
        private const int HTBOTTOMLEFT = Native.HTBOTTOMLEFT;
        private const int HTBOTTOMRIGHT = Native.HTBOTTOMRIGHT;
        private const int WM_LBUTTONDOWN = Native.WM_LBUTTONDOWN;
        private const int WM_RBUTTONDOWN = Native.WM_RBUTTONDOWN;
        private const int WM_MBUTTONDOWN = Native.WM_MBUTTONDOWN;
        private const int WM_NCLBUTTONDOWN = Native.WM_NCLBUTTONDOWN;
        private const int WM_NCRBUTTONDOWN = Native.WM_NCRBUTTONDOWN;
        private const int WM_NCMBUTTONDOWN = Native.WM_NCMBUTTONDOWN;

        // Grab zones are generous and shrink gracefully on tiny/huge windows so the
        // edges/corners stay easy to grab regardless of window size.
        private const int ResizeEdgeMin = 8;
        private const int ResizeEdgeMax = 16;
        private const int ResizeCornerMin = 14;
        private const int ResizeCornerMax = 26;

        private const int PulseDurationMs = 550;
        private const int StyleSwitchDurationMs = 260;
        private const int FrameFadeInMs = 70;
        private const int FrameFadeOutMs = 260;

        private static readonly Color ColorOn = Color.FromArgb(255, 52, 211, 153);
        private static readonly Color ColorOff = Color.FromArgb(255, 118, 128, 145);

        // Shared across every indicator window: one physical keystroke should
        // register once, not once per open window.
        private static readonly bool[] s_prevKeyDown = new bool[256];
        private static float s_typingActivity;

        public IndicatorSource Source { get; private set; }
        public Style CurrentStyle { get { return _style; } }
        public event EventHandler Changed;

        private readonly Timer _pollTimer;
        private readonly Timer _animTimer;

        private bool _active;
        private string _dynamicValue;
        private IndicatorLabels _labels;
        private Style _style = Style.Full;
        private Style _prevStyle = Style.Full;

        private bool _pulseActive;
        private int _pulseStart;

        private bool _styleAnimActive;
        private int _styleAnimStart;

        private float _frameOpacity;
        private float _frameTarget;
        private bool _showDragFrame = true;
        private bool _isSelected;

        private bool _showCapsLockLockBadge;
        private bool _showLayoutSwitchLockBadge;
        private LockBadgeForm _lockBadgeForm;

        private int _soundKind;
        private string _soundBuiltinId = "";
        private string _soundFilePath = "";

        private readonly List<RippleData> _ripples = new List<RippleData>();
        private Point _lastRipplePos = new Point(-9999, -9999);

        private float _tiltAngle;
        private float _tiltVelocity;

        // Continuously-integrated angle/offset state for Radar/Orbit/Matrix.
        // These must NOT be derived as phase*speed — multiplying an
        // ever-increasing absolute clock by a speed that can change the
        // instant the toggle flips produces a discontinuous jump. Integrating
        // a smoothed speed frame-by-frame keeps position and speed continuous.
        private float _radarAngle;
        private float _radarSpeed;
        private float _radarActivity;

        private float _orbitAngle;
        private float _orbitSpeed;

        private float _matrixOffset;
        private float _matrixSpeed;

        private struct RippleData
        {
            public float X;
            public float Y;
            public int StartTick;
        }

        private readonly int _clockStart = Environment.TickCount;

        private float Phase()
        {
            return (Environment.TickCount - _clockStart) / 1000f;
        }

        private static bool NeedsContinuousAnim(Style s)
        {
            return s == Style.Terminal || s == Style.Radar || s == Style.Liquid || s == Style.Orbit
                || s == Style.Matrix || s == Style.Equalizer || s == Style.Neon
                || s == Style.Cube3D || s == Style.Sphere3D || s == Style.Hud
                || s == Style.Heartbeat || s == Style.Oscilloscope || s == Style.Starfield
                || s == Style.Typing || s == Style.Ripple || s == Style.TiltLiquid
                || s == Style.Compass || s == Style.Confetti || s == Style.Crystal
                || s == Style.Badge3D || s == Style.Chip3D;
        }

        private static bool IsLegacyStyle(Style s)
        {
            return s == Style.Full || s == Style.Compact || s == Style.Pill
                || s == Style.Ring || s == Style.Dot || s == Style.Outline;
        }

        public IndicatorForm(WindowConfig cfg, bool topmost, bool showDragFrame, int targetFps)
        {
            Source = cfg.Source;
            _style = cfg.Style;
            _prevStyle = cfg.Style;
            _showDragFrame = showDragFrame;
            _showCapsLockLockBadge = cfg.ShowCapsLockLockBadge;
            _showLayoutSwitchLockBadge = cfg.ShowLayoutSwitchLockBadge;
            _soundKind = cfg.SoundKind;
            _soundBuiltinId = cfg.SoundBuiltinId ?? "";
            _soundFilePath = cfg.SoundFilePath ?? "";

            Text = "CapsIndicator — " + SourceCatalog.Name(Source);
            FormBorderStyle = FormBorderStyle.None;
            TopMost = topmost;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(48, 30);
            BackColor = Color.Black;

            Size = new Size(Math.Max(cfg.W, MinimumSize.Width), Math.Max(cfg.H, MinimumSize.Height));
            if (cfg.X >= 0 && cfg.Y >= 0)
            {
                Location = new Point(cfg.X, cfg.Y);
            }
            else
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - 20, wa.Top + 20);
            }

            SizeChanged += delegate { RenderAndPresent(); RaiseChanged(); RepositionLockBadge(); };
            LocationChanged += delegate { RaiseChanged(); RepositionLockBadge(); };
            VisibleChanged += delegate { UpdateLockBadge(); };
            MouseDown += IndicatorForm_MouseDown;
            MouseMove += IndicatorForm_MouseMove;
            MouseUp += delegate { RenderAndPresent(); };
            Deactivate += delegate
            {
                // A persistently-selected indicator (via the select-next/prev
                // hotkeys) keeps its outline no matter what else gets focus —
                // in particular, clicking around inside the Settings window
                // must not clear it.
                if (!_isSelected) { _frameTarget = 0f; EnsureAnimTimer(); }
            };

            _pollTimer = new Timer();
            _pollTimer.Interval = 120;
            _pollTimer.Tick += PollTimer_Tick;

            _animTimer = new Timer();
            _animTimer.Interval = FpsToInterval(targetFps);
            _animTimer.Tick += AnimTimer_Tick;

            UpdateLockBadge();
        }

        private static int FpsToInterval(int fps)
        {
            if (fps < 1) fps = 1;
            if (fps > 240) fps = 240;
            int ms = 1000 / fps;
            return Math.Max(1, ms);
        }

        public void ApplyFrameRate(int fps)
        {
            _animTimer.Interval = FpsToInterval(fps);
        }

        public WindowConfig ToConfig()
        {
            WindowConfig w = new WindowConfig();
            w.Source = Source;
            w.Style = _style;
            w.X = Location.X;
            w.Y = Location.Y;
            w.W = Width;
            w.H = Height;
            w.ShowCapsLockLockBadge = _showCapsLockLockBadge;
            w.ShowLayoutSwitchLockBadge = _showLayoutSwitchLockBadge;
            w.SoundKind = _soundKind;
            w.SoundBuiltinId = _soundBuiltinId;
            w.SoundFilePath = _soundFilePath;
            return w;
        }

        // Per-window toggle sound — played once each time this indicator's
        // active state flips (see PollTimer_Tick). Independent of Style, and
        // of every other window's own sound choice.
        public int SoundKind
        {
            get { return _soundKind; }
            set
            {
                if (_soundKind == value) return;
                _soundKind = value;
                RaiseChanged();
            }
        }

        public string SoundBuiltinId
        {
            get { return _soundBuiltinId; }
            set
            {
                value = value ?? "";
                if (_soundBuiltinId == value) return;
                _soundBuiltinId = value;
                RaiseChanged();
            }
        }

        public string SoundFilePath
        {
            get { return _soundFilePath; }
            set
            {
                value = value ?? "";
                if (_soundFilePath == value) return;
                _soundFilePath = value;
                RaiseChanged();
            }
        }

        // Whether the small "Caps Lock is locked" / "layout switch is locked"
        // badges float above this particular indicator window. Per-window and
        // independent of Style — the badges always look the same regardless
        // of which style the main indicator is drawn in.
        public bool ShowCapsLockLockBadge
        {
            get { return _showCapsLockLockBadge; }
            set
            {
                if (_showCapsLockLockBadge == value) return;
                _showCapsLockLockBadge = value;
                UpdateLockBadge();
                RaiseChanged();
            }
        }

        public bool ShowLayoutSwitchLockBadge
        {
            get { return _showLayoutSwitchLockBadge; }
            set
            {
                if (_showLayoutSwitchLockBadge == value) return;
                _showLayoutSwitchLockBadge = value;
                UpdateLockBadge();
                RaiseChanged();
            }
        }

        private void UpdateLockBadge()
        {
            bool any = _showCapsLockLockBadge || _showLayoutSwitchLockBadge;
            if (!any)
            {
                if (_lockBadgeForm != null) _lockBadgeForm.ApplyVisibility(false, false, Visible);
                return;
            }
            if (_lockBadgeForm == null || _lockBadgeForm.IsDisposed)
            {
                _lockBadgeForm = new LockBadgeForm(this);
            }
            _lockBadgeForm.ApplyVisibility(_showCapsLockLockBadge, _showLayoutSwitchLockBadge, Visible);
        }

        private void RepositionLockBadge()
        {
            if (_lockBadgeForm != null) _lockBadgeForm.Reposition();
        }

        public void ApplyStyle(Style s)
        {
            if (_style == s) return;
            _prevStyle = _style;
            _style = s;
            _styleAnimActive = true;
            _styleAnimStart = Environment.TickCount;
            EnsureAnimTimer();
            RaiseChanged();
        }

        public void ApplySource(IndicatorSource s)
        {
            if (Source == s) return;
            Source = s;
            _active = SourceCatalog.GetActive(Source, out _dynamicValue);
            _labels = SourceCatalog.GetLabels(Source, _active, _dynamicValue);
            Text = "CapsIndicator — " + SourceCatalog.Name(Source);
            RenderAndPresent();
            RaiseChanged();
        }

        public void ApplyTopmost(bool topmost)
        {
            TopMost = topmost;
            if (_lockBadgeForm != null) _lockBadgeForm.ApplyTopmost(topmost);
        }

        public void ApplyShowDragFrame(bool show)
        {
            _showDragFrame = show;
        }

        public void SetSelected(bool selected)
        {
            if (_isSelected == selected) return;
            _isSelected = selected;
            _frameTarget = selected ? 1f : 0f;
            EnsureAnimTimer();
        }

        private void RaiseChanged()
        {
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _active = SourceCatalog.GetActive(Source, out _dynamicValue);
            _labels = SourceCatalog.GetLabels(Source, _active, _dynamicValue);
            _pollTimer.Start();
            if (NeedsContinuousAnim(_style)) EnsureAnimTimer();
            RenderAndPresent();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    m.Result = (IntPtr)HitTest(PointToClient(Cursor.Position));
                }
                return;
            }
            else if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_RBUTTONDOWN || m.Msg == WM_MBUTTONDOWN
                || m.Msg == WM_NCLBUTTONDOWN || m.Msg == WM_NCRBUTTONDOWN || m.Msg == WM_NCMBUTTONDOWN)
            {
                // Any mouse-down anywhere on the window — including clicks that
                // start a drag/resize (those arrive as WM_NCLBUTTONDOWN, which a
                // plain Form.MouseDown handler never sees) and clicks in fully
                // transparent-looking areas — should reveal the bounds frame.
                ShowFrame();
            }
            else if (m.Msg == WM_SYSCOMMAND)
            {
                // Block Aero Snap maximize/minimize (Win+Up / Win+Down) — this
                // window has no real caption/sizeframe chrome, so letting the
                // shell "maximize" or "minimize" it just breaks the layered
                // rendering instead of doing anything useful.
                int cmd = m.WParam.ToInt32() & 0xFFF0;
                if (cmd == SC_MAXIMIZE || cmd == SC_MINIMIZE || cmd == SC_RESTORE)
                {
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private int HitTest(Point p)
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            // Edge/corner grab zones scale with window size (clamped) so they
            // stay easy to hit on both tiny and huge windows, without eating
            // more than roughly a third of a very small window's content.
            int edge = Clamp(ResizeEdgeMin, ResizeEdgeMax, Math.Min(w, h) / 4);
            int corner = Clamp(ResizeCornerMin, ResizeCornerMax, Math.Min(w, h) / 3);

            if (p.X <= corner && p.Y <= corner) return HTTOPLEFT;
            if (p.X >= w - corner && p.Y <= corner) return HTTOPRIGHT;
            if (p.X <= corner && p.Y >= h - corner) return HTBOTTOMLEFT;
            if (p.X >= w - corner && p.Y >= h - corner) return HTBOTTOMRIGHT;
            if (p.X <= edge) return HTLEFT;
            if (p.X >= w - edge) return HTRIGHT;
            if (p.Y <= edge) return HTTOP;
            if (p.Y >= h - edge) return HTBOTTOM;
            return HTCAPTION;
        }

        private static int Clamp(int min, int max, int v)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private void PollTimer_Tick(object sender, EventArgs e)
        {
            string dyn;
            bool state = SourceCatalog.GetActive(Source, out dyn);
            bool valueChanged = dyn != _dynamicValue;
            if (state != _active || valueChanged)
            {
                bool wasActive = _active;
                _active = state;
                _dynamicValue = dyn;
                _labels = SourceCatalog.GetLabels(Source, _active, _dynamicValue);
                if (state != wasActive)
                {
                    _pulseActive = true;
                    _pulseStart = Environment.TickCount;
                    EnsureAnimTimer();
                    SoundService.Play(_soundKind, _soundBuiltinId, _soundFilePath);
                }
                else
                {
                    RenderAndPresent();
                }
            }

            // Cheap "any key just pressed" edge detection to drive the Typing
            // style's activity meter — a plain state poll, not a keylogger:
            // we never learn which key was pressed, only that something was.
            // Shared across all indicator windows via static fields.
            int pressedNow = 0;
            for (int vk = 8; vk <= 254; vk++)
            {
                bool down = (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
                if (down && !s_prevKeyDown[vk]) pressedNow++;
                s_prevKeyDown[vk] = down;
            }
            if (pressedNow > 0)
            {
                s_typingActivity = Math.Min(1f, s_typingActivity + pressedNow * 0.35f);
                if (_style == Style.Typing) EnsureAnimTimer();
            }
            s_typingActivity *= 0.90f;
        }

        private int _lastAnimTick = Environment.TickCount;

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            int now = Environment.TickCount;
            float dt = (now - _lastAnimTick) / 1000f;
            _lastAnimTick = now;
            if (dt <= 0f) dt = 0.001f;
            if (dt > 0.1f) dt = 0.1f; // clamp so a resumed/stalled timer doesn't jump physics

            if (_style == Style.TiltLiquid) UpdateTiltPhysics(dt);
            if (_style == Style.Radar) UpdateRadarPhysics(dt);
            if (_style == Style.Orbit) UpdateOrbitPhysics(dt);
            if (_style == Style.Matrix) UpdateMatrixPhysics(dt);

            // Snappy appear, graceful fade — matches how a UI highlight should
            // feel: instant feedback on grab, gentle release afterwards.
            if (_frameOpacity < _frameTarget)
            {
                _frameOpacity = Math.Min(_frameTarget, _frameOpacity + dt * 1000f / FrameFadeInMs);
            }
            else if (_frameOpacity > _frameTarget)
            {
                _frameOpacity = Math.Max(_frameTarget, _frameOpacity - dt * 1000f / FrameFadeOutMs);
            }

            RenderAndPresent();

            bool continuous = NeedsContinuousAnim(_style)
                || (_styleAnimActive && NeedsContinuousAnim(_prevStyle));
            bool frameAnimating = _frameOpacity != _frameTarget;
            if (!_pulseActive && !_styleAnimActive && !continuous && !frameAnimating)
            {
                _animTimer.Stop();
            }
        }

        private void EnsureAnimTimer()
        {
            if (!_animTimer.Enabled)
            {
                _lastAnimTick = Environment.TickCount;
                _animTimer.Start();
            }
        }

        private void UpdateTiltPhysics(float dt)
        {
            Point mp = PointToClient(Cursor.Position);
            float nx = 0f;
            if (ClientSize.Width > 0)
            {
                nx = (mp.X - ClientSize.Width / 2f) / (ClientSize.Width / 2f);
            }
            if (nx < -1f) nx = -1f;
            if (nx > 1f) nx = 1f;
            bool inside = ClientRectangle.Contains(mp);
            float target = inside ? nx * 22f : 0f;

            float springK = 90f;
            float damping = 11f;
            float accel = (target - _tiltAngle) * springK - _tiltVelocity * damping;
            _tiltVelocity += accel * dt;
            _tiltAngle += _tiltVelocity * dt;
        }

        private void UpdateRadarPhysics(float dt)
        {
            float targetSpeed = _active ? 130f : 22f;
            float smooth = 1f - (float)Math.Exp(-dt * 4.0);
            _radarSpeed += (targetSpeed - _radarSpeed) * smooth;
            _radarActivity += ((_active ? 1f : 0f) - _radarActivity) * smooth;
            _radarAngle += _radarSpeed * dt;
            if (_radarAngle > 360f) _radarAngle -= 360f * (float)Math.Floor(_radarAngle / 360f);
        }

        private void UpdateOrbitPhysics(float dt)
        {
            float targetSpeed = _active ? 150f : 26f;
            float smooth = 1f - (float)Math.Exp(-dt * 4.0);
            _orbitSpeed += (targetSpeed - _orbitSpeed) * smooth;
            _orbitAngle += _orbitSpeed * dt;
            if (_orbitAngle > 360f) _orbitAngle -= 360f * (float)Math.Floor(_orbitAngle / 360f);
        }

        private void UpdateMatrixPhysics(float dt)
        {
            float targetSpeed = _active ? 60f : 8f;
            float smooth = 1f - (float)Math.Exp(-dt * 3.0);
            _matrixSpeed += (targetSpeed - _matrixSpeed) * smooth;
            _matrixOffset += _matrixSpeed * dt;
        }

        public event EventHandler ClickedForSelection;

        private void ShowFrame()
        {
            // A click always marks this indicator as "selected" — this is
            // what the select-relative/cycle-style-selected hotkeys act on —
            // independent of whether the transient drag-frame visual itself
            // is enabled.
            if (ClickedForSelection != null) ClickedForSelection(this, EventArgs.Empty);

            if (!_showDragFrame) return;
            _frameTarget = 1f;
            EnsureAnimTimer();
        }

        private void IndicatorForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            MaybeSpawnRipple(e.Location, true);
        }

        private void IndicatorForm_MouseMove(object sender, MouseEventArgs e)
        {
            MaybeSpawnRipple(e.Location, false);
        }

        private void MaybeSpawnRipple(Point p, bool force)
        {
            if (_style != Style.Ripple) return;

            int dx = p.X - _lastRipplePos.X;
            int dy = p.Y - _lastRipplePos.Y;
            if (!force && (dx * dx + dy * dy) < 400) return;

            _lastRipplePos = p;
            if (_ripples.Count > 8) _ripples.RemoveAt(0);

            RippleData r = new RippleData();
            r.X = p.X;
            r.Y = p.Y;
            r.StartTick = Environment.TickCount;
            _ripples.Add(r);
            EnsureAnimTimer();
        }

        #region Rendering

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
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                DrawScene(g, new Rectangle(0, 0, w, h));
            }

            PresentBitmap(bmp);
            bmp.Dispose();
        }

        private void DrawScene(Graphics g, Rectangle full)
        {
            float pulseScale = ComputePulseScale();
            float pulseGlow = ComputePulseGlow();
            float rawT = PulseT();
            float phase = Phase();

            float radarBlipAngle = 0f;
            if (_style == Style.Radar || (_styleAnimActive && _prevStyle == Style.Radar))
            {
                Point centerScreen = PointToScreen(new Point(full.X + full.Width / 2, full.Y + full.Height / 2));
                Point mouseScreen = Cursor.Position;
                double dx = mouseScreen.X - centerScreen.X;
                double dy = mouseScreen.Y - centerScreen.Y;
                radarBlipAngle = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            }

            // Normalized cursor position within the window, used by the
            // Chip3D "spotlight" style to track the mouse across its surface.
            float mouseNX = 0f;
            float mouseNY = 0f;
            {
                Point mp = PointToClient(Cursor.Position);
                if (full.Width > 0) mouseNX = (mp.X - full.X - full.Width / 2f) / (full.Width / 2f);
                if (full.Height > 0) mouseNY = (mp.Y - full.Y - full.Height / 2f) / (full.Height / 2f);
                if (mouseNX < -1f) mouseNX = -1f;
                if (mouseNX > 1f) mouseNX = 1f;
                if (mouseNY < -1f) mouseNY = -1f;
                if (mouseNY > 1f) mouseNY = 1f;
            }

            if (_styleAnimActive)
            {
                int elapsed = Environment.TickCount - _styleAnimStart;
                float t = elapsed / (float)StyleSwitchDurationMs;
                if (t >= 1f)
                {
                    t = 1f;
                    _styleAnimActive = false;
                }
                float te = EaseOutCubic(t);
                DrawStyle(g, _prevStyle, full, _active, _labels, 1f, 0f, rawT, phase, 1f - te,
                    _ripples, _tiltAngle, _pulseStart, _radarAngle, _radarActivity, radarBlipAngle,
                    _orbitAngle, _matrixOffset, mouseNX, mouseNY);
                DrawStyle(g, _style, full, _active, _labels, pulseScale, pulseGlow, rawT, phase, te,
                    _ripples, _tiltAngle, _pulseStart, _radarAngle, _radarActivity, radarBlipAngle,
                    _orbitAngle, _matrixOffset, mouseNX, mouseNY);
            }
            else
            {
                DrawStyle(g, _style, full, _active, _labels, pulseScale, pulseGlow, rawT, phase, 1f,
                    _ripples, _tiltAngle, _pulseStart, _radarAngle, _radarActivity, radarBlipAngle,
                    _orbitAngle, _matrixOffset, mouseNX, mouseNY);
            }

            if (_frameOpacity > 0.01f)
            {
                DrawBoundsFrame(g, full, _frameOpacity);
            }
        }

        private static void DrawBoundsFrame(Graphics g, Rectangle full, float opacity)
        {
            RectangleF r = new RectangleF(full.X + 1f, full.Y + 1f, full.Width - 2f, full.Height - 2f);
            Color c = Color.FromArgb(255, 255, 255, 255);

            using (Pen outline = new Pen(WithAlpha(c, opacity * 0.55f), 1f))
            {
                g.DrawRectangle(outline, r.X, r.Y, r.Width, r.Height);
            }

            float cl = Math.Min(16f, Math.Min(r.Width, r.Height) * 0.4f);
            using (Pen corner = new Pen(WithAlpha(c, opacity * 0.95f), 2f))
            {
                corner.StartCap = LineCap.Round;
                corner.EndCap = LineCap.Round;

                g.DrawLine(corner, r.X, r.Y, r.X + cl, r.Y);
                g.DrawLine(corner, r.X, r.Y, r.X, r.Y + cl);

                g.DrawLine(corner, r.Right, r.Y, r.Right - cl, r.Y);
                g.DrawLine(corner, r.Right, r.Y, r.Right, r.Y + cl);

                g.DrawLine(corner, r.X, r.Bottom, r.X + cl, r.Bottom);
                g.DrawLine(corner, r.X, r.Bottom, r.X, r.Bottom - cl);

                g.DrawLine(corner, r.Right, r.Bottom, r.Right - cl, r.Bottom);
                g.DrawLine(corner, r.Right, r.Bottom, r.Right, r.Bottom - cl);
            }
        }

        private float ComputePulseScale()
        {
            float t = PulseT();
            if (t >= 1f) return 1f;
            double decay = Math.Exp(-t * 6.0);
            double wave = Math.Sin(Math.PI * 3.2 * t);
            // Kept small on purpose: the legacy styles only leave a few px of
            // margin around their shape, so a bigger bounce pushes the shape
            // (and its text) past the window edge and gets hard-clipped there.
            return (float)(1.0 + 0.045 * decay * wave);
        }

        private float ComputePulseGlow()
        {
            float t = PulseT();
            if (t >= 1f) return 0f;
            return (float)Math.Exp(-t * 5.0);
        }

        private float PulseT()
        {
            if (!_pulseActive) return 1f;
            int elapsed = Environment.TickCount - _pulseStart;
            if (elapsed >= PulseDurationMs)
            {
                _pulseActive = false;
                return 1f;
            }
            return elapsed / (float)PulseDurationMs;
        }

        private static float EaseOutCubic(float t)
        {
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }


        private void PresentBitmap(Bitmap bmp)
        {
            PremultiplyAlpha(bmp);

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

        internal static void PremultiplyAlpha(Bitmap bmp)
        {
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(data.Stride) * bmp.Height;
                byte[] buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);
                for (int i = 0; i < buffer.Length; i += 4)
                {
                    byte a = buffer[i + 3];
                    // Windows treats fully-transparent (alpha=0) pixels of a
                    // layered window as click-through, letting mouse input fall
                    // to whatever is behind it. Flooring to 1 is visually
                    // imperceptible (~0.4% opacity) but keeps the whole window
                    // rectangle grabbable, including "empty" areas of styles
                    // like Dot/Ring/Outline.
                    if (a == 0) { a = 1; buffer[i + 3] = 1; }
                    buffer[i] = (byte)(buffer[i] * a / 255);
                    buffer[i + 1] = (byte)(buffer[i + 1] * a / 255);
                    buffer[i + 2] = (byte)(buffer[i + 2] * a / 255);
                }
                Marshal.Copy(buffer, 0, data.Scan0, bytes);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        #endregion

        #region Style drawing

        private static Color WithAlpha(Color c, float opacity)
        {
            if (opacity < 0f) opacity = 0f;
            if (opacity > 1f) opacity = 1f;
            return Color.FromArgb((int)(c.A * opacity), c.R, c.G, c.B);
        }

        private static Color Darken(Color c, float factor)
        {
            return Color.FromArgb(c.A,
                Clamp((int)(c.R * factor)), Clamp((int)(c.G * factor)), Clamp((int)(c.B * factor)));
        }

        private static Color Lighten(Color c, float amount)
        {
            return Color.FromArgb(c.A,
                Clamp((int)(c.R + (255 - c.R) * amount)),
                Clamp((int)(c.G + (255 - c.G) * amount)),
                Clamp((int)(c.B + (255 - c.B) * amount)));
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }

        private static Color LerpColor(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Color.FromArgb(
                Clamp((int)(a.A + (b.A - a.A) * t)),
                Clamp((int)(a.R + (b.R - a.R) * t)),
                Clamp((int)(a.G + (b.G - a.G) * t)),
                Clamp((int)(a.B + (b.B - a.B) * t)));
        }

        internal static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            float d = radius * 2f;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            if (d < 0f) d = 0f;

            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Kept intentionally tight (a few px) so the soft falloff finishes
        // before it reaches the window edge — anything wider gets hard-clipped
        // by the bitmap boundary, which reads as a square cutout bug rather
        // than a glow.
        private static void DrawGlow(Graphics g, GraphicsPath basePath, RectangleF baseRect, float radius,
            Color color, float opacity)
        {
            if (opacity <= 0.01f) return;
            for (int i = 5; i >= 1; i--)
            {
                float grow = i * 1.0f;
                RectangleF r = RectangleF.Inflate(baseRect, grow, grow);
                using (GraphicsPath p = RoundedRect(r, radius + grow))
                using (SolidBrush b = new SolidBrush(WithAlpha(color, opacity * 0.09f)))
                {
                    g.FillPath(b, p);
                }
            }
        }

        private static void DrawShadowedText(Graphics g, string text, Font font, RectangleF rect,
            Color color, float opacity)
        {
            if (rect.Width <= 0f || rect.Height <= 0f || string.IsNullOrEmpty(text)) return;

            StringFormat fmt = new StringFormat();
            fmt.Alignment = StringAlignment.Center;
            fmt.LineAlignment = StringAlignment.Center;
            fmt.FormatFlags |= StringFormatFlags.NoWrap;

            // Auto-shrink so long labels never overflow a narrow window and get
            // hard-clipped at the bitmap edge — measure once, and if it doesn't
            // fit, draw with a temporary smaller font instead of the caller's.
            Font drawFont = font;
            Font tempFont = null;
            SizeF measured = g.MeasureString(text, font, PointF.Empty, fmt);
            float available = rect.Width - 2f;
            if (measured.Width > available && available > 1f)
            {
                float scale = available / measured.Width;
                float newSize = Math.Max(5f, font.Size * scale);
                tempFont = new Font(font.FontFamily, newSize, font.Style);
                drawFont = tempFont;
            }

            RectangleF shadowRect = rect;
            shadowRect.Offset(0f, 1.4f);
            using (SolidBrush shadowBrush = new SolidBrush(WithAlpha(Color.Black, opacity * 0.5f)))
            {
                g.DrawString(text, drawFont, shadowBrush, shadowRect, fmt);
            }
            using (SolidBrush brush = new SolidBrush(WithAlpha(color, opacity)))
            {
                g.DrawString(text, drawFont, brush, rect, fmt);
            }

            if (tempFont != null) tempFont.Dispose();
            fmt.Dispose();
        }

        private static void DrawStyle(Graphics g, Style style, Rectangle rectI, bool active, IndicatorLabels labels,
            float pulseScale, float pulseGlow, float rawT, float phase, float opacity,
            List<RippleData> ripples, float tiltAngle, int pulseStart,
            float radarAngle, float radarActivity, float radarBlipAngle, float orbitAngle, float matrixOffset,
            float mouseNX, float mouseNY)
        {
            if (opacity <= 0.01f || rectI.Width <= 0 || rectI.Height <= 0) return;

            RectangleF rect = rectI;
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;

            bool bounce = IsLegacyStyle(style);

            GraphicsState state = g.Save();
            if (bounce)
            {
                g.TranslateTransform(cx, cy);
                g.ScaleTransform(pulseScale, pulseScale);
                g.TranslateTransform(-cx, -cy);
            }

            Color accent = active ? ColorOn : ColorOff;

            switch (style)
            {
                case Style.Full:
                    DrawFull(g, rect, accent, active, labels, pulseGlow, opacity);
                    break;
                case Style.Compact:
                    DrawCompact(g, rect, accent, active, labels, pulseGlow, opacity);
                    break;
                case Style.Pill:
                    DrawPill(g, rect, accent, active, labels, pulseGlow, rawT, opacity);
                    break;
                case Style.Ring:
                    DrawRing(g, rect, accent, active, labels, pulseGlow, rawT, opacity);
                    break;
                case Style.Dot:
                    DrawDot(g, rect, accent, active, labels, pulseGlow, opacity);
                    break;
                case Style.Outline:
                    DrawOutline(g, rect, accent, active, labels, pulseGlow, opacity);
                    break;
                case Style.Terminal:
                    DrawTerminal(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Radar:
                    DrawRadar(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity,
                        radarAngle, radarActivity, radarBlipAngle);
                    break;
                case Style.Liquid:
                    DrawLiquid(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Orbit:
                    DrawOrbit(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity, orbitAngle);
                    break;
                case Style.Matrix:
                    DrawMatrix(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity, matrixOffset);
                    break;
                case Style.Equalizer:
                    DrawEqualizer(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Neon:
                    DrawNeon(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Gauge:
                    DrawGauge(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Led:
                    DrawLed(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Text:
                    DrawTextOnly(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Badge3D:
                    DrawBadge3D(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Cube3D:
                    DrawCube3D(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Sphere3D:
                    DrawSphere3D(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Chip3D:
                    DrawChip3D(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity, mouseNX, mouseNY);
                    break;
                case Style.Hud:
                    DrawHud(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Ripple:
                    DrawRipple(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity, ripples);
                    break;
                case Style.TiltLiquid:
                    DrawTiltLiquid(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity, tiltAngle);
                    break;
                case Style.Typing:
                    DrawTyping(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Heartbeat:
                    DrawHeartbeat(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Confetti:
                    DrawConfetti(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity, pulseStart);
                    break;
                case Style.Compass:
                    DrawCompass(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Oscilloscope:
                    DrawOscilloscope(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Crystal:
                    DrawCrystal(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
                case Style.Starfield:
                    DrawStarfield(g, rect, accent, active, labels, pulseGlow, rawT, phase, opacity);
                    break;
            }

            g.Restore(state);
        }

        // Used by the Settings gallery to render a small static swatch of a
        // style without needing a live indicator window behind it.
        public static void RenderPreview(Graphics g, Style style, Rectangle rect, bool active, float phase)
        {
            IndicatorLabels labels = SourceCatalog.GetLabels(IndicatorSource.CapsLock, active, null);
            float previewRadarAngle = (phase * 40f) % 360f;
            float previewOrbitAngle = (phase * 60f) % 360f;
            float previewMatrixOffset = phase * 20f;
            float previewMouseNX = (float)Math.Sin(phase * 0.7) * 0.6f;
            float previewMouseNY = (float)Math.Cos(phase * 0.5) * 0.6f;
            DrawStyle(g, style, rect, active, labels, 1f, 0f, 1f, phase, 1f,
                s_previewRipples, 0f, Environment.TickCount - 100000,
                previewRadarAngle, active ? 1f : 0f, 205f, previewOrbitAngle, previewMatrixOffset,
                previewMouseNX, previewMouseNY);
        }

        private static readonly List<RippleData> s_previewRipples = new List<RippleData>();

        private static void DrawFull(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float radius = Math.Min(panel.Height, panel.Width) * 0.18f;

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                DrawGlow(g, path, panel, radius, accent, opacity * (0.22f + 0.5f * pulseGlow));

                using (LinearGradientBrush grad = new LinearGradientBrush(
                    panel, WithAlpha(Lighten(accent, 0.12f), opacity), WithAlpha(Darken(accent, 0.62f), opacity),
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(grad, path);
                }

                RectangleF glossRect = new RectangleF(panel.X, panel.Y, panel.Width, panel.Height * 0.45f);
                using (GraphicsPath glossPath = RoundedRect(glossRect, radius))
                using (SolidBrush gloss = new SolidBrush(WithAlpha(Color.White, opacity * 0.10f)))
                {
                    g.FillPath(gloss, glossPath);
                }

                using (Pen border = new Pen(WithAlpha(Color.Black, opacity * 0.25f), 1f))
                {
                    g.DrawPath(border, path);
                }
            }

            Font capFont = new Font("Segoe UI Semibold", Math.Max(7f, panel.Height / 7.5f), FontStyle.Regular);
            RectangleF capRect = new RectangleF(panel.X, panel.Y + panel.Height * 0.14f, panel.Width, panel.Height * 0.28f);
            DrawShadowedText(g, labels.Caption, capFont, capRect, Color.White, opacity * 0.85f);
            capFont.Dispose();

            Font stateFont = new Font("Segoe UI", Math.Max(9f, panel.Height / 3.6f), FontStyle.Bold);
            RectangleF stateRect = new RectangleF(panel.X, panel.Y + panel.Height * 0.42f, panel.Width, panel.Height * 0.5f);
            DrawShadowedText(g, labels.Primary, stateFont, stateRect, Color.White, opacity);
            stateFont.Dispose();
        }

        private static void DrawCompact(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -5f, -5f);
            float radius = Math.Min(panel.Height, panel.Width) * 0.28f;

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                DrawGlow(g, path, panel, radius, accent, opacity * (0.18f + 0.45f * pulseGlow));
                using (LinearGradientBrush grad = new LinearGradientBrush(
                    panel, WithAlpha(Lighten(accent, 0.1f), opacity), WithAlpha(Darken(accent, 0.65f), opacity),
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(grad, path);
                }
                using (Pen border = new Pen(WithAlpha(Color.Black, opacity * 0.25f), 1f))
                {
                    g.DrawPath(border, path);
                }
            }

            Font font = new Font("Segoe UI", Math.Max(8f, panel.Height / 2.6f), FontStyle.Bold);
            DrawShadowedText(g, labels.Short + " " + labels.Primary, font, panel, Color.White, opacity);
            font.Dispose();
        }

        private static void DrawPill(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -5f, -5f);
            float radius = panel.Height / 2f;

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                using (SolidBrush glass = new SolidBrush(WithAlpha(Color.FromArgb(255, 28, 30, 36), opacity * 0.82f)))
                {
                    g.FillPath(glass, path);
                }
                using (Pen border = new Pen(WithAlpha(Color.White, opacity * 0.12f), 1f))
                {
                    g.DrawPath(border, path);
                }
            }

            // The indicator morphs from a small gray dot into a stretched
            // rounded green bar (and back), animating smoothly. While ON, the
            // label sits a fixed distance from the window's right edge and
            // the bar stretches to close the fixed gap up to it — so the
            // bar's length is a direct function of the window's own width,
            // and widening the window only ever lengthens the bar, never
            // moves the label further from the edge.
            float eased = EaseOutCubic(Math.Min(1f, Math.Max(0f, pulseT)));
            float morphFrom = active ? 0f : 1f;
            float morphTo = active ? 1f : 0f;
            float morph = morphFrom + (morphTo - morphFrom) * eased;

            float barHeight = panel.Height * 0.5f;
            float barStartX = panel.X + panel.Height * 0.25f;
            float barY = panel.Y + (panel.Height - barHeight) / 2f;

            Font font = new Font("Segoe UI Semibold", Math.Max(8f, panel.Height / 2.8f), FontStyle.Regular);
            SizeF textSize = g.MeasureString(labels.Primary, font);

            float rightMargin = panel.Height * 0.32f;
            float barTextGap = panel.Height * 0.22f;

            float onTextX = panel.Right - rightMargin - textSize.Width;
            float onBarWidth = Math.Max(barHeight, (onTextX - barTextGap) - barStartX);

            float offBarWidth = barHeight;
            float offTextX = barStartX + offBarWidth + barTextGap;

            float barWidth = offBarWidth + (onBarWidth - offBarWidth) * morph;
            float textX = offTextX + (onTextX - offTextX) * morph;

            RectangleF barRect = new RectangleF(barStartX, barY, barWidth, barHeight);
            Color barColor = LerpColor(ColorOff, ColorOn, morph);

            using (GraphicsPath barPath = RoundedRect(barRect, barHeight / 2f))
            {
                DrawGlow(g, barPath, barRect, barHeight / 2f, barColor,
                    opacity * (0.2f + 0.55f * pulseGlow) * (0.25f + 0.75f * morph));
                using (LinearGradientBrush grad = new LinearGradientBrush(
                    barRect, WithAlpha(Lighten(barColor, 0.22f), opacity), WithAlpha(Darken(barColor, 0.7f), opacity),
                    LinearGradientMode.ForwardDiagonal))
                {
                    g.FillPath(grad, barPath);
                }
                using (Pen rim = new Pen(WithAlpha(Color.Black, opacity * 0.28f), 1.2f))
                {
                    g.DrawPath(rim, barPath);
                }
            }

            RectangleF textRect = new RectangleF(textX, panel.Y, Math.Max(1f, textSize.Width + 2f), panel.Height);
            StringFormat fmt = new StringFormat();
            fmt.Alignment = StringAlignment.Near;
            fmt.LineAlignment = StringAlignment.Center;
            using (SolidBrush textBrush = new SolidBrush(WithAlpha(Color.White, opacity * 0.95f)))
            {
                g.DrawString(labels.Primary, font, textBrush, textRect, fmt);
            }
            font.Dispose();
            fmt.Dispose();
        }

        private static void DrawRing(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float opacity)
        {
            float d = Math.Min(rect.Width, rect.Height) - 12f;
            if (d < 6f) d = 6f;
            RectangleF ring = new RectangleF(rect.X + (rect.Width - d) / 2f, rect.Y + (rect.Height - d) / 2f, d, d);
            float thickness = Math.Max(2.5f, d * 0.09f);

            // Smoothly animate the fill sweep and its colour between the two
            // states instead of snapping straight to the new value.
            float eased = EaseOutCubic(Math.Min(1f, Math.Max(0f, pulseT)));
            float fromSweep = active ? 70f : 360f;
            float toSweep = active ? 360f : 70f;
            float sweep = fromSweep + (toSweep - fromSweep) * eased;
            float morphFrom = active ? 0f : 1f;
            float morphTo = active ? 1f : 0f;
            float morph = morphFrom + (morphTo - morphFrom) * eased;
            Color ringColor = LerpColor(ColorOff, ColorOn, morph);

            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(ring);
                DrawGlow(g, glowPath, ring, d / 2f, ringColor, opacity * (0.15f + 0.55f * pulseGlow) * (0.3f + 0.7f * morph));
            }

            using (Pen track = new Pen(WithAlpha(Color.White, opacity * 0.10f), thickness))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                g.DrawEllipse(track, RectangleF.Inflate(ring, -thickness / 2f, -thickness / 2f));
            }

            using (Pen arc = new Pen(WithAlpha(ringColor, opacity), thickness))
            {
                arc.StartCap = LineCap.Round;
                arc.EndCap = LineCap.Round;
                g.DrawArc(arc, RectangleF.Inflate(ring, -thickness / 2f, -thickness / 2f), -90f, sweep);
            }

            PointF center = new PointF(ring.X + ring.Width / 2f, ring.Y + ring.Height / 2f);
            Color iconColor = LerpColor(Color.FromArgb(255, 205, 208, 214), Color.White, morph);
            if (labels.Glyph == "⇪")
            {
                DrawMinimalCapsGlyph(g, center, d * 0.42f, iconColor, opacity);
            }
            else
            {
                Font font = new Font("Segoe UI", Math.Max(9f, d * 0.3f), FontStyle.Bold);
                DrawShadowedText(g, labels.Glyph, font, ring, iconColor, opacity);
                font.Dispose();
            }
        }

        // A small hand-drawn arrow-over-bar glyph (the universal Caps Lock
        // symbol) instead of a font character — guarantees exact centring
        // and a cleaner, more minimal silhouette than most font glyphs give.
        private static void DrawMinimalCapsGlyph(Graphics g, PointF center, float size, Color color, float opacity)
        {
            float aw = size * 0.34f;
            float apexY = center.Y - size * 0.5f;
            float baseY = center.Y - size * 0.02f;
            float stemW = size * 0.16f;
            float barW = size * 0.62f;
            float barH = size * 0.13f;
            float barY = center.Y + size * 0.28f;

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddPolygon(new PointF[]
                {
                    new PointF(center.X, apexY),
                    new PointF(center.X + aw, baseY),
                    new PointF(center.X + stemW / 2f, baseY),
                    new PointF(center.X + stemW / 2f, barY - size * 0.06f),
                    new PointF(center.X - stemW / 2f, barY - size * 0.06f),
                    new PointF(center.X - stemW / 2f, baseY),
                    new PointF(center.X - aw, baseY)
                });
                using (SolidBrush b = new SolidBrush(WithAlpha(color, opacity)))
                {
                    g.FillPath(b, path);
                }
            }

            RectangleF barRect = new RectangleF(center.X - barW / 2f, barY, barW, barH);
            using (SolidBrush b2 = new SolidBrush(WithAlpha(color, opacity)))
            {
                g.FillRectangle(b2, barRect);
            }
        }

        private static void DrawDot(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float opacity)
        {
            float d = Math.Min(rect.Width, rect.Height) - 14f;
            if (d < 4f) d = 4f;
            RectangleF dot = new RectangleF(rect.X + (rect.Width - d) / 2f, rect.Y + (rect.Height - d) / 2f, d, d);

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(dot);
                DrawGlow(g, path, dot, d / 2f, accent, opacity * (0.25f + 0.6f * pulseGlow) * (active ? 1f : 0.35f));

                using (LinearGradientBrush grad = new LinearGradientBrush(
                    dot, WithAlpha(Lighten(accent, 0.25f), opacity), WithAlpha(Darken(accent, 0.7f), opacity),
                    LinearGradientMode.ForwardDiagonal))
                {
                    g.FillEllipse(grad, dot);
                }
                using (Pen rim = new Pen(WithAlpha(Color.Black, opacity * 0.3f), 1.4f))
                {
                    g.DrawEllipse(rim, dot);
                }
            }
        }

        private static void DrawOutline(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float radius = Math.Min(panel.Height, panel.Width) * 0.22f;

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                DrawGlow(g, path, panel, radius, accent, opacity * (0.16f + 0.5f * pulseGlow) * (active ? 1f : 0.25f));
                using (Pen border = new Pen(WithAlpha(accent, opacity), 2f))
                {
                    g.DrawPath(border, path);
                }
            }

            Font font = new Font("Segoe UI Semibold", Math.Max(8f, panel.Height / 3f), FontStyle.Regular);
            DrawShadowedText(g, labels.Short + " " + labels.Primary, font, panel, accent, opacity);
            font.Dispose();
        }

        private static void DrawTerminal(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float radius = Math.Min(panel.Height, panel.Width) * 0.12f;

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                DrawGlow(g, path, panel, radius, accent, opacity * (0.12f + 0.5f * pulseGlow) * (active ? 1f : 0.3f));

                using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 8, 14, 10), opacity * 0.92f)))
                {
                    g.FillPath(bg, path);
                }

                using (Pen scan = new Pen(WithAlpha(Color.White, opacity * 0.035f), 1f))
                {
                    for (float y = panel.Y + 3f; y < panel.Bottom - 2f; y += 3.5f)
                    {
                        g.DrawLine(scan, panel.X + 2f, y, panel.Right - 2f, y);
                    }
                }

                using (Pen border = new Pen(WithAlpha(accent, opacity * 0.7f), 1.2f))
                {
                    g.DrawPath(border, path);
                }
            }

            string full = (labels.Short + ": " + labels.Primary);
            string full2 = "> " + full;

            Font font = new Font("Consolas", Math.Max(8f, panel.Height / 3.4f), FontStyle.Bold);
            SizeF textSize = g.MeasureString(full2, font);
            float innerWidth = panel.Width - 6f;
            if (textSize.Width > innerWidth && innerWidth > 1f)
            {
                float shrink = innerWidth / textSize.Width;
                Font shrunk = new Font("Consolas", Math.Max(6f, font.Size * shrink), FontStyle.Bold);
                font.Dispose();
                font = shrunk;
                textSize = g.MeasureString(full2, font);
            }

            int revealLen = (int)Math.Round(pulseT * full2.Length);
            if (revealLen < 0) revealLen = 0;
            if (revealLen > full2.Length) revealLen = full2.Length;
            string shown = full2.Substring(0, revealLen);

            float textX = panel.X + (panel.Width - textSize.Width) / 2f;
            float textY = panel.Y + (panel.Height - textSize.Height) / 2f;

            Color textColor = active ? Color.FromArgb(255, 140, 255, 170) : Color.FromArgb(255, 150, 150, 150);
            using (SolidBrush brush = new SolidBrush(WithAlpha(textColor, opacity)))
            {
                g.DrawString(shown, font, brush, textX, textY);
            }

            SizeF shownSize = g.MeasureString(shown, font);
            bool cursorVisible = ((int)(phase * 2f)) % 2 == 0;
            if (cursorVisible)
            {
                float cursorWidth = Math.Max(2f, font.Size * 0.16f);
                float cursorX = textX + shownSize.Width;
                float maxRight = panel.Right - 3f;
                if (cursorX + cursorWidth > maxRight) cursorX = maxRight - cursorWidth;

                RectangleF cursorRect = new RectangleF(cursorX, textY + 1f, cursorWidth, textSize.Height - 2f);
                using (SolidBrush cb = new SolidBrush(WithAlpha(textColor, opacity * 0.9f)))
                {
                    g.FillRectangle(cb, cursorRect);
                }
            }

            font.Dispose();
        }

        private static void DrawRadar(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity,
            float headAngle, float activityT, float blipAngle)
        {
            float d = Math.Min(rect.Width, rect.Height) - 12f;
            if (d < 10f) d = 10f;
            RectangleF circle = new RectangleF(rect.X + (rect.Width - d) / 2f, rect.Y + (rect.Height - d) / 2f, d, d);
            PointF center = new PointF(circle.X + d / 2f, circle.Y + d / 2f);
            float radius = d / 2f;

            Color sweepColor = LerpColor(ColorOff, ColorOn, activityT);

            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(circle);
                DrawGlow(g, glowPath, circle, radius, sweepColor, opacity * (0.12f + 0.4f * pulseGlow) * (0.25f + 0.75f * activityT));
            }

            using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 6, 14, 10), opacity * 0.85f)))
            {
                g.FillEllipse(bg, circle);
            }

            using (Pen ringPen = new Pen(WithAlpha(sweepColor, opacity * 0.25f), 1f))
            {
                g.DrawEllipse(ringPen, circle);
                g.DrawEllipse(ringPen, RectangleF.Inflate(circle, -radius * 0.33f, -radius * 0.33f));
                g.DrawEllipse(ringPen, RectangleF.Inflate(circle, -radius * 0.66f, -radius * 0.66f));
                g.DrawLine(ringPen, circle.X, center.Y, circle.Right, center.Y);
                g.DrawLine(ringPen, center.X, circle.Y, center.X, circle.Bottom);
            }

            // Filled thin pie slices (rather than a handful of discrete lines)
            // give a much smoother-looking sweep gradient, and the angle is
            // driven by an externally-integrated value so speed changes ease
            // in continuously instead of jumping to a new position.
            GraphicsState clip = g.Save();
            g.SetClip(circle);
            int trailSteps = 70;
            float trailSpan = 60f;
            float sliceWidth = trailSpan / trailSteps * 1.6f;
            for (int i = 0; i < trailSteps; i++)
            {
                float t = i / (float)(trailSteps - 1);
                float a = headAngle - t * trailSpan;
                float taper = (1f - t) * (1f - t);
                float alpha = taper * (0.25f + 0.75f * activityT);
                using (SolidBrush sweepBrush = new SolidBrush(WithAlpha(sweepColor, opacity * alpha)))
                {
                    g.FillPie(sweepBrush, circle.X, circle.Y, circle.Width, circle.Height, a - sliceWidth / 2f, sliceWidth);
                }
            }
            g.Restore(clip);

            // The blip's position tracks the mouse cursor on screen; it only
            // fades in once the sweep actually passes over its angle.
            float diff = Math.Abs(((headAngle - blipAngle + 540f) % 360f) - 180f);
            float blipStrength = Math.Max(0f, 1f - diff / 26f);
            if (blipStrength > 0f)
            {
                float rad = blipAngle * (float)Math.PI / 180f;
                PointF bp = new PointF(center.X + radius * 0.62f * (float)Math.Cos(rad), center.Y + radius * 0.62f * (float)Math.Sin(rad));
                float bs = 3f + blipStrength * 3f;
                using (SolidBrush blipBrush = new SolidBrush(WithAlpha(sweepColor, opacity * blipStrength)))
                {
                    g.FillEllipse(blipBrush, bp.X - bs / 2f, bp.Y - bs / 2f, bs, bs);
                }
            }

            using (Pen border = new Pen(WithAlpha(Color.Black, opacity * 0.3f), 1f))
            {
                g.DrawEllipse(border, circle);
            }
        }

        private static void DrawLiquid(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float radius = Math.Min(panel.Height, panel.Width) * 0.22f;

            using (GraphicsPath containerPath = RoundedRect(panel, radius))
            {
                DrawGlow(g, containerPath, panel, radius, accent, opacity * (0.1f + 0.4f * pulseGlow) * (active ? 1f : 0.2f));

                using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 14, 16, 22), opacity * 0.85f)))
                {
                    g.FillPath(bg, containerPath);
                }

                GraphicsState clipState = g.Save();
                g.SetClip(containerPath);

                float targetLevel = active ? 0.82f : 0.16f;
                float fromLevel = active ? 0.16f : 0.82f;
                float level = fromLevel + (targetLevel - fromLevel) * EaseOutCubic(pulseT);

                float levelY = panel.Bottom - panel.Height * level;
                float amplitude = panel.Height * 0.035f;
                float waveLen = Math.Max(20f, panel.Width * 0.6f);

                using (GraphicsPath wavePath = new GraphicsPath())
                {
                    PointF[] pts = new PointF[24];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float t = i / (float)(pts.Length - 1);
                        float x = panel.X + t * panel.Width;
                        float y = levelY + amplitude * (float)Math.Sin((x / waveLen) * 2f * Math.PI + phase * 3f);
                        pts[i] = new PointF(x, y);
                    }
                    wavePath.AddLine(panel.X, panel.Bottom + 4f, panel.X, pts[0].Y);
                    wavePath.AddLines(pts);
                    wavePath.AddLine(pts[pts.Length - 1].X, pts[pts.Length - 1].Y, panel.Right, panel.Bottom + 4f);
                    wavePath.CloseFigure();

                    using (LinearGradientBrush grad = new LinearGradientBrush(
                        new PointF(panel.X, levelY - amplitude), new PointF(panel.X, panel.Bottom),
                        WithAlpha(Lighten(accent, 0.15f), opacity * 0.9f), WithAlpha(Darken(accent, 0.6f), opacity * 0.9f)))
                    {
                        g.FillPath(grad, wavePath);
                    }
                }

                g.Restore(clipState);

                using (Pen border = new Pen(WithAlpha(Color.White, opacity * 0.12f), 1f))
                {
                    g.DrawPath(border, containerPath);
                }
            }

            Font font = new Font("Segoe UI Semibold", Math.Max(8f, panel.Height / 3.2f), FontStyle.Regular);
            DrawShadowedText(g, labels.Primary, font, panel, Color.White, opacity);
            font.Dispose();
        }

        private static void DrawOrbit(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity, float orbitAngle)
        {
            float size = Math.Min(rect.Width, rect.Height);
            PointF center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            float orbitRadius = size * 0.34f;
            float coreRadius = size * 0.11f;

            RectangleF coreRect = new RectangleF(center.X - coreRadius, center.Y - coreRadius, coreRadius * 2f, coreRadius * 2f);
            using (GraphicsPath corePath = new GraphicsPath())
            {
                corePath.AddEllipse(coreRect);
                DrawGlow(g, corePath, coreRect, coreRadius, accent, opacity * (0.25f + 0.6f * pulseGlow) * (active ? 1f : 0.3f));
                using (SolidBrush coreBrush = new SolidBrush(WithAlpha(accent, opacity)))
                {
                    g.FillEllipse(coreBrush, coreRect);
                }
            }

            int particleCount = 3;
            float particleRadius = size * 0.055f;

            for (int i = 0; i < particleCount; i++)
            {
                float baseAngle = orbitAngle + i * (360f / particleCount);

                for (int trail = 5; trail >= 0; trail--)
                {
                    float a = baseAngle - trail * 9f;
                    float rad = a * (float)Math.PI / 180f;
                    PointF p = new PointF(center.X + orbitRadius * (float)Math.Cos(rad), center.Y + orbitRadius * (float)Math.Sin(rad));
                    float alpha = (1f - trail / 6f) * (trail == 0 ? 1f : 0.35f);
                    float pr = particleRadius * (trail == 0 ? 1f : 0.7f);
                    using (SolidBrush pb = new SolidBrush(WithAlpha(accent, opacity * alpha)))
                    {
                        g.FillEllipse(pb, p.X - pr, p.Y - pr, pr * 2f, pr * 2f);
                    }
                }
            }

            using (Pen orbitPen = new Pen(WithAlpha(Color.White, opacity * 0.08f), 1f))
            {
                g.DrawEllipse(orbitPen, center.X - orbitRadius, center.Y - orbitRadius, orbitRadius * 2f, orbitRadius * 2f);
            }
        }

        private static void DrawMatrix(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity, float matrixOffset)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float radius = Math.Min(panel.Height, panel.Width) * 0.12f;

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                DrawGlow(g, path, panel, radius, accent, opacity * (0.1f + 0.35f * pulseGlow) * (active ? 1f : 0.2f));
                using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 5, 10, 6), opacity * 0.94f)))
                {
                    g.FillPath(bg, path);
                }

                GraphicsState clip = g.Save();
                g.SetClip(path);

                Font font = new Font("Consolas", Math.Max(7f, panel.Height * 0.16f), FontStyle.Bold);
                float cellW = font.Size * 0.9f;
                float cellH = font.Size * 1.05f;
                int cols = Math.Max(1, (int)(panel.Width / cellW));
                float trailLen = panel.Height * 0.9f;
                int rows = (int)(panel.Height / cellH) + 2;
                float cycleLen = panel.Height + trailLen;

                for (int c = 0; c < cols; c++)
                {
                    float colSeed = c * 53f + 17f;
                    float raw = (matrixOffset + colSeed) % cycleLen;
                    if (raw < 0f) raw += cycleLen;
                    float headY = panel.Y - trailLen + raw;

                    for (int r = 0; r < rows; r++)
                    {
                        float y = panel.Y + r * cellH;
                        float dist = headY - y;
                        if (dist < 0f || dist > trailLen) continue;
                        float t = dist / trailLen;
                        float a = (1f - t) * (1f - t);
                        if (a < 0.02f) continue;

                        int charCode = (c * 31 + r * 17 + (int)(phase * 3f)) % 2;
                        string ch = (charCode == 0) ? "0" : "1";

                        Color col = (dist < cellH * 1.2f)
                            ? WithAlpha(Color.White, opacity * a * (active ? 1f : 0.5f))
                            : WithAlpha(accent, opacity * a * (active ? 0.9f : 0.4f));

                        using (SolidBrush b = new SolidBrush(col))
                        {
                            g.DrawString(ch, font, b, panel.X + c * cellW, y);
                        }
                    }
                }

                font.Dispose();
                g.Restore(clip);

                using (Pen border = new Pen(WithAlpha(accent, opacity * 0.35f), 1f))
                {
                    g.DrawPath(border, path);
                }
            }
        }

        private static void DrawEqualizer(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            int bars = 5;
            float gap = panel.Width * 0.05f;
            float barWidth = (panel.Width - gap * (bars - 1)) / bars;

            // Bars are driven by real keyboard activity (s_typingActivity, the
            // same shared/decaying meter the Typing style uses) rather than a
            // fixed sine sweep, so the equalizer only "dances" while the user
            // is actually typing and settles to a calm idle wobble otherwise.
            float activity = s_typingActivity;

            for (int i = 0; i < bars; i++)
            {
                float freq = 3.6f + i * 2.1f;
                float offset = i * 2.3f;
                float noise = (float)((Math.Sin(phase * freq + offset) + Math.Sin(phase * freq * 1.7f + offset * 0.6f) * 0.6f) / 1.6);
                noise = (noise + 1f) * 0.5f; // 0..1
                float idle = 0.05f + 0.02f * (float)Math.Sin(phase * 0.6f + offset);
                float wave = idle + activity * noise;
                if (wave > 1f) wave = 1f;
                if (wave < 0f) wave = 0f;

                float minH = panel.Height * 0.08f;
                float h = minH + (panel.Height - minH) * wave;
                RectangleF barRect = new RectangleF(panel.X + i * (barWidth + gap), panel.Bottom - h, barWidth, h);
                float radius = barWidth * 0.4f;

                using (GraphicsPath path = RoundedRect(barRect, radius))
                {
                    DrawGlow(g, path, barRect, radius, accent, opacity * (0.08f + 0.35f * pulseGlow) * (0.15f + 0.55f * activity));
                    using (LinearGradientBrush grad = new LinearGradientBrush(
                        barRect, WithAlpha(Lighten(accent, 0.2f), opacity), WithAlpha(Darken(accent, 0.55f), opacity),
                        LinearGradientMode.Vertical))
                    {
                        g.FillPath(grad, path);
                    }
                }
            }
        }

        private static void DrawNeon(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float radius = Math.Min(panel.Height, panel.Width) * 0.3f;

            float baseFlicker = 0.9f + 0.1f * (float)Math.Sin(phase * 5.0);
            float dip = (((int)(phase * 23f)) % 47 == 0) ? 0.3f : 1f;
            float flicker = baseFlicker * dip;
            float glowStrength = active ? flicker : 0.18f;

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                DrawGlow(g, path, panel, radius, accent, opacity * (0.35f + 0.5f * pulseGlow) * glowStrength);
                using (Pen tube = new Pen(WithAlpha(Lighten(accent, 0.3f), opacity * (active ? flicker : 0.35f)), 3f))
                {
                    tube.LineJoin = LineJoin.Round;
                    g.DrawPath(tube, path);
                }
                using (Pen core = new Pen(WithAlpha(Color.White, opacity * (active ? flicker * 0.8f : 0.15f)), 1f))
                {
                    g.DrawPath(core, path);
                }
            }

            Font font = new Font("Segoe UI", Math.Max(9f, panel.Height / 3.4f), FontStyle.Bold);
            Color textColor = active ? Lighten(accent, 0.25f) : Color.FromArgb(255, 90, 96, 100);
            float textOpacity = opacity * (active ? Math.Max(0.55f, flicker) : 0.5f);
            DrawShadowedText(g, labels.Short, font, panel, textColor, textOpacity);
            font.Dispose();
        }

        private static void DrawGauge(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            float radius = Math.Min(rect.Width, rect.Height * 1.7f) * 0.46f;
            PointF pivot = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height * 0.82f);
            RectangleF arcRect = new RectangleF(pivot.X - radius, pivot.Y - radius, radius * 2f, radius * 2f);

            using (Pen track = new Pen(WithAlpha(Color.White, opacity * 0.12f), Math.Max(3f, radius * 0.14f)))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                g.DrawArc(track, arcRect, 180f, 180f);
            }

            using (Pen activeArc = new Pen(WithAlpha(accent, opacity * 0.8f), Math.Max(3f, radius * 0.14f)))
            {
                activeArc.StartCap = LineCap.Round;
                activeArc.EndCap = LineCap.Round;
                float sweep = active ? 180f : 30f;
                g.DrawArc(activeArc, arcRect, 180f, sweep);
            }

            // The needle always animates from the angle of the state it's
            // leaving to the angle of the state it's entering — hardcoding
            // fromAngle made the off transition a same-value no-op sweep, so
            // the needle just snapped straight back instead of sweeping.
            float fromAngle = active ? 200f : 340f;
            float toAngle = active ? 340f : 200f;
            float eased = EaseOutCubic(Math.Min(1f, Math.Max(0f, pulseT)));
            float overshoot = (float)Math.Sin(eased * Math.PI) * 8f * (1f - eased);
            float angle = fromAngle + (toAngle - fromAngle) * eased + (active ? overshoot : -overshoot);
            float rad = angle * (float)Math.PI / 180f;

            PointF needleTip = new PointF(pivot.X + radius * 0.86f * (float)Math.Cos(rad),
                pivot.Y + radius * 0.86f * (float)Math.Sin(rad));

            using (Pen needlePen = new Pen(WithAlpha(Color.White, opacity * 0.95f), Math.Max(2f, radius * 0.07f)))
            {
                needlePen.StartCap = LineCap.Round;
                needlePen.EndCap = LineCap.Round;
                g.DrawLine(needlePen, pivot, needleTip);
            }

            float hubR = radius * 0.14f;
            using (SolidBrush hub = new SolidBrush(WithAlpha(accent, opacity)))
            {
                g.FillEllipse(hub, pivot.X - hubR, pivot.Y - hubR, hubR * 2f, hubR * 2f);
            }
            using (Pen hubRing = new Pen(WithAlpha(Color.Black, opacity * 0.4f), 1f))
            {
                g.DrawEllipse(hubRing, pivot.X - hubR, pivot.Y - hubR, hubR * 2f, hubR * 2f);
            }

            // Sits below the arc's peak (which can reach close to the panel's
            // top edge) so the label text never crosses the dial ring.
            Font font = new Font("Segoe UI Semibold", Math.Max(7f, rect.Height * 0.14f), FontStyle.Regular);
            RectangleF labelRect = new RectangleF(rect.X, rect.Y + rect.Height * 0.32f, rect.Width, rect.Height * 0.24f);
            DrawShadowedText(g, labels.Primary, font, labelRect, Color.White, opacity * 0.85f);
            font.Dispose();
        }

        private static void DrawLed(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            float d = Math.Min(rect.Width, rect.Height) - 12f;
            if (d < 6f) d = 6f;
            RectangleF bulb = new RectangleF(rect.X + (rect.Width - d) / 2f, rect.Y + (rect.Height - d) / 2f, d, d);

            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(bulb);
                DrawGlow(g, glowPath, bulb, d / 2f, accent, opacity * (active ? 0.35f : 0.04f) * (1f + pulseGlow));
            }

            using (SolidBrush bezel = new SolidBrush(WithAlpha(Color.FromArgb(255, 40, 40, 44), opacity)))
            {
                g.FillEllipse(bezel, bulb);
            }

            RectangleF inner = RectangleF.Inflate(bulb, -d * 0.12f, -d * 0.12f);
            Color litColor = active ? accent : Color.FromArgb(255, 40, 55, 46);
            using (GraphicsPath lensPath = new GraphicsPath())
            {
                lensPath.AddEllipse(inner);
                using (PathGradientBrush pg = new PathGradientBrush(lensPath))
                {
                    pg.CenterPoint = new PointF(inner.X + inner.Width * 0.35f, inner.Y + inner.Height * 0.3f);
                    pg.CenterColor = WithAlpha(Lighten(litColor, active ? 0.5f : 0.15f), opacity);
                    pg.SurroundColors = new Color[] { WithAlpha(Darken(litColor, active ? 0.3f : 0.1f), opacity) };
                    g.FillPath(pg, lensPath);
                }
            }

            RectangleF spec = new RectangleF(inner.X + inner.Width * 0.18f, inner.Y + inner.Height * 0.14f,
                inner.Width * 0.32f, inner.Height * 0.22f);
            using (SolidBrush specBrush = new SolidBrush(WithAlpha(Color.White, opacity * (active ? 0.55f : 0.25f))))
            {
                g.FillEllipse(specBrush, spec);
            }

            using (Pen rim = new Pen(WithAlpha(Color.Black, opacity * 0.5f), 1.4f))
            {
                g.DrawEllipse(rim, bulb);
            }
        }

        private static void DrawTextOnly(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            // Stays minimal (no box, no glow) but still reacts to a toggle:
            // pops in with a small scale+fade instead of just snapping text.
            float t = Math.Min(1f, Math.Max(0f, pulseT));
            float te = EaseOutCubic(t);
            float scale = 0.8f + 0.2f * te;

            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-cx, -cy);

            Font font = new Font("Segoe UI", Math.Max(9f, rect.Height * 0.32f), FontStyle.Bold);
            Color c = active ? accent : Color.FromArgb(255, 150, 150, 150);
            DrawShadowedText(g, active ? labels.Caption : labels.Caption.ToLowerInvariant(), font, rect, c, opacity * (0.4f + 0.6f * te));
            font.Dispose();

            g.Restore(st);
        }

        private static void DrawBadge3D(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float radius = Math.Min(panel.Width, panel.Height) * 0.2f;
            float press = 1f - 0.05f * (float)Math.Sin(Math.Min(1f, Math.Max(0f, pulseT)) * Math.PI);
            RectangleF p = RectangleF.Inflate(panel, -panel.Width * (1f - press) * 0.05f, -panel.Height * (1f - press) * 0.05f);

            // A stack of shrinking, darkening slices offset downward gives the
            // badge a visible extruded side face — the flat single-face
            // version this replaced had no depth cue at all, so it just read
            // as a plain gradient rectangle instead of "3D".
            float depth = Math.Min(p.Width, p.Height) * 0.16f * press;
            int layers = 7;
            for (int i = layers; i >= 1; i--)
            {
                float t = i / (float)layers;
                RectangleF layerRect = p;
                layerRect.Offset(0f, depth * t);
                using (GraphicsPath lp = RoundedRect(layerRect, radius))
                using (SolidBrush lb = new SolidBrush(WithAlpha(Darken(accent, 0.75f - 0.35f * t), opacity)))
                {
                    g.FillPath(lb, lp);
                }
            }
            using (GraphicsPath shadowPath = RoundedRect(new RectangleF(p.X, p.Bottom + depth - 1f, p.Width, depth * 0.6f), radius))
            using (SolidBrush shadowBrush = new SolidBrush(WithAlpha(Color.Black, opacity * 0.35f)))
            {
                g.FillPath(shadowBrush, shadowPath);
            }

            using (GraphicsPath path = RoundedRect(p, radius))
            {
                DrawGlow(g, path, p, radius, accent, opacity * (0.15f + 0.4f * pulseGlow));

                using (LinearGradientBrush grad = new LinearGradientBrush(p,
                    WithAlpha(Lighten(accent, 0.15f), opacity), WithAlpha(Darken(accent, 0.55f), opacity),
                    LinearGradientMode.ForwardDiagonal))
                {
                    g.FillPath(grad, path);
                }

                // slow diagonal sheen sweeping across the surface, like light
                // catching a glossy button — continuous, not just a toggle flash
                GraphicsState sheenClip = g.Save();
                g.SetClip(path);
                float sweepPos = ((phase * 0.35f) % 1.6f) - 0.3f;
                float sx = p.X + p.Width * sweepPos;
                using (LinearGradientBrush sheen = new LinearGradientBrush(
                    new PointF(sx - p.Width * 0.3f, p.Y), new PointF(sx + p.Width * 0.3f, p.Bottom),
                    Color.Transparent, WithAlpha(Color.White, opacity * 0.3f)))
                {
                    Blend blend = new Blend();
                    blend.Positions = new float[] { 0f, 0.5f, 1f };
                    blend.Factors = new float[] { 0f, 1f, 0f };
                    sheen.Blend = blend;
                    g.FillRectangle(sheen, p.X - p.Width, p.Y, p.Width * 3f, p.Height);
                }
                g.Restore(sheenClip);
            }

            Font font = new Font("Segoe UI", Math.Max(8f, p.Height / 3f), FontStyle.Bold);
            DrawShadowedText(g, labels.Primary, font, p, Color.White, opacity);
            font.Dispose();
        }

        private static void DrawCube3D(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            float hs = Math.Min(rect.Width, rect.Height) * 0.32f;
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;

            float cubeEased = EaseOutCubic(Math.Min(1f, Math.Max(0f, pulseT)));
            float cubeOnMix = active ? cubeEased : 1f - cubeEased;
            float rotRad = phase * (12f + cubeOnMix * 28f) * (float)Math.PI / 180f;

            // Two things move together, the way a real turntable cube reads:
            // the front dividing edge sweeps between the shoulders (which
            // face currently reads as dominant), AND the outer envelope
            // itself narrows a little as that edge nears either shoulder
            // (near edge-on) and widens back out as it crosses the middle
            // (corner-on) — so the whole silhouette visibly turns instead of
            // only that one internal vertex sliding left and right. The
            // narrowing is bounded (never below ~0.8x) so it never collapses
            // back into the old squashed-sliver bug.
            float mid = 0.5f + 0.5f * (float)Math.Cos(rotRad);
            float skew = Math.Abs(mid - 0.5f) * 2f;
            float half = hs * 0.87f * (1f - skew * 0.22f);

            PointF t = new PointF(cx, cy - hs);
            PointF l = new PointF(cx - half, cy - hs * 0.5f);
            PointF r = new PointF(cx + half, cy - hs * 0.5f);
            PointF bl = new PointF(cx - half, cy + hs * 0.5f);
            PointF br = new PointF(cx + half, cy + hs * 0.5f);
            PointF b = new PointF(cx, cy + hs);

            PointF c = new PointF(l.X + (r.X - l.X) * mid, cy);
            float leftScale = mid;
            float rightScale = 1f - mid;

            // Colour eases between the off/on tone over the toggle instead of
            // snapping the instant `active` flips, matching the smooth
            // transition every other style animates through.
            Color baseC = LerpColor(Color.FromArgb(255, 90, 96, 104), ColorOn, cubeOnMix);

            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddPolygon(new PointF[] { t, r, br, b, bl, l });
                DrawGlow(g, glowPath, new RectangleF(cx - hs, cy - hs, hs * 2f, hs * 2f), hs * 0.3f,
                    baseC, opacity * (0.14f + 0.45f * pulseGlow) * (0.3f + 0.7f * cubeOnMix));
            }

            float sheen = (float)((Math.Sin(phase * 1.6) + 1.0) / 2.0);

            // Pushed further apart than the original (top brighter, sides
            // darker) so the three faces stay visually distinct — and read
            // clearly as a cube — even at the small sizes these indicators
            // usually render at.
            using (GraphicsPath topFace = new GraphicsPath())
            using (SolidBrush topBrush = new SolidBrush(WithAlpha(Lighten(baseC, 0.45f + sheen * 0.2f), opacity)))
            {
                topFace.AddPolygon(new PointF[] { t, r, c, l });
                g.FillPath(topBrush, topFace);
            }

            using (GraphicsPath leftFace = new GraphicsPath())
            using (SolidBrush leftBrush = new SolidBrush(WithAlpha(Darken(baseC, 0.48f + 0.35f * leftScale), opacity)))
            {
                leftFace.AddPolygon(new PointF[] { l, c, b, bl });
                g.FillPath(leftBrush, leftFace);
            }

            using (GraphicsPath rightFace = new GraphicsPath())
            using (SolidBrush rightBrush = new SolidBrush(WithAlpha(Darken(baseC, 0.18f + 0.55f * rightScale), opacity)))
            {
                rightFace.AddPolygon(new PointF[] { c, r, br, b });
                g.FillPath(rightBrush, rightFace);
            }

            using (Pen edge = new Pen(WithAlpha(Color.Black, opacity * 0.55f), 1.3f))
            {
                edge.LineJoin = LineJoin.Round;
                g.DrawPolygon(edge, new PointF[] { t, r, br, b, bl, l });
                g.DrawLine(edge, l, c);
                g.DrawLine(edge, c, r);
                g.DrawLine(edge, c, b);
            }
        }

        private static void DrawSphere3D(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            float d = Math.Min(rect.Width, rect.Height) - 12f;
            if (d < 6f) d = 6f;
            RectangleF ball = new RectangleF(rect.X + (rect.Width - d) / 2f, rect.Y + (rect.Height - d) / 2f, d, d);

            // Colour, glow strength and rotation speed all ease smoothly
            // between the off/on look via onMix instead of snapping the
            // instant `active` flips — the previous version reassigned these
            // straight off the boolean, so the sphere would visibly "teleport"
            // colour and speed the frame the state changed.
            float sphereEased = EaseOutCubic(Math.Min(1f, Math.Max(0f, pulseT)));
            float onMix = active ? sphereEased : 1f - sphereEased;
            Color sphereColor = LerpColor(ColorOff, ColorOn, onMix);
            float breathe = onMix * (0.5f + 0.5f * (float)Math.Sin(phase * 2.4));

            // The highlight orbits the surface continuously — reads as the
            // sphere itself slowly turning under a fixed light, not a static
            // sticker glued to a ball.
            float rotSpeed = 9f + onMix * 46f;
            float rotAngle = phase * rotSpeed * (float)Math.PI / 180f;
            float orbitR = d * 0.16f;
            float hcx = ball.X + ball.Width * 0.5f + orbitR * (float)Math.Cos(rotAngle) - d * 0.06f;
            float hcy = ball.Y + ball.Height * 0.42f + orbitR * 0.65f * (float)Math.Sin(rotAngle) - d * 0.06f;

            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(ball);
                DrawGlow(g, glowPath, ball, d / 2f, sphereColor,
                    opacity * (0.15f + 0.35f * breathe + 0.4f * pulseGlow) * (0.25f + 0.75f * onMix));
            }

            using (GraphicsPath spherePath = new GraphicsPath())
            {
                spherePath.AddEllipse(ball);
                using (PathGradientBrush pg = new PathGradientBrush(spherePath))
                {
                    pg.CenterPoint = new PointF(hcx, hcy);
                    pg.CenterColor = WithAlpha(Lighten(sphereColor, 0.4f + breathe * 0.15f), opacity);
                    pg.SurroundColors = new Color[] { WithAlpha(Darken(sphereColor, 0.45f), opacity) };
                    g.FillPath(pg, spherePath);
                }
            }

            RectangleF spec = new RectangleF(hcx - ball.Width * 0.13f, hcy - ball.Height * 0.09f,
                ball.Width * 0.26f, ball.Height * 0.18f);
            using (SolidBrush specBrush = new SolidBrush(WithAlpha(Color.White, opacity * 0.65f)))
            {
                g.FillEllipse(specBrush, spec);
            }
        }

        // "Spotlight" — replaces the old inert Chip style. A soft highlight
        // tracks the mouse across the panel and a small core drifts toward
        // the cursor with a bit of parallax, so the style visibly reacts to
        // mouse movement instead of just sitting there as a static pill.
        private static void DrawChip3D(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity, float mouseNX, float mouseNY)
        {
            RectangleF panel = RectangleF.Inflate(rect, -5f, -5f);
            float radius = Math.Min(panel.Width, panel.Height) * 0.24f;

            float eased = EaseOutCubic(Math.Min(1f, Math.Max(0f, pulseT)));
            float onMix = active ? eased : 1f - eased;
            Color liveColor = LerpColor(ColorOff, ColorOn, onMix);

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                DrawGlow(g, path, panel, radius, liveColor, opacity * (0.1f + 0.3f * pulseGlow) * (0.2f + 0.6f * onMix));

                using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 16, 18, 24), opacity * 0.94f)))
                {
                    g.FillPath(bg, path);
                }

                GraphicsState clip = g.Save();
                g.SetClip(path);

                // Soft highlight that follows the cursor across the surface.
                float spotX = panel.X + panel.Width * (0.5f + mouseNX * 0.42f);
                float spotY = panel.Y + panel.Height * (0.5f + mouseNY * 0.42f);
                float spotR = Math.Max(panel.Width, panel.Height) * 0.9f;
                using (GraphicsPath spotPath = new GraphicsPath())
                {
                    spotPath.AddEllipse(spotX - spotR, spotY - spotR, spotR * 2f, spotR * 2f);
                    using (PathGradientBrush spot = new PathGradientBrush(spotPath))
                    {
                        spot.CenterPoint = new PointF(spotX, spotY);
                        spot.CenterColor = WithAlpha(Lighten(liveColor, 0.4f), opacity * (0.28f + 0.22f * onMix));
                        spot.SurroundColors = new Color[] { WithAlpha(liveColor, 0f) };
                        g.FillPath(spot, spotPath);
                    }
                }

                g.Restore(clip);

                using (Pen border = new Pen(WithAlpha(Color.White, opacity * 0.1f), 1f))
                {
                    g.DrawPath(border, path);
                }

                // A rim highlight brightens on whichever edge faces the
                // cursor, reinforcing the sense that light is following it.
                float mouseDist = (float)Math.Sqrt(mouseNX * mouseNX + mouseNY * mouseNY);
                if (mouseDist > 0.02f)
                {
                    float rimAngle = (float)(Math.Atan2(mouseNY, mouseNX) * 180.0 / Math.PI);
                    using (Pen rim = new Pen(WithAlpha(Lighten(liveColor, 0.45f),
                        opacity * (0.35f + 0.35f * onMix) * Math.Min(1f, mouseDist)), 1.6f))
                    {
                        RectangleF rr = RectangleF.Inflate(panel, -1f, -1f);
                        g.DrawArc(rim, rr, rimAngle - 40f, 80f);
                    }
                }
            }

            // Parallax core: drifts a few px toward the cursor, like a bead
            // resting under a tilting glass surface.
            RectangleF core = RectangleF.Inflate(panel, -panel.Width * 0.30f, -panel.Height * 0.30f);
            core.Offset(mouseNX * panel.Width * 0.07f, mouseNY * panel.Height * 0.07f);
            float coreR = Math.Min(core.Width, core.Height) / 2f;
            float breathe = onMix * (0.5f + 0.5f * (float)Math.Sin(phase * 2.2));
            using (GraphicsPath corePath = RoundedRect(core, coreR))
            {
                using (SolidBrush coreBg = new SolidBrush(WithAlpha(Darken(liveColor, 0.6f), opacity)))
                {
                    g.FillPath(coreBg, corePath);
                }
                using (SolidBrush coreGlow = new SolidBrush(
                    WithAlpha(liveColor, opacity * (0.3f + 0.3f * breathe + 0.4f * pulseGlow) * (0.25f + 0.75f * onMix))))
                {
                    g.FillPath(coreGlow, corePath);
                }
                using (Pen inset = new Pen(WithAlpha(Color.Black, opacity * 0.4f), 1.2f))
                {
                    g.DrawPath(inset, corePath);
                }
            }

            // labelRect must be at least as tall as the font itself — it
            // previously wasn't (font size panel.Height/4 inside a
            // panel.Height*0.2 box), so the glyph overflowed the box
            // vertically and got hard-clipped at the window's own top edge.
            float labelH = panel.Height * 0.3f;
            RectangleF labelRect = new RectangleF(panel.X, panel.Bottom - labelH, panel.Width, labelH);
            Font font = new Font("Segoe UI Semibold", Math.Max(7f, labelH * 0.56f), FontStyle.Regular);
            DrawShadowedText(g, labels.Primary, font, labelRect, Color.FromArgb(255, 210, 212, 218), opacity * 0.8f);
            font.Dispose();
        }

        private static GraphicsPath NotchedRect(RectangleF r, float notch)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddLine(r.X + notch, r.Y, r.Right - notch, r.Y);
            p.AddLine(r.Right - notch, r.Y, r.Right, r.Y + notch);
            p.AddLine(r.Right, r.Y + notch, r.Right, r.Bottom - notch);
            p.AddLine(r.Right, r.Bottom - notch, r.Right - notch, r.Bottom);
            p.AddLine(r.Right - notch, r.Bottom, r.X + notch, r.Bottom);
            p.AddLine(r.X + notch, r.Bottom, r.X, r.Bottom - notch);
            p.AddLine(r.X, r.Bottom - notch, r.X, r.Y + notch);
            p.CloseFigure();
            return p;
        }

        private static void DrawHud(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float notch = Math.Min(panel.Width, panel.Height) * 0.18f;

            using (GraphicsPath path = NotchedRect(panel, notch))
            {
                DrawGlow(g, path, panel, notch, accent, opacity * (0.15f + 0.4f * pulseGlow) * (active ? 1f : 0.25f));
                using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 6, 14, 16), opacity * 0.85f)))
                {
                    g.FillPath(bg, path);
                }

                GraphicsState clip = g.Save();
                g.SetClip(path);
                float scanY = panel.Y + ((phase * panel.Height * 0.5f) % (panel.Height + 10f)) - 5f;
                using (LinearGradientBrush scanBrush = new LinearGradientBrush(
                    new PointF(panel.X, scanY - 6f), new PointF(panel.X, scanY + 6f),
                    WithAlpha(accent, 0f), WithAlpha(accent, opacity * (active ? 0.35f : 0.1f))))
                {
                    g.FillRectangle(scanBrush, panel.X, scanY - 6f, panel.Width, 12f);
                }
                g.Restore(clip);

                using (Pen border = new Pen(WithAlpha(accent, opacity * (active ? 0.9f : 0.4f)), 1.4f))
                {
                    g.DrawPath(border, path);
                }
            }

            using (Pen tick = new Pen(WithAlpha(accent, opacity * 0.7f), 1.6f))
            {
                float tl = notch * 0.5f;
                g.DrawLine(tick, panel.X, panel.Y + notch * 1.3f, panel.X, panel.Y + notch * 1.3f + tl);
                g.DrawLine(tick, panel.Right, panel.Bottom - notch * 1.3f, panel.Right, panel.Bottom - notch * 1.3f - tl);
            }

            Font font = new Font("Consolas", Math.Max(7f, panel.Height / 4.2f), FontStyle.Bold);
            Color textColor = active ? Lighten(accent, 0.3f) : Color.FromArgb(255, 120, 150, 150);
            DrawShadowedText(g, labels.Short + "::" + labels.Primary, font, panel, textColor, opacity);
            font.Dispose();
        }

        private static void DrawRipple(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity, List<RippleData> ripples)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float radius = Math.Min(panel.Width, panel.Height) * 0.2f;

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                using (SolidBrush bg = new SolidBrush(WithAlpha(Darken(accent, active ? 0.5f : 0.3f), opacity * 0.5f)))
                {
                    g.FillPath(bg, path);
                }

                GraphicsState clip = g.Save();
                g.SetClip(path);

                // Real ripples only ever appeared on mouse hover/click, so
                // without interaction the style just sat there as a flat
                // panel. A slow, always-on ambient ripple emanating from the
                // centre keeps it visibly alive, on top of any real
                // interactive ripples layered above it.
                PointF ambientCenter = new PointF(panel.X + panel.Width / 2f, panel.Y + panel.Height / 2f);
                float ambientSpeed = active ? 0.55f : 0.3f;
                int ambientCount = 3;
                float ambientMaxR = Math.Max(panel.Width, panel.Height) * 0.8f;
                for (int k = 0; k < ambientCount; k++)
                {
                    float t = ((phase * ambientSpeed) + k / (float)ambientCount) % 1f;
                    float radiusPx = t * ambientMaxR;
                    float a = (1f - t) * (active ? 0.5f : 0.22f);
                    using (Pen ambientPen = new Pen(WithAlpha(Lighten(accent, 0.25f), opacity * a), 1.5f))
                    {
                        g.DrawEllipse(ambientPen, ambientCenter.X - radiusPx, ambientCenter.Y - radiusPx, radiusPx * 2f, radiusPx * 2f);
                    }
                }

                int now = Environment.TickCount;
                for (int i = ripples.Count - 1; i >= 0; i--)
                {
                    RippleData rp = ripples[i];
                    float age = (now - rp.StartTick) / 900f;
                    if (age >= 1f) { ripples.RemoveAt(i); continue; }
                    float radiusPx = age * Math.Max(panel.Width, panel.Height);
                    float a = 1f - age;
                    using (Pen ripplePen = new Pen(WithAlpha(Lighten(accent, 0.3f), opacity * a * 0.8f), 2f))
                    {
                        g.DrawEllipse(ripplePen, rp.X - radiusPx, rp.Y - radiusPx, radiusPx * 2f, radiusPx * 2f);
                    }
                }

                g.Restore(clip);
                using (Pen border = new Pen(WithAlpha(Color.White, opacity * 0.15f), 1f))
                {
                    g.DrawPath(border, path);
                }
            }

            Font font = new Font("Segoe UI Semibold", Math.Max(8f, panel.Height / 3.4f), FontStyle.Regular);
            DrawShadowedText(g, labels.Primary, font, panel, Color.White, opacity * 0.85f);
            font.Dispose();
        }

        private static void DrawTiltLiquid(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity, float tiltAngle)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float radius = Math.Min(panel.Width, panel.Height) * 0.22f;

            using (GraphicsPath containerPath = RoundedRect(panel, radius))
            {
                DrawGlow(g, containerPath, panel, radius, accent, opacity * (0.1f + 0.35f * pulseGlow) * (active ? 1f : 0.2f));
                using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 12, 16, 20), opacity * 0.85f)))
                {
                    g.FillPath(bg, containerPath);
                }

                GraphicsState clipState = g.Save();
                g.SetClip(containerPath);

                float cx = panel.X + panel.Width / 2f;
                float cy = panel.Y + panel.Height / 2f;
                g.TranslateTransform(cx, cy);
                g.RotateTransform(tiltAngle);
                g.TranslateTransform(-cx, -cy);

                float level = active ? 0.78f : 0.22f;
                float levelY = panel.Bottom - panel.Height * level;
                float amplitude = panel.Height * 0.03f;
                float waveLen = Math.Max(20f, panel.Width * 0.55f);
                float bigW = panel.Width * 2.2f;
                float bigX = cx - bigW / 2f;

                using (GraphicsPath wavePath = new GraphicsPath())
                {
                    int n = 28;
                    PointF[] pts = new PointF[n];
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)(n - 1);
                        float x = bigX + t * bigW;
                        float y = levelY + amplitude * (float)Math.Sin((x / waveLen) * 2f * Math.PI + phase * 2.6f);
                        pts[i] = new PointF(x, y);
                    }
                    wavePath.AddLine(bigX, panel.Bottom + bigW, bigX, pts[0].Y);
                    wavePath.AddLines(pts);
                    wavePath.AddLine(pts[n - 1].X, pts[n - 1].Y, bigX + bigW, panel.Bottom + bigW);
                    wavePath.CloseFigure();

                    using (LinearGradientBrush grad = new LinearGradientBrush(
                        new PointF(cx, levelY - amplitude), new PointF(cx, panel.Bottom),
                        WithAlpha(Lighten(accent, 0.15f), opacity * 0.9f), WithAlpha(Darken(accent, 0.6f), opacity * 0.9f)))
                    {
                        g.FillPath(grad, wavePath);
                    }
                }

                g.Restore(clipState);
                using (Pen border = new Pen(WithAlpha(Color.White, opacity * 0.12f), 1f))
                {
                    g.DrawPath(border, containerPath);
                }
            }

            Font font = new Font("Segoe UI Semibold", Math.Max(8f, panel.Height / 3.2f), FontStyle.Regular);
            DrawShadowedText(g, labels.Primary, font, panel, Color.White, opacity);
            font.Dispose();
        }

        private static void DrawTyping(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            float d = Math.Min(rect.Width, rect.Height) - 14f;
            if (d < 6f) d = 6f;
            RectangleF core = new RectangleF(rect.X + (rect.Width - d) / 2f, rect.Y + (rect.Height - d) / 2f, d, d);

            float activity = s_typingActivity;
            float scale = 1f + activity * 0.25f;
            RectangleF scaled = RectangleF.Inflate(core, core.Width * (scale - 1f) / 2f, core.Height * (scale - 1f) / 2f);

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(scaled);
                DrawGlow(g, path, scaled, d / 2f, accent,
                    opacity * (0.15f + 0.6f * activity + 0.3f * pulseGlow) * (active ? 1f : 0.4f));
                using (SolidBrush b = new SolidBrush(WithAlpha(accent, opacity * (0.55f + 0.35f * activity))))
                {
                    g.FillEllipse(b, scaled);
                }
            }

            PointF cpt = new PointF(core.X + core.Width / 2f, core.Y + core.Height / 2f);
            int ticks = 8;
            for (int i = 0; i < ticks; i++)
            {
                float a = i * (360f / ticks) + phase * 40f;
                float rad = a * (float)Math.PI / 180f;
                float r1 = d * 0.62f;
                float r2 = r1 + 3f + activity * 5f;
                PointF p1 = new PointF(cpt.X + r1 * (float)Math.Cos(rad), cpt.Y + r1 * (float)Math.Sin(rad));
                PointF p2 = new PointF(cpt.X + r2 * (float)Math.Cos(rad), cpt.Y + r2 * (float)Math.Sin(rad));
                using (Pen tickPen = new Pen(WithAlpha(accent, opacity * (0.15f + 0.5f * activity)), 1.6f))
                {
                    g.DrawLine(tickPen, p1, p2);
                }
            }
        }

        private static float EcgSignal(float t)
        {
            float v;
            if (t > 0.38f && t < 0.42f) v = (t - 0.38f) / 0.04f * 0.3f;
            else if (t >= 0.42f && t < 0.46f) v = 0.3f - (t - 0.42f) / 0.04f * 1.3f;
            else if (t >= 0.46f && t < 0.5f) v = -1f + (t - 0.46f) / 0.04f * 1.5f;
            else if (t >= 0.5f && t < 0.56f) v = 0.5f - (t - 0.5f) / 0.06f * 0.5f;
            else v = 0f;
            return v;
        }

        private static void DrawHeartbeat(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float radius = Math.Min(panel.Width, panel.Height) * 0.14f;

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                DrawGlow(g, path, panel, radius, accent, opacity * (0.1f + 0.3f * pulseGlow) * (active ? 1f : 0.2f));
                using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 8, 10, 10), opacity * 0.9f)))
                {
                    g.FillPath(bg, path);
                }

                GraphicsState clip = g.Save();
                g.SetClip(path);

                float speed = active ? 1.1f : 0.35f;
                float mag = active ? 1f : 0.25f;
                int n = 120;
                PointF[] pts = new PointF[n];
                float cycleLen = panel.Width * 0.55f;
                for (int i = 0; i < n; i++)
                {
                    float x = panel.X + panel.Width * i / (float)(n - 1);
                    float cyclePos = ((x - panel.X) / cycleLen) - phase * speed;
                    cyclePos = cyclePos - (float)Math.Floor(cyclePos);
                    float v = EcgSignal(cyclePos) * mag;
                    float y = panel.Y + panel.Height * 0.5f - v * panel.Height * 0.4f;
                    pts[i] = new PointF(x, y);
                }
                using (Pen linePen = new Pen(WithAlpha(Lighten(accent, 0.2f), opacity * (active ? 0.95f : 0.5f)), 1.8f))
                {
                    linePen.LineJoin = LineJoin.Round;
                    g.DrawLines(linePen, pts);
                }

                g.Restore(clip);
                using (Pen border = new Pen(WithAlpha(accent, opacity * 0.3f), 1f))
                {
                    g.DrawPath(border, path);
                }
            }
        }

        private static void DrawConfetti(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity, int pulseStart)
        {
            RectangleF panel = RectangleF.Inflate(rect, -2f, -2f);
            float radius = Math.Min(panel.Width, panel.Height) * 0.16f;

            using (GraphicsPath basePath = RoundedRect(panel, radius))
            using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 20, 22, 26), opacity * 0.5f)))
            {
                g.FillPath(bg, basePath);
            }

            Font font = new Font("Segoe UI Semibold", Math.Max(8f, panel.Height / 3.2f), FontStyle.Regular);
            DrawShadowedText(g, labels.Primary, font, panel, Color.White, opacity * 0.9f);
            font.Dispose();

            float elapsedMs = Environment.TickCount - pulseStart;
            float cx = panel.X + panel.Width / 2f;
            float cy = panel.Y + panel.Height * 0.4f;

            if (active)
            {
                float dur = 1300f;
                if (elapsedMs < 0f || elapsedMs > dur) return;
                float t = elapsedMs / 1000f;

                GraphicsState clip = g.Save();
                g.SetClip(new RectangleF(rect.X - 4f, rect.Y - 4f, rect.Width + 8f, rect.Height + 40f));

                int count = 22;
                Color[] palette = new Color[]
                {
                    Color.FromArgb(255, 255, 99, 132), Color.FromArgb(255, 255, 206, 84),
                    Color.FromArgb(255, 75, 192, 192), Color.FromArgb(255, 153, 102, 255),
                    Lighten(accent, 0.3f)
                };

                for (int i = 0; i < count; i++)
                {
                    int seed = i * 977;
                    float ang = (seed % 360) * (float)Math.PI / 180f;
                    float speed = 40f + (seed % 50);
                    float vx = (float)Math.Cos(ang) * speed;
                    float vy = (float)Math.Sin(ang) * speed - 60f;
                    float gravity = 140f;
                    float x = cx + vx * t;
                    float y = cy + vy * t + 0.5f * gravity * t * t;
                    float fade = Math.Max(0f, 1f - t / (dur / 1000f));
                    Color col = palette[i % palette.Length];
                    float rot = seed % 180;

                    using (SolidBrush pb = new SolidBrush(WithAlpha(col, opacity * fade)))
                    {
                        GraphicsState s2 = g.Save();
                        g.TranslateTransform(x, y);
                        g.RotateTransform(rot + t * 200f);
                        g.FillRectangle(pb, -2.5f, -1.5f, 5f, 3f);
                        g.Restore(s2);
                    }
                }

                g.Restore(clip);
            }
            else
            {
                // Turning off had no animation at all — the confetti just
                // vanished the instant the state flipped. A quick, muted
                // "dust settle" gives the deactivation its own visible beat
                // instead of a hard cut to the bare panel.
                float dur = 550f;
                if (elapsedMs < 0f || elapsedMs > dur) return;
                float t = elapsedMs / 1000f;

                GraphicsState clip = g.Save();
                g.SetClip(new RectangleF(rect.X - 4f, rect.Y - 4f, rect.Width + 8f, rect.Height + 20f));

                int count = 14;
                for (int i = 0; i < count; i++)
                {
                    int seed = i * 613 + 11;
                    float ang = (seed % 360) * (float)Math.PI / 180f;
                    float outSpeed = 18f + (seed % 26);
                    float vx = (float)Math.Cos(ang) * outSpeed;
                    float vy = 50f + (seed % 25);
                    float x = cx + vx * t;
                    float y = cy + vy * t;
                    float fade = Math.Max(0f, 1f - t / (dur / 1000f));
                    float size = 2f + (seed % 3) * 0.6f;

                    using (SolidBrush pb = new SolidBrush(WithAlpha(Color.FromArgb(255, 150, 155, 165), opacity * fade * 0.65f)))
                    {
                        g.FillEllipse(pb, x - size / 2f, y - size / 2f, size, size);
                    }
                }

                g.Restore(clip);
            }
        }

        private static void DrawCompass(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            float d = Math.Min(rect.Width, rect.Height) - 12f;
            if (d < 10f) d = 10f;
            RectangleF circle = new RectangleF(rect.X + (rect.Width - d) / 2f, rect.Y + (rect.Height - d) / 2f, d, d);
            PointF center = new PointF(circle.X + d / 2f, circle.Y + d / 2f);
            float radius = d / 2f;

            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(circle);
                DrawGlow(g, glowPath, circle, radius, accent, opacity * (0.1f + 0.35f * pulseGlow) * (active ? 1f : 0.2f));
            }

            using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 18, 20, 24), opacity * 0.85f)))
            {
                g.FillEllipse(bg, circle);
            }

            using (Pen ring = new Pen(WithAlpha(Color.White, opacity * 0.2f), 1.2f))
            {
                g.DrawEllipse(ring, RectangleF.Inflate(circle, -2f, -2f));
            }

            string[] dirs = new string[] { "N", "E", "S", "W" };
            Font dirFont = new Font("Segoe UI", Math.Max(6f, radius * 0.22f), FontStyle.Bold);
            StringFormat dfmt = new StringFormat();
            dfmt.Alignment = StringAlignment.Center;
            dfmt.LineAlignment = StringAlignment.Center;
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f - 90f;
                float rad = a * (float)Math.PI / 180f;
                PointF pp = new PointF(center.X + radius * 0.76f * (float)Math.Cos(rad), center.Y + radius * 0.76f * (float)Math.Sin(rad));
                RectangleF dr = new RectangleF(pp.X - 8f, pp.Y - 8f, 16f, 16f);
                using (SolidBrush db = new SolidBrush(WithAlpha(Color.FromArgb(255, 190, 195, 200), opacity * 0.6f)))
                {
                    g.DrawString(dirs[i], dirFont, db, dr, dfmt);
                }
            }
            dirFont.Dispose();
            dfmt.Dispose();

            float fromAngle = active ? 90f : -90f;
            float toAngle = active ? -90f : 90f;
            float eased = EaseOutCubic(Math.Min(1f, Math.Max(0f, pulseT)));
            float overshoot = (float)Math.Sin(eased * Math.PI) * 10f * (1f - eased) * (active ? -1f : 1f);
            float jitter = (float)Math.Sin(phase * 3.3) * 1.5f;
            float angle = fromAngle + (toAngle - fromAngle) * eased + overshoot + jitter;
            float rad2 = angle * (float)Math.PI / 180f;

            PointF needleN = new PointF(center.X + radius * 0.7f * (float)Math.Cos(rad2), center.Y + radius * 0.7f * (float)Math.Sin(rad2));
            PointF needleS = new PointF(center.X - radius * 0.4f * (float)Math.Cos(rad2), center.Y - radius * 0.4f * (float)Math.Sin(rad2));

            using (Pen needleNPen = new Pen(WithAlpha(accent, opacity), 2.4f))
            {
                needleNPen.StartCap = LineCap.Round;
                needleNPen.EndCap = LineCap.Triangle;
                g.DrawLine(needleNPen, center, needleN);
            }
            using (Pen needleSPen = new Pen(WithAlpha(Color.FromArgb(255, 200, 200, 200), opacity * 0.8f), 2.4f))
            {
                needleSPen.StartCap = LineCap.Round;
                needleSPen.EndCap = LineCap.Triangle;
                g.DrawLine(needleSPen, center, needleS);
            }

            float hubR = radius * 0.09f;
            using (SolidBrush hub = new SolidBrush(WithAlpha(Color.FromArgb(255, 230, 230, 230), opacity)))
            {
                g.FillEllipse(hub, center.X - hubR, center.Y - hubR, hubR * 2f, hubR * 2f);
            }
        }

        private static void DrawOscilloscope(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -6f, -6f);
            float radius = Math.Min(panel.Width, panel.Height) * 0.14f;

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                DrawGlow(g, path, panel, radius, accent, opacity * (0.12f + 0.35f * pulseGlow) * (active ? 1f : 0.2f));
                using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 6, 10, 8), opacity * 0.9f)))
                {
                    g.FillPath(bg, path);
                }

                GraphicsState clip = g.Save();
                g.SetClip(path);

                using (Pen gridPen = new Pen(WithAlpha(Color.White, opacity * 0.05f), 1f))
                {
                    g.DrawLine(gridPen, panel.X, panel.Y + panel.Height / 2f, panel.Right, panel.Y + panel.Height / 2f);
                }

                float freq = active ? 3.2f : 1f;
                float amp = active ? 0.32f : 0.08f;
                int n = 90;
                PointF[] pts = new PointF[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)(n - 1);
                    float x = panel.X + t * panel.Width;
                    float y = panel.Y + panel.Height * 0.5f
                        - amp * panel.Height * (float)Math.Sin(t * freq * 2f * Math.PI * 2f - phase * 4f)
                        - amp * panel.Height * 0.3f * (float)Math.Sin(t * freq * 2f * Math.PI * 5f - phase * 7f);
                    pts[i] = new PointF(x, y);
                }
                using (Pen wavePen = new Pen(WithAlpha(Lighten(accent, 0.2f), opacity * (active ? 0.95f : 0.5f)), 1.6f))
                {
                    wavePen.LineJoin = LineJoin.Round;
                    g.DrawLines(wavePen, pts);
                }

                g.Restore(clip);
                using (Pen border = new Pen(WithAlpha(accent, opacity * 0.3f), 1f))
                {
                    g.DrawPath(border, path);
                }
            }
        }

        // A faceted gem with a genuine multi-sided cross-section (instead of
        // the old flat two-triangle diamond), slowly turning, with a moving
        // sparkle glint on its brightest facet and — once lit — thin prism
        // rays refracting outward. Colour/glow ease smoothly across the
        // on/off toggle rather than snapping with `active`.
        private static void DrawCrystal(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            float w = Math.Min(rect.Width, rect.Height * 1.3f) * 0.62f;
            float h = rect.Height * 0.76f;
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;

            float eased = EaseOutCubic(Math.Min(1f, Math.Max(0f, pulseT)));
            float onMix = active ? eased : 1f - eased;
            Color baseC = LerpColor(Color.FromArgb(255, 120, 126, 134), ColorOn, onMix);

            float rot = phase * (6f + onMix * 10f);
            const int sides = 6;
            PointF apex = new PointF(cx, cy - h * 0.5f);
            PointF baseTip = new PointF(cx, cy + h * 0.5f);
            float ringR = w * 0.5f;
            float ringY = cy - h * 0.06f;
            PointF[] ring = new PointF[sides];
            float[] ringAngle = new float[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = rot + i * (360f / sides);
                ringAngle[i] = a;
                float rad = a * (float)Math.PI / 180f;
                ring[i] = new PointF(cx + ringR * (float)Math.Cos(rad) * 0.95f, ringY + ringR * (float)Math.Sin(rad) * 0.34f);
            }

            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(cx - w * 0.6f, cy - h * 0.6f, w * 1.2f, h * 1.2f);
                DrawGlow(g, glowPath, new RectangleF(cx - w * 0.6f, cy - h * 0.6f, w * 1.2f, h * 1.2f), w * 0.25f,
                    baseC, opacity * (0.12f + 0.35f * pulseGlow) * (0.25f + 0.75f * onMix));
            }

            // Thin prism rays fan out once the gem is lit, rotating slowly —
            // a light-refraction cue that reads clearly even at small sizes.
            if (onMix > 0.02f)
            {
                int rays = 10;
                for (int i = 0; i < rays; i++)
                {
                    float a = rot * 0.4f + i * (360f / rays);
                    float rad = a * (float)Math.PI / 180f;
                    float twinkle = 0.4f + 0.6f * (float)((Math.Sin(phase * 3f + i * 1.7) + 1.0) / 2.0);
                    float len = Math.Max(w, h) * (0.55f + 0.25f * twinkle);
                    PointF far = new PointF(cx + (float)Math.Cos(rad) * len, cy + (float)Math.Sin(rad) * len * 0.9f);
                    Color rayColor = LerpColor(baseC, Color.FromArgb(255,
                        (int)(180 + 60 * Math.Sin(a * 0.05)), (int)(180 + 60 * Math.Sin(a * 0.05 + 2)), (int)(180 + 60 * Math.Sin(a * 0.05 + 4))), 0.5f);
                    using (Pen rayPen = new Pen(WithAlpha(rayColor, opacity * 0.16f * onMix * twinkle), 1f))
                    {
                        g.DrawLine(rayPen, new PointF(cx, cy - h * 0.06f), far);
                    }
                }
            }

            // Solid backing fill spanning the gem's full extent, drawn before
            // the individual facets — without it, the hairline anti-aliased
            // seam between two adjacent facet polygons let the panel
            // background peek through as a visible "missing side" gap.
            PointF leftMost = ring[0];
            PointF rightMost = ring[0];
            for (int i = 1; i < sides; i++)
            {
                if (ring[i].X < leftMost.X) leftMost = ring[i];
                if (ring[i].X > rightMost.X) rightMost = ring[i];
            }
            using (GraphicsPath basePath = new GraphicsPath())
            using (SolidBrush baseBrush = new SolidBrush(WithAlpha(Darken(baseC, 0.5f), opacity)))
            {
                basePath.AddPolygon(new PointF[] { apex, rightMost, baseTip, leftMost });
                g.FillPath(baseBrush, basePath);
            }

            // Facets: upper pyramid (apex..ring[i]..ring[i+1]) and lower
            // pyramid (baseTip..ring[i]..ring[i+1]), each shaded by a fake
            // light direction so the gem reads as faceted rather than flat.
            float brightest = float.MinValue;
            int brightestI = 0;
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                float mid = ringAngle[i] + (360f / sides) / 2f;
                float light = 0.5f + 0.5f * (float)Math.Cos((mid - 55f) * Math.PI / 180f);
                if (light > brightest) { brightest = light; brightestI = i; }

                Color upperColor = LerpColor(Darken(baseC, 0.45f), Lighten(baseC, 0.55f), light);
                using (GraphicsPath fp = new GraphicsPath())
                using (SolidBrush fb = new SolidBrush(WithAlpha(upperColor, opacity)))
                {
                    fp.AddPolygon(new PointF[] { apex, ring[i], ring[j] });
                    g.FillPath(fb, fp);
                }

                Color lowerColor = LerpColor(Darken(baseC, 0.65f), Lighten(baseC, 0.15f), light);
                using (GraphicsPath fp2 = new GraphicsPath())
                using (SolidBrush fb2 = new SolidBrush(WithAlpha(lowerColor, opacity)))
                {
                    fp2.AddPolygon(new PointF[] { baseTip, ring[i], ring[j] });
                    g.FillPath(fb2, fp2);
                }

                using (Pen edge = new Pen(WithAlpha(Color.Black, opacity * 0.35f), 1f))
                {
                    g.DrawLine(edge, apex, ring[i]);
                    g.DrawLine(edge, baseTip, ring[i]);
                    g.DrawLine(edge, ring[i], ring[j]);
                }
            }

            // A small bright cross-flare glints on the currently brightest
            // facet, sweeping around the gem as it turns.
            int gi = brightestI, gj = (brightestI + 1) % sides;
            PointF glintP = new PointF((ring[gi].X + ring[gj].X + apex.X) / 3f, (ring[gi].Y + ring[gj].Y + apex.Y) / 3f);
            float glintT = (float)((Math.Sin(phase * 5f) + 1.0) / 2.0);
            float glintSize = (2.5f + glintT * 2f) * (0.4f + 0.6f * onMix);
            using (Pen flare = new Pen(WithAlpha(Color.White, opacity * (0.5f + 0.4f * glintT) * (0.3f + 0.7f * onMix)), 1.3f))
            {
                g.DrawLine(flare, glintP.X - glintSize, glintP.Y, glintP.X + glintSize, glintP.Y);
                g.DrawLine(flare, glintP.X, glintP.Y - glintSize, glintP.X, glintP.Y + glintSize);
            }
        }

        private static float Hash(int seed)
        {
            unchecked
            {
                int x = seed * 374761393;
                x = (x ^ (x >> 13)) * 1274126177;
                x = x ^ (x >> 16);
                return (x & 0x7FFFFFFF) / (float)0x7FFFFFFF;
            }
        }

        // "Constellation" — replaces plain independent twinkling dots with
        // faint lines linking nearby stars and a periodic shooting star
        // streaking across the panel, so it reads as a scene rather than
        // scattered noise. Colour eases smoothly across the on/off toggle.
        private static void DrawStarfield(Graphics g, RectangleF rect, Color accent, bool active, IndicatorLabels labels,
            float pulseGlow, float pulseT, float phase, float opacity)
        {
            RectangleF panel = RectangleF.Inflate(rect, -2f, -2f);
            float radius = Math.Min(panel.Width, panel.Height) * 0.12f;

            float eased = EaseOutCubic(Math.Min(1f, Math.Max(0f, pulseT)));
            float onMix = active ? eased : 1f - eased;
            Color starColor = LerpColor(Color.FromArgb(255, 170, 175, 185), Lighten(ColorOn, 0.3f), onMix);

            using (GraphicsPath path = RoundedRect(panel, radius))
            {
                using (SolidBrush bg = new SolidBrush(WithAlpha(Color.FromArgb(255, 6, 6, 14), opacity * 0.92f)))
                {
                    g.FillPath(bg, path);
                }

                GraphicsState clip = g.Save();
                g.SetClip(path);

                int count = 16;
                PointF[] pts = new PointF[count];
                float[] tws = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float rx = Hash(i * 13 + 1);
                    float ry = Hash(i * 29 + 7);
                    pts[i] = new PointF(panel.X + rx * panel.Width, panel.Y + ry * panel.Height);
                    float speed = 0.6f + Hash(i * 41 + 3) * 1.4f;
                    tws[i] = (float)((Math.Sin(phase * speed + i) + 1.0) / 2.0);
                }

                float maxLinkDist = Math.Max(panel.Width, panel.Height) * 0.4f;
                for (int i = 0; i < count; i++)
                {
                    for (int j = i + 1; j < count; j++)
                    {
                        float dx = pts[i].X - pts[j].X;
                        float dy = pts[i].Y - pts[j].Y;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dist > maxLinkDist) continue;
                        float a = (1f - dist / maxLinkDist) * 0.14f * (0.4f + 0.6f * onMix) * ((tws[i] + tws[j]) * 0.5f);
                        using (Pen linkPen = new Pen(WithAlpha(starColor, opacity * a), 1f))
                        {
                            g.DrawLine(linkPen, pts[i], pts[j]);
                        }
                    }
                }

                for (int i = 0; i < count; i++)
                {
                    float size = 0.9f + Hash(i * 53 + 5) * 1.7f;
                    float a = 0.25f + tws[i] * 0.65f;
                    using (SolidBrush sb = new SolidBrush(WithAlpha(starColor, opacity * a)))
                    {
                        g.FillEllipse(sb, pts[i].X - size / 2f, pts[i].Y - size / 2f, size, size);
                    }
                }

                // A shooting star periodically streaks across the panel.
                float cycle = 3.4f;
                float phaseMod = phase - cycle * (float)Math.Floor(phase / cycle);
                float st = phaseMod / cycle;
                if (st < 0.28f)
                {
                    float u = st / 0.28f;
                    float sx = panel.X - panel.Width * 0.15f + u * panel.Width * 1.3f;
                    float sy = panel.Y + panel.Height * 0.15f + u * panel.Height * 0.55f;
                    float tailLen = panel.Width * 0.22f;
                    PointF tail = new PointF(sx - tailLen, sy - tailLen * 0.42f);
                    float fade = (float)Math.Sin(u * Math.PI);
                    using (Pen streak = new Pen(WithAlpha(Color.White, opacity * fade * (0.5f + 0.5f * onMix)), 1.4f))
                    {
                        streak.StartCap = LineCap.Round;
                        streak.EndCap = LineCap.Round;
                        g.DrawLine(streak, tail, new PointF(sx, sy));
                    }
                    using (SolidBrush headB = new SolidBrush(WithAlpha(Color.White, opacity * fade)))
                    {
                        g.FillEllipse(headB, sx - 1.2f, sy - 1.2f, 2.4f, 2.4f);
                    }
                }

                g.Restore(clip);
                using (Pen border = new Pen(WithAlpha(Color.White, opacity * 0.08f), 1f))
                {
                    g.DrawPath(border, path);
                }
            }

            Font font = new Font("Segoe UI Semibold", Math.Max(7f, panel.Height / 4f), FontStyle.Regular);
            RectangleF labelRect = new RectangleF(panel.X, panel.Bottom - panel.Height * 0.28f, panel.Width, panel.Height * 0.24f);
            DrawShadowedText(g, labels.Primary, font, labelRect, starColor, opacity * 0.85f);
            font.Dispose();
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_pollTimer != null) _pollTimer.Dispose();
                if (_animTimer != null) _animTimer.Dispose();
                if (_lockBadgeForm != null) _lockBadgeForm.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
