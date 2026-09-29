using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapsIndicator
{
    internal sealed class SettingsForm : Form
    {
        private AppConfig _config;
        private readonly List<IndicatorForm> _indicators = new List<IndicatorForm>();
        private IndicatorForm _selectedIndicator;
        private bool _allVisible = true;
        private Style _defaultStyleForNewWindows = Style.Full;

        private Panel _tabStrip;
        private Panel _tabContent;
        private Panel _tabStyles, _tabHotkeys, _tabWindows, _tabTech, _tabHelp;
        private TabHeaderButton[] _tabHeaderButtons;
        private int _selectedTabIndex;

        private readonly List<StyleCard> _cards = new List<StyleCard>();
        private Timer _galleryTimer;

        private readonly Dictionary<string, HotkeyRow> _hotkeyRows = new Dictionary<string, HotkeyRow>();
        private string _capturingId;
        private HotkeyRow _capturingRow;

        private FlowLayoutPanel _windowsFlow;
        private FlowLayoutPanel _hotkeysFlow;
        private FlatButton _addWindowButton;
        private readonly List<WindowRow> _rows = new List<WindowRow>();
        private readonly ToolTip _tipWindows = new ToolTip();

        private FlowLayoutPanel _helpFlow;
        private readonly List<Label> _helpHeaderLabels = new List<Label>();
        private readonly List<Label> _helpBodyLabels = new List<Label>();

        private CheckBox _cbAutostart, _cbSaveWindowPositions, _cbSaveStyles, _cbSaveSizes,
            _cbShowDragFrame, _cbTopmost, _cbAutoStartIndicators, _cbCapsLockLock, _cbLayoutSwitchLock,
            _cbMuteAll;
        private ComboBox _fpsBox;
        private ComboBox _layoutModeBox;
        private TrackBar _volumeSlider;
        private Label _volumeValueLabel;

        private bool _reallyClosing;
        private readonly uint _currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        public SettingsForm(bool startHidden)
        {
            Theme.ApplyDark(this);
            Text = "CapsIndicator — Настройки";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ControlBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            // Wide enough that the Windows tab's ~900px-wide rows (source,
            // style, position, lock badges, sound controls) fit without
            // triggering a horizontal scrollbar at the default size.
            Size = new Size(980, 640);
            MinimumSize = new Size(760, 460);
            KeyPreview = true;
            Icon = null;

            KeyDown += SettingsForm_KeyDown;
            FormClosing += SettingsForm_FormClosing;

            _config = AppConfig.Load(AppConfig.DefaultPath());
            if (_config == null) _config = AppConfig.CreateDefault();

            KeyboardLockService.CapsLockLocked = _config.CapsLockLockEnabled;
            KeyboardLockService.LayoutSwitchLocked = _config.LayoutSwitchLockEnabled;
            KeyboardLockService.Install();

            SoundService.Muted = _config.SoundMuted;
            SoundService.Volume = _config.SoundVolume;

            MouseSelectionWatcher.IsProtectedPoint = PointIsProtected;
            MouseSelectionWatcher.OutsideClick += DeselectViaOutsideClick;
            MouseSelectionWatcher.Install();

            BuildUi();

            List<string> failedIds = ApplyAllHotkeysIndividually();
            if (failedIds.Count > 0)
            {
                SaveConfig();
                RefreshAllHotkeyRows();
                List<string> names = new List<string>();
                foreach (string id in failedIds) names.Add(NameFor(id));
                MessageBox.Show(this,
                    "Эти хоткеи уже заняты другими программами и были сброшены (остальные не тронуты):\n"
                    + string.Join("\n", names.ToArray()),
                    "CapsIndicator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SelectTab(1);
            }

            if (_config.AutoStartIndicatorsWithApp)
            {
                RestoreWindows();
            }

            _cbAutostart.Checked = AutostartHelper.IsEnabled();

            if (!startHidden || failedIds.Count > 0)
            {
                Show();
                BringInFrontOfTopmostIndicators();
            }
        }

        #region Hotkeys

        private static string NameFor(string id)
        {
            foreach (HotkeyCatalog.Entry e in HotkeyCatalog.All)
            {
                if (e.Id == id) return e.Name;
            }
            return id;
        }

        // Every hotkey is independent: registering the whole set clears and
        // reports only the specific entries that actually conflict, leaving
        // everything else exactly as configured.
        private List<string> ApplyAllHotkeysIndividually()
        {
            UnregisterAllHotkeys();
            List<string> failed = new List<string>();
            for (int i = 0; i < HotkeyCatalog.All.Length; i++)
            {
                string id = HotkeyCatalog.All[i].Id;
                HotkeyBinding b = _config.GetHotkey(id);
                if (!b.IsAssigned) continue;
                bool ok = Native.RegisterHotKey(Handle, i + 1, b.Modifiers(), b.VirtualKey);
                if (!ok)
                {
                    failed.Add(id);
                    b.Clear();
                }
            }
            return failed;
        }

        private void UnregisterAllHotkeys()
        {
            for (int i = 1; i <= HotkeyCatalog.All.Length; i++) Native.UnregisterHotKey(Handle, i);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int idx = m.WParam.ToInt32() - 1;
                if (idx >= 0 && idx < HotkeyCatalog.All.Length)
                {
                    InvokeHotkeyAction(HotkeyCatalog.All[idx].Id);
                }
                return;
            }
            else if (m.Msg == (int)Native.WM_SHOW_SETTINGS)
            {
                OpenAndFocus();
                return;
            }
            base.WndProc(ref m);
        }

        private void InvokeHotkeyAction(string id)
        {
            switch (id)
            {
                case HotkeyCatalog.ToggleVisibility: ToggleAllVisibility(); break;
                case HotkeyCatalog.OpenSettings: OpenAndFocus(); break;
                case HotkeyCatalog.CloseApp: QuitApplication(); break;
                case HotkeyCatalog.ToggleAutostart: ToggleAutostartHotkeyAction(); break;
                case HotkeyCatalog.SelectNext: SelectRelative(1); break;
                case HotkeyCatalog.SelectPrev: SelectRelative(-1); break;
                case HotkeyCatalog.DeselectAll: SetSelected(null); break;
                case HotkeyCatalog.CycleStyleSelected: CycleStyleOfSelected(1); break;
                case HotkeyCatalog.CycleStyleSelectedBack: CycleStyleOfSelected(-1); break;
                case HotkeyCatalog.CycleSourceSelected: CycleSourceOfSelected(); break;
                case HotkeyCatalog.ToggleVisibilitySelected: ToggleVisibilityOfSelected(); break;
                case HotkeyCatalog.ToggleCaseSelectedText: TextTransformService.ApplyCaseToggleToSelection(); break;
                case HotkeyCatalog.FixLayoutSelectedText:
                    TextTransformService.ApplyLayoutFixToSelection(_config.LayoutFixUniversal);
                    break;
                case HotkeyCatalog.ToggleLayoutFixMode: ToggleLayoutFixMode(); break;
                case HotkeyCatalog.ToggleCapsLockLock: ToggleCapsLockLockAction(); break;
                case HotkeyCatalog.ToggleLayoutSwitchLock: ToggleLayoutSwitchLockAction(); break;
                case HotkeyCatalog.ToggleMute: ToggleMuteAction(); break;
            }
        }

        private void ToggleCapsLockLockAction()
        {
            _config.CapsLockLockEnabled = !_config.CapsLockLockEnabled;
            KeyboardLockService.CapsLockLocked = _config.CapsLockLockEnabled;
            if (_cbCapsLockLock != null) _cbCapsLockLock.Checked = _config.CapsLockLockEnabled;
            SaveConfig();
        }

        private void ToggleLayoutSwitchLockAction()
        {
            _config.LayoutSwitchLockEnabled = !_config.LayoutSwitchLockEnabled;
            KeyboardLockService.LayoutSwitchLocked = _config.LayoutSwitchLockEnabled;
            if (_cbLayoutSwitchLock != null) _cbLayoutSwitchLock.Checked = _config.LayoutSwitchLockEnabled;
            SaveConfig();
        }

        private void ToggleMuteAction()
        {
            _config.SoundMuted = !_config.SoundMuted;
            SoundService.Muted = _config.SoundMuted;
            if (_cbMuteAll != null) _cbMuteAll.Checked = _config.SoundMuted;
            SaveConfig();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyDarkTitleBar(Handle);
        }

        private void QuitApplication()
        {
            _reallyClosing = true;
            Close();
            Application.Exit();
        }

        // Reopening after Hide()/minimize sometimes left the window showing
        // only a taskbar entry with nothing paintable on screen — either
        // WinForms' WindowState/Visible bookkeeping got out of sync with the
        // real native window state (setting WindowState before the handle
        // has ever been shown doesn't always "stick"), or the saved position
        // had drifted outside every current monitor (e.g. after a display/
        // DPI change). Driving the native window directly with ShowWindow,
        // and clamping the position back on screen, fixes both regardless of
        // which one actually happened.
        private void OpenAndFocus()
        {
            IntPtr h = Handle; // force handle creation before any native calls
            Native.ShowWindow(h, Native.SW_RESTORE);
            WindowState = FormWindowState.Normal;
            EnsureOnScreen();
            Show();
            Native.ShowWindow(h, Native.SW_SHOW);
            BringInFrontOfTopmostIndicators();
            Activate();
        }

        private void EnsureOnScreen()
        {
            Rectangle b = Bounds;
            foreach (Screen s in Screen.AllScreens)
            {
                if (s.WorkingArea.IntersectsWith(b)) return;
            }
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                wa.Left + Math.Max(0, (wa.Width - Width) / 2),
                wa.Top + Math.Max(0, (wa.Height - Height) / 2));
        }

        // Indicator windows are topmost by default, and Windows always keeps
        // topmost windows above regular ones regardless of activation — a
        // plain Show()/Activate() on Settings can end up hidden behind them.
        // Briefly toggling TopMost is the standard trick to force a normal
        // window in front of an existing topmost one.
        private void BringInFrontOfTopmostIndicators()
        {
            TopMost = true;
            TopMost = false;
            BringToFront();
        }

        private void ToggleAllVisibility()
        {
            _allVisible = !_allVisible;
            foreach (IndicatorForm f in _indicators) f.Visible = _allVisible;
        }

        private void ToggleAutostartHotkeyAction()
        {
            bool newState = !AutostartHelper.IsEnabled();
            if (AutostartHelper.SetEnabled(newState))
            {
                _config.Autostart = newState;
                _cbAutostart.Checked = newState;
                SaveConfig();
            }
        }

        private void ToggleLayoutFixMode()
        {
            _config.LayoutFixUniversal = !_config.LayoutFixUniversal;
            if (_layoutModeBox != null) _layoutModeBox.SelectedIndex = _config.LayoutFixUniversal ? 0 : 1;
            SaveConfig();
        }

        #region Selection

        private void SetSelected(IndicatorForm f)
        {
            if (_selectedIndicator == f) return;
            if (_selectedIndicator != null) _selectedIndicator.SetSelected(false);
            _selectedIndicator = f;
            if (_selectedIndicator != null) _selectedIndicator.SetSelected(true);
            RefreshStyleGallerySelection();
        }

        // While a window is selected, the gallery reflects (and edits) that
        // window's own style instead of the "default for new windows" pick.
        private void RefreshStyleGallerySelection()
        {
            Style target = _selectedIndicator != null ? _selectedIndicator.CurrentStyle : _defaultStyleForNewWindows;
            foreach (StyleCard c in _cards) c.Selected = (c.Style == target);
        }

        // Feeds MouseSelectionWatcher: a click anywhere outside every visible
        // indicator window and outside the Settings window itself (or any of
        // its popups, e.g. an open ComboBox dropdown) clears the current
        // selection, the same way clicking empty desktop space deselects an
        // icon. Identified by owning process rather than Form.Bounds, since
        // Bounds does not reliably reflect the on-screen rectangle while the
        // Settings window is maximized, which used to cause clicks inside it
        // to be treated as "outside" and wrongly clear the selection.
        private bool PointIsProtected(Point p)
        {
            IntPtr hwnd = Native.WindowFromPoint(new POINT(p.X, p.Y));
            if (hwnd == IntPtr.Zero) return false;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            return pid == _currentProcessId;
        }

        private void DeselectViaOutsideClick()
        {
            SetSelected(null);
        }

        private void SelectRelative(int direction)
        {
            if (_indicators.Count == 0) return;
            int idx = _selectedIndicator == null ? -1 : _indicators.IndexOf(_selectedIndicator);
            idx = ((idx + direction) % _indicators.Count + _indicators.Count) % _indicators.Count;
            SetSelected(_indicators[idx]);
        }

        private void CycleStyleOfSelected(int direction)
        {
            if (_selectedIndicator == null) return;
            const int count = 30;
            int next = (((int)_selectedIndicator.CurrentStyle + direction) % count + count) % count;
            _selectedIndicator.ApplyStyle((Style)next);
            RefreshRowFor(_selectedIndicator);
        }

        private void CycleSourceOfSelected()
        {
            if (_selectedIndicator == null) return;
            int count = SourceCatalog.All.Length;
            int idx = Array.IndexOf(SourceCatalog.All, _selectedIndicator.Source);
            idx = (idx + 1) % count;
            _selectedIndicator.ApplySource(SourceCatalog.All[idx]);
            RefreshRowFor(_selectedIndicator);
        }

        private void ToggleVisibilityOfSelected()
        {
            if (_selectedIndicator == null) return;
            _selectedIndicator.Visible = !_selectedIndicator.Visible;
            RefreshRowFor(_selectedIndicator);
        }

        private void RefreshRowFor(IndicatorForm f)
        {
            foreach (WindowRow row in _rows)
            {
                if (row.Indicator == f) { row.RefreshFromIndicator(); break; }
            }
            if (f == _selectedIndicator)
            {
                RefreshStyleGallerySelection();
                foreach (StyleCard c in _cards) c.Invalidate();
            }
        }

        #endregion

        private void SettingsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_capturingId == null) return;
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyCode == Keys.Escape)
            {
                _capturingRow.SetCapturing(false);
                _capturingId = null;
                _capturingRow = null;
                return;
            }
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey
                || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                return;
            }

            HotkeyBinding b = new HotkeyBinding();
            b.Control = e.Control; b.Shift = e.Shift; b.Alt = e.Alt; b.VirtualKey = (uint)e.KeyCode;

            string id = _capturingId;
            _capturingRow.SetCapturing(false);
            _capturingId = null;
            _capturingRow = null;
            SetHotkey(id, b);
        }

        private void StartCapture(string id, HotkeyRow row)
        {
            if (_capturingRow != null) _capturingRow.SetCapturing(false);
            _capturingId = id;
            _capturingRow = row;
            row.SetCapturing(true);
        }

        private void ClearHotkey(string id)
        {
            SetHotkey(id, new HotkeyBinding());
        }

        private void SetHotkey(string id, HotkeyBinding b)
        {
            _config.Hotkeys[id] = b;
            List<string> failed = ApplyAllHotkeysIndividually();
            SaveConfig();
            RefreshAllHotkeyRows();

            if (failed.Contains(id))
            {
                MessageBox.Show(this,
                    "Комбинация «" + b.DisplayText() + "» уже занята другой программой. "
                    + "Хоткей «" + NameFor(id) + "» сброшен — задайте другую комбинацию.",
                    "CapsIndicator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshAllHotkeyRows()
        {
            foreach (HotkeyRow row in _hotkeyRows.Values) row.Refresh();
        }

        #endregion

        #region UI shell (custom tab strip — no native TabControl chrome)

        private void BuildUi()
        {
            // WinForms docks controls in reverse of the order they're added
            // (the LAST control added claims its edge/fill FIRST) — so the
            // Fill content pane must be added before the Top strip, or the
            // strip ends up overlapping the content instead of pushing it
            // down, which is exactly what was clipping the top of every tab.
            _tabContent = new Panel();
            _tabContent.Dock = DockStyle.Fill;
            _tabContent.BackColor = Theme.Background;
            Controls.Add(_tabContent);

            _tabStrip = new Panel();
            _tabStrip.Dock = DockStyle.Top;
            _tabStrip.Height = 40;
            _tabStrip.BackColor = Theme.Background;
            Controls.Add(_tabStrip);

            string[] names = new string[] { "Стили", "Хоткеи", "Окна", "Параметры", "Помощь" };
            _tabHeaderButtons = new TabHeaderButton[names.Length];
            int tabWidth = 130;
            int x = 0;
            for (int i = 0; i < names.Length; i++)
            {
                TabHeaderButton btn = new TabHeaderButton();
                btn.Text = names[i];
                btn.Left = x; btn.Top = 0; btn.Width = tabWidth; btn.Height = 40;
                int idx = i;
                btn.Click += delegate { SelectTab(idx); };
                _tabStrip.Controls.Add(btn);
                _tabHeaderButtons[i] = btn;
                x += tabWidth;
            }

            _tabStyles = MakeTabPage();
            _tabHotkeys = MakeTabPage();
            _tabWindows = MakeTabPage();
            _tabTech = MakeTabPage();
            _tabHelp = MakeTabPage();

            _tabContent.Controls.Add(_tabStyles);
            _tabContent.Controls.Add(_tabHotkeys);
            _tabContent.Controls.Add(_tabWindows);
            _tabContent.Controls.Add(_tabTech);
            _tabContent.Controls.Add(_tabHelp);

            BuildStylesTab();
            BuildHotkeysTab();
            BuildWindowsTab();
            BuildTechTab();
            BuildHelpTab();

            SelectTab(0);
        }

        private static Panel MakeTabPage()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            p.BackColor = Theme.Background;
            p.ForeColor = Theme.TextPrimary;
            p.Padding = new Padding(14);
            return p;
        }

        private void SelectTab(int idx)
        {
            _selectedTabIndex = idx;
            _tabStyles.Visible = idx == 0;
            _tabHotkeys.Visible = idx == 1;
            _tabWindows.Visible = idx == 2;
            _tabTech.Visible = idx == 3;
            _tabHelp.Visible = idx == 4;
            for (int i = 0; i < _tabHeaderButtons.Length; i++) _tabHeaderButtons[i].Selected = (i == idx);
        }

        #endregion

        #region Styles tab

        private void BuildStylesTab()
        {
            // Fill must be added before Top — see the note in BuildUi().
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.AutoScroll = true;
            flow.WrapContents = true;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.BackColor = Theme.Background;
            _tabStyles.Controls.Add(flow);
            Theme.ApplyDarkScrollbar(flow);

            Label hint = new Label();
            hint.Text = "Галерея стилей индикатора. Если выбрано окно — карточка меняет его стиль, иначе — стиль по умолчанию для новых окон.";
            hint.ForeColor = Theme.TextSecondary;
            hint.Font = Theme.FontSmall;
            hint.Dock = DockStyle.Top;
            hint.Height = 26;
            _tabStyles.Controls.Add(hint);

            for (int i = 0; i < 30; i++)
            {
                Style s = (Style)i;
                StyleCard card = new StyleCard(s);
                card.Selected = (s == _defaultStyleForNewWindows);
                card.CardClick += StyleCard_Click;
                _cards.Add(card);
                flow.Controls.Add(card);
            }

            _galleryTimer = new Timer();
            _galleryTimer.Interval = 150;
            _galleryTimer.Tick += delegate
            {
                foreach (StyleCard c in _cards) c.Invalidate();
            };
            _galleryTimer.Start();
        }

        private void StyleCard_Click(StyleCard card)
        {
            if (_selectedIndicator != null)
            {
                _selectedIndicator.ApplyStyle(card.Style);
                RefreshRowFor(_selectedIndicator);
            }
            else
            {
                _defaultStyleForNewWindows = card.Style;
            }
            RefreshStyleGallerySelection();
            foreach (StyleCard c in _cards) c.Invalidate();
        }

        private sealed class StyleCard : Panel
        {
            public readonly Style Style;
            public bool Selected;
            public event Action<StyleCard> CardClick;

            public StyleCard(Style s)
            {
                Style = s;
                Size = new Size(150, 118);
                Margin = new Padding(6);
                BackColor = Theme.Panel;
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
                Click += delegate { RaiseClick(); };
            }

            private void RaiseClick()
            {
                if (CardClick != null) CardClick(this);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (SolidBrush bg = new SolidBrush(Selected ? Theme.PanelLight : Theme.Panel))
                {
                    g.FillRectangle(bg, ClientRectangle);
                }
                if (Selected)
                {
                    using (Pen p = new Pen(Theme.Accent, 1.5f))
                    {
                        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                    }
                }

                Rectangle previewRect = new Rectangle(8, 8, Width - 16, 72);
                IndicatorForm.RenderPreview(g, Style, previewRect, true, Environment.TickCount / 1000f);

                StringFormat fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                using (SolidBrush fg = new SolidBrush(Theme.TextPrimary))
                {
                    g.DrawString(StyleCatalog.Name(Style), Theme.FontSmall, fg,
                        new RectangleF(4, 84, Width - 8, 30), fmt);
                }
                fmt.Dispose();
            }
        }

        #endregion

        #region Hotkeys tab

        private void BuildHotkeysTab()
        {
            // Fill must be added before Top — see the note in BuildUi().
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.AutoScroll = true;
            flow.WrapContents = false;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.BackColor = Theme.Background;
            Theme.ApplyDarkScrollbar(flow);
            _tabHotkeys.Controls.Add(flow);
            _hotkeysFlow = flow;
            flow.SizeChanged += delegate { ResizeHotkeyRows(); };

            Label hint = new Label();
            hint.Text = "Каждый хоткей необязателен — можно оставить любой (или все) не заданным. "
                + "Клик по полю — задать комбинацию, Esc — отмена.";
            hint.ForeColor = Theme.TextSecondary;
            hint.Font = Theme.FontSmall;
            hint.Dock = DockStyle.Top;
            hint.Height = 34;
            _tabHotkeys.Controls.Add(hint);

            foreach (HotkeyCatalog.Entry entry in HotkeyCatalog.All)
            {
                HotkeyRow row = new HotkeyRow(this, entry.Id, entry.Name);
                _hotkeyRows[entry.Id] = row;
                flow.Controls.Add(row.Root);
            }
            ResizeHotkeyRows();
        }

        private sealed class HotkeyRow
        {
            public readonly Panel Root;
            private readonly string _id;
            private readonly SettingsForm _owner;
            private readonly FlatButton _bindButton;

            public HotkeyRow(SettingsForm owner, string id, string name)
            {
                _owner = owner;
                _id = id;

                Root = new Panel();
                Root.Width = 700;
                Root.Height = 58;
                Root.Margin = new Padding(0, 0, 0, 6);

                // The name sits on its own full-width line above the
                // buttons instead of sharing the row with them — squeezed
                // into a fixed 290px next to the bind button, the longer
                // action names (e.g. "Переключить режим исправления
                // раскладки") always got ellipsised, no matter the window
                // width, since that 290px never scaled with anything.
                Label lbl = new Label();
                lbl.Text = name;
                lbl.ForeColor = Theme.TextPrimary;
                lbl.Font = Theme.FontRegular;
                lbl.AutoSize = false;
                lbl.Left = 0; lbl.Top = 0; lbl.Width = Root.Width; lbl.Height = 20;
                lbl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                lbl.AutoEllipsis = true;

                _bindButton = new FlatButton();
                _bindButton.Left = 0; _bindButton.Top = 22; _bindButton.Width = Root.Width - 96; _bindButton.Height = 32;
                _bindButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                _bindButton.Click += delegate { _owner.StartCapture(_id, this); };

                FlatButton clearBtn = new FlatButton();
                clearBtn.Text = "Сброс";
                clearBtn.Left = Root.Width - 90; clearBtn.Top = 22; clearBtn.Width = 90; clearBtn.Height = 32;
                clearBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                clearBtn.Click += delegate { _owner.ClearHotkey(_id); };

                Root.Controls.Add(lbl);
                Root.Controls.Add(_bindButton);
                Root.Controls.Add(clearBtn);

                Refresh();
            }

            public void Refresh()
            {
                _bindButton.Text = _owner._config.GetHotkey(_id).DisplayText();
            }

            public void SetCapturing(bool capturing)
            {
                _bindButton.Text = capturing ? "Нажмите клавиши… (Esc)" : _owner._config.GetHotkey(_id).DisplayText();
            }
        }

        #endregion

        #region Windows tab

        private void BuildWindowsTab()
        {
            // Fill must be added before Top — see the note in BuildUi().
            _windowsFlow = new FlowLayoutPanel();
            _windowsFlow.Dock = DockStyle.Fill;
            _windowsFlow.AutoScroll = true;
            _windowsFlow.FlowDirection = FlowDirection.TopDown;
            _windowsFlow.WrapContents = false;
            _tabWindows.Controls.Add(_windowsFlow);
            _windowsFlow.SizeChanged += delegate { ResizeWindowRows(); };
            Theme.ApplyDarkScrollbar(_windowsFlow);

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 40;

            _addWindowButton = new FlatButton();
            _addWindowButton.Text = "+ Добавить окно индикатора";
            // A fixed 220px was narrower than this text renders at in this
            // font/DPI, silently clipping its tail ("+ Добавить окно
            // индикат…") — measure it instead of guessing.
            Size addBtnSize = TextRenderer.MeasureText(_addWindowButton.Text, Theme.FontRegular);
            _addWindowButton.Width = addBtnSize.Width + 32;
            _addWindowButton.Height = 32;
            _addWindowButton.Top = 4;
            _addWindowButton.Click += delegate { AddNewWindow(); };
            top.Controls.Add(_addWindowButton);

            _tabWindows.Controls.Add(top);
        }

        // FlowLayoutPanel doesn't stretch its children to its own width, so
        // each row's width is tracked manually whenever the panel resizes —
        // this is what makes the Windows/Hotkeys tabs actually reflow with
        // the Settings window instead of staying a fixed width forever.
        private void ResizeWindowRows()
        {
            // The row's fixed-left-hand content (source/style pickers,
            // X/Y, badges, sound row) genuinely needs its full ~900px design
            // width to lay out without overlap — shrinking the row below
            // that on a narrower Settings window pushed the right-hand
            // controls (the lock badges especially) out past the row's own
            // shrunk bounds, clipping them entirely rather than reflowing.
            // A horizontal scrollbar on a narrow window beats that.
            int w = Math.Max(900, _windowsFlow.ClientSize.Width - 28);
            foreach (WindowRow row in _rows) row.Root.Width = w;
        }

        private void ResizeHotkeyRows()
        {
            if (_hotkeysFlow == null) return;
            int w = Math.Max(360, _hotkeysFlow.ClientSize.Width - 28);
            foreach (HotkeyRow row in _hotkeyRows.Values) row.Root.Width = w;
        }

        private void AddNewWindow()
        {
            WindowConfig wc = new WindowConfig();
            wc.Source = IndicatorSource.CapsLock;
            wc.Style = _defaultStyleForNewWindows;
            wc.X = -1; wc.Y = -1; wc.W = 190; wc.H = 74;
            CreateIndicator(wc);
            SyncConfigFromIndicators();
            SaveConfig();
        }

        private IndicatorForm CreateIndicator(WindowConfig wc)
        {
            IndicatorForm f = new IndicatorForm(wc, _config.IndicatorsTopmost, _config.ShowDragFrame, _config.TargetFps);
            f.Changed += delegate { OnIndicatorChanged(f); };
            f.ClickedForSelection += delegate { SetSelected(f); };
            f.Show();
            f.Visible = _allVisible;
            _indicators.Add(f);
            AddWindowRow(f);
            return f;
        }

        private void OnIndicatorChanged(IndicatorForm f)
        {
            RefreshRowFor(f);
            SyncConfigFromIndicators();
            SaveConfig();
        }

        private void SyncConfigFromIndicators()
        {
            _config.Windows.Clear();
            foreach (IndicatorForm f in _indicators)
            {
                WindowConfig wc = f.ToConfig();
                if (!_config.SaveWindowPositions) { wc.X = -1; wc.Y = -1; }
                if (!_config.SaveSizes) { wc.W = 190; wc.H = 74; }
                if (!_config.SaveStyles) { wc.Style = Style.Full; }
                _config.Windows.Add(wc);
            }
        }

        private void RestoreWindows()
        {
            foreach (WindowConfig wc in new List<WindowConfig>(_config.Windows))
            {
                CreateIndicator(wc);
            }
            // Move/Resize don't reliably fire once for a window created and
            // positioned before its handle exists, so the Changed event alone
            // can't be trusted to persist the just-restored windows — sync
            // explicitly instead of hoping for an event.
            SyncConfigFromIndicators();
            SaveConfig();
        }

        private void AddWindowRow(IndicatorForm f)
        {
            WindowRow row = new WindowRow(this, f);
            _rows.Add(row);
            _windowsFlow.Controls.Add(row.Root);
            ResizeWindowRows();
        }

        private void RemoveWindowRow(WindowRow row)
        {
            if (_selectedIndicator == row.Indicator) SetSelected(null);
            _indicators.Remove(row.Indicator);
            row.Indicator.Close();
            row.Indicator.Dispose();
            _windowsFlow.Controls.Remove(row.Root);
            _rows.Remove(row);
            SyncConfigFromIndicators();
            SaveConfig();
        }

        private sealed class WindowRow
        {
            public readonly Panel Root;
            public readonly IndicatorForm Indicator;
            private readonly SettingsForm _owner;
            private readonly ComboBox _sourceBox;
            private readonly ComboBox _styleBox;
            private readonly DarkSpinner _xBox;
            private readonly DarkSpinner _yBox;
            private readonly DarkSpinner _widthBox;
            private readonly DarkSpinner _heightBox;
            private readonly FlatButton _visButton;
            private readonly DarkCheckBox _capsBadgeCb;
            private readonly DarkCheckBox _layoutBadgeCb;
            private readonly ComboBox _soundKindBox;
            private readonly ComboBox _soundBuiltinBox;
            private readonly FlatButton _soundFileBtn;
            private readonly Label _soundFileLabel;
            private readonly FlatButton _soundTestBtn;
            private bool _suppress;

            public WindowRow(SettingsForm owner, IndicatorForm indicator)
            {
                _owner = owner;
                Indicator = indicator;

                Root = new Panel();
                Root.Width = 900;
                Root.Height = 122;
                Root.Margin = new Padding(0, 0, 0, 8);
                Root.BackColor = Theme.Panel;

                _sourceBox = new ComboBox();
                _sourceBox.DropDownStyle = ComboBoxStyle.DropDownList;
                _sourceBox.Left = 10; _sourceBox.Top = 8; _sourceBox.Width = 180;
                Theme.StyleCombo(_sourceBox);
                foreach (IndicatorSource s in SourceCatalog.All) _sourceBox.Items.Add(SourceCatalog.Name(s));
                _sourceBox.SelectedIndex = (int)indicator.Source;
                _sourceBox.SelectedIndexChanged += delegate { OnSourceChanged(); };

                _styleBox = new ComboBox();
                _styleBox.DropDownStyle = ComboBoxStyle.DropDownList;
                _styleBox.Left = 200; _styleBox.Top = 8; _styleBox.Width = 190;
                Theme.StyleCombo(_styleBox);
                PopulateStyleBox(indicator.Source, indicator.CurrentStyle);
                _styleBox.SelectedIndexChanged += delegate { OnStyleChanged(); };

                Label xLbl = MakeLabel("X:", 400, 14);
                _xBox = MakeSpinner(422, 8, 66, -3000, 5000, indicator.Location.X, 10);
                _xBox.ValueChanged += delegate { OnPositionChanged(); };

                Label yLbl = MakeLabel("Y:", 498, 14);
                _yBox = MakeSpinner(520, 8, 66, -3000, 5000, indicator.Location.Y, 10);
                _yBox.ValueChanged += delegate { OnPositionChanged(); };

                // Same row as the style picker, on purpose — each of these
                // toggles a small always-on-top badge above THIS window that
                // shows whether the corresponding lock (Caps Lock / layout
                // switch) is currently engaged. The badge look is fixed and
                // does not depend on the indicator's own Style.
                // Widths measured from the actual text instead of guessed —
                // guessed widths clipped "Раскладка" to "Раскла" at this
                // font/DPI, same as everywhere else fixed pixel widths were
                // narrower than the real rendered text.
                _capsBadgeCb = new DarkCheckBox();
                _capsBadgeCb.Text = "Caps";
                _capsBadgeCb.AutoSize = false;
                _capsBadgeCb.Left = 600; _capsBadgeCb.Top = 10; _capsBadgeCb.Width = CheckBoxWidthFor("Caps"); _capsBadgeCb.Height = 22;
                _capsBadgeCb.Checked = indicator.ShowCapsLockLockBadge;
                _owner._tipWindows.SetToolTip(_capsBadgeCb,
                    "Показывать над этим окном мини-индикатор блокировки Caps Lock");
                _capsBadgeCb.CheckedChanged += delegate { OnCapsBadgeChanged(); };

                _layoutBadgeCb = new DarkCheckBox();
                _layoutBadgeCb.Text = "Раскладка";
                _layoutBadgeCb.AutoSize = false;
                _layoutBadgeCb.Left = _capsBadgeCb.Right + 6; _layoutBadgeCb.Top = 10;
                _layoutBadgeCb.Width = CheckBoxWidthFor("Раскладка"); _layoutBadgeCb.Height = 22;
                _layoutBadgeCb.Checked = indicator.ShowLayoutSwitchLockBadge;
                _owner._tipWindows.SetToolTip(_layoutBadgeCb,
                    "Показывать над этим окном мини-индикатор блокировки смены раскладки");
                _layoutBadgeCb.CheckedChanged += delegate { OnLayoutBadgeChanged(); };

                Label wLbl = MakeLabel("Ш:", 10, 47);
                _widthBox = MakeSpinner(32, 41, 66, 40, 1000, indicator.Width, 5);
                _widthBox.ValueChanged += delegate { OnSizeChanged(); };

                Label hLbl = MakeLabel("В:", 108, 47);
                _heightBox = MakeSpinner(130, 41, 66, 24, 600, indicator.Height, 5);
                _heightBox.ValueChanged += delegate { OnSizeChanged(); };

                // A third row, dedicated to the per-window toggle sound: none,
                // one of the built-in system sounds, or an arbitrary audio
                // file the user picks. Independent of Style and of every
                // other window's own choice.
                // Chained off each other's measured Right edge instead of
                // hardcoded lefts — "Звук:" at a fixed 42px box clipped to
                // "Звв" at this font/DPI, which then threw off every control
                // positioned after it.
                Label soundLbl = new Label();
                soundLbl.Text = "Звук:";
                soundLbl.ForeColor = Theme.TextSecondary;
                soundLbl.Font = Theme.FontRegular;
                soundLbl.AutoSize = true;
                soundLbl.Left = 10; soundLbl.Top = 90;

                _soundKindBox = new ComboBox();
                _soundKindBox.DropDownStyle = ComboBoxStyle.DropDownList;
                _soundKindBox.Left = soundLbl.Right + 8; _soundKindBox.Top = 86; _soundKindBox.Width = 110;
                Theme.StyleCombo(_soundKindBox);
                _soundKindBox.Items.Add("Нет");
                _soundKindBox.Items.Add("Встроенный");
                _soundKindBox.Items.Add("Файл");
                _soundKindBox.SelectedIndex = Math.Max(0, Math.Min(2, indicator.SoundKind));
                _soundKindBox.SelectedIndexChanged += delegate { OnSoundKindChanged(); };

                int soundDetailLeft = _soundKindBox.Right + 8;

                _soundBuiltinBox = new ComboBox();
                _soundBuiltinBox.DropDownStyle = ComboBoxStyle.DropDownList;
                _soundBuiltinBox.Left = soundDetailLeft; _soundBuiltinBox.Top = 86; _soundBuiltinBox.Width = 190;
                Theme.StyleCombo(_soundBuiltinBox);
                foreach (SoundService.BuiltinSound b in SoundService.Builtins) _soundBuiltinBox.Items.Add(b.Name);
                SelectBuiltin(indicator.SoundBuiltinId);
                _soundBuiltinBox.SelectedIndexChanged += delegate { OnSoundBuiltinChanged(); };

                _soundFileBtn = new FlatButton();
                _soundFileBtn.Text = "Выбрать файл…";
                _soundFileBtn.Left = soundDetailLeft; _soundFileBtn.Top = 86; _soundFileBtn.Width = ButtonWidthFor(_soundFileBtn.Text); _soundFileBtn.Height = 26;
                _soundFileBtn.Click += delegate { PickSoundFile(); };

                _soundFileLabel = new Label();
                _soundFileLabel.Text = FileLabelText(indicator.SoundFilePath);
                _soundFileLabel.ForeColor = Theme.TextSecondary;
                _soundFileLabel.Font = Theme.FontSmall;
                _soundFileLabel.AutoEllipsis = true;
                _soundFileLabel.Left = _soundFileBtn.Right + 10; _soundFileLabel.Top = 91;
                _soundFileLabel.Width = Math.Max(80, Root.Width - _soundFileLabel.Left - 110);
                _soundFileLabel.Height = 18;
                _soundFileLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                _owner._tipWindows.SetToolTip(_soundFileLabel, indicator.SoundFilePath);

                _soundTestBtn = new FlatButton();
                _soundTestBtn.Text = "▶ Тест";
                int soundTestWidth = ButtonWidthFor(_soundTestBtn.Text);
                _soundTestBtn.Left = Root.Width - soundTestWidth - 10; _soundTestBtn.Top = 86; _soundTestBtn.Width = soundTestWidth; _soundTestBtn.Height = 26;
                _soundTestBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                _soundTestBtn.Click += delegate { TestSound(); };

                UpdateSoundControlsVisibility();

                // "Показать" is the longer of the two toggle labels this
                // button ever shows, so size for that one up front — sizing
                // only for whichever label happens to be current at
                // construction time (as before) meant the button could be
                // too narrow the moment it flipped to the longer word.
                _visButton = new FlatButton();
                _visButton.Text = Indicator.Visible ? "Скрыть" : "Показать";
                _visButton.Left = 210; _visButton.Top = 40; _visButton.Width = ButtonWidthFor("Показать"); _visButton.Height = 30;
                _visButton.Click += delegate { ToggleVisible(); };

                FlatButton selectBtn = new FlatButton();
                selectBtn.Text = "Выбрать";
                selectBtn.Left = _visButton.Right + 8; selectBtn.Top = 40; selectBtn.Width = ButtonWidthFor("Выбрать"); selectBtn.Height = 30;
                selectBtn.Click += delegate { _owner.SetSelected(Indicator); };

                FlatButton removeBtn = new FlatButton();
                removeBtn.Text = "Удалить";
                int removeBtnWidth = ButtonWidthFor("Удалить");
                removeBtn.Left = Root.Width - removeBtnWidth - 10; removeBtn.Top = 40; removeBtn.Width = removeBtnWidth; removeBtn.Height = 30;
                removeBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                removeBtn.ForeColor = Theme.Danger;
                removeBtn.Click += delegate { _owner.RemoveWindowRow(this); };

                Root.Controls.Add(_sourceBox);
                Root.Controls.Add(_styleBox);
                Root.Controls.Add(xLbl);
                Root.Controls.Add(_xBox);
                Root.Controls.Add(yLbl);
                Root.Controls.Add(_yBox);
                Root.Controls.Add(_capsBadgeCb);
                Root.Controls.Add(_layoutBadgeCb);
                Root.Controls.Add(wLbl);
                Root.Controls.Add(_widthBox);
                Root.Controls.Add(hLbl);
                Root.Controls.Add(_heightBox);
                Root.Controls.Add(soundLbl);
                Root.Controls.Add(_soundKindBox);
                Root.Controls.Add(_soundBuiltinBox);
                Root.Controls.Add(_soundFileBtn);
                Root.Controls.Add(_soundFileLabel);
                Root.Controls.Add(_soundTestBtn);
                Root.Controls.Add(_visButton);
                Root.Controls.Add(selectBtn);
                Root.Controls.Add(removeBtn);
            }

            private static Label MakeLabel(string text, int left, int top)
            {
                Label l = new Label();
                l.Text = text; l.Left = left; l.Top = top; l.Width = 20;
                l.ForeColor = Theme.TextSecondary;
                l.Font = Theme.FontRegular;
                return l;
            }

            // Sizes a FlatButton/DarkCheckBox from the text it will actually
            // show instead of a guessed constant, which kept turning out
            // narrower than the real rendered text and clipping the last
            // character or two ("Выбрат", "Удалит").
            private static int ButtonWidthFor(string text)
            {
                Size measured = TextRenderer.MeasureText(text, Theme.FontRegular);
                return Math.Max(70, measured.Width + 34);
            }

            private static int CheckBoxWidthFor(string text)
            {
                Size measured = TextRenderer.MeasureText(text, Theme.FontRegular);
                return Math.Max(50, measured.Width + 34);
            }

            private static DarkSpinner MakeSpinner(int left, int top, int width, int min, int max, int value, int step)
            {
                DarkSpinner n = new DarkSpinner();
                n.Left = left; n.Top = top; n.Width = width; n.Height = 24;
                n.Minimum = min; n.Maximum = max;
                n.Step = step;
                n.Value = Math.Max(min, Math.Min(max, value));
                return n;
            }

            // Sources with no natural on/off (Language shows a live code, not
            // a state word) only make sense with styles that actually render
            // text — pure shape/icon/animation styles are filtered out of the
            // picker for them.
            private void PopulateStyleBox(IndicatorSource source, Style current)
            {
                _styleBox.Items.Clear();
                bool restrictToText = source == IndicatorSource.Language;
                int selectIndex = 0;
                for (int i = 0; i < StyleCatalog.Names.Length; i++)
                {
                    if (restrictToText && !StyleCatalog.HasText((Style)i)) continue;
                    _styleBox.Items.Add(StyleCatalog.Names[i]);
                    if (i == (int)current) selectIndex = _styleBox.Items.Count - 1;
                }
                _styleBox.SelectedIndex = _styleBox.Items.Count > 0 ? selectIndex : -1;
            }

            private Style StyleAtSelection()
            {
                bool restrictToText = (IndicatorSource)_sourceBox.SelectedIndex == IndicatorSource.Language;
                int idx = -1;
                int seen = -1;
                for (int i = 0; i < StyleCatalog.Names.Length; i++)
                {
                    if (restrictToText && !StyleCatalog.HasText((Style)i)) continue;
                    seen++;
                    if (seen == _styleBox.SelectedIndex) { idx = i; break; }
                }
                return idx >= 0 ? (Style)idx : Style.Full;
            }

            private void OnSourceChanged()
            {
                if (_suppress) return;
                IndicatorSource newSource = (IndicatorSource)_sourceBox.SelectedIndex;
                Style keepStyle = StyleCatalog.HasText(Indicator.CurrentStyle) || newSource != IndicatorSource.Language
                    ? Indicator.CurrentStyle : Style.Full;
                PopulateStyleBox(newSource, keepStyle);
                Indicator.ApplySource(newSource);
                Indicator.ApplyStyle(StyleAtSelection());
            }

            private void OnStyleChanged()
            {
                if (_suppress) return;
                Indicator.ApplyStyle(StyleAtSelection());
            }

            private void OnSizeChanged()
            {
                if (_suppress) return;
                Indicator.Width = _widthBox.Value;
                Indicator.Height = _heightBox.Value;
            }

            private void OnPositionChanged()
            {
                if (_suppress) return;
                Indicator.Location = new Point(_xBox.Value, _yBox.Value);
            }

            private void OnCapsBadgeChanged()
            {
                if (_suppress) return;
                Indicator.ShowCapsLockLockBadge = _capsBadgeCb.Checked;
            }

            private void OnLayoutBadgeChanged()
            {
                if (_suppress) return;
                Indicator.ShowLayoutSwitchLockBadge = _layoutBadgeCb.Checked;
            }

            private void OnSoundKindChanged()
            {
                UpdateSoundControlsVisibility();
                if (_suppress) return;
                Indicator.SoundKind = _soundKindBox.SelectedIndex;
            }

            private void OnSoundBuiltinChanged()
            {
                if (_suppress) return;
                int idx = _soundBuiltinBox.SelectedIndex;
                if (idx < 0 || idx >= SoundService.Builtins.Length) return;
                Indicator.SoundBuiltinId = SoundService.Builtins[idx].Id;
            }

            private void PickSoundFile()
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Filter = "Аудио файлы|*.wav;*.mp3;*.wma;*.aac;*.m4a;*.ogg|Все файлы|*.*";
                    dlg.CheckFileExists = true;
                    if (!string.IsNullOrEmpty(Indicator.SoundFilePath))
                    {
                        try { dlg.InitialDirectory = System.IO.Path.GetDirectoryName(Indicator.SoundFilePath); }
                        catch { }
                    }
                    if (dlg.ShowDialog(_owner) == DialogResult.OK)
                    {
                        Indicator.SoundFilePath = dlg.FileName;
                        _soundFileLabel.Text = FileLabelText(dlg.FileName);
                        _owner._tipWindows.SetToolTip(_soundFileLabel, dlg.FileName);
                    }
                }
            }

            private void TestSound()
            {
                int kind = _soundKindBox.SelectedIndex;
                string builtinId = kind == SoundKind.Builtin ? Indicator.SoundBuiltinId : null;
                string filePath = kind == SoundKind.File ? Indicator.SoundFilePath : null;
                SoundService.PlayPreview(kind, builtinId, filePath);
            }

            private void UpdateSoundControlsVisibility()
            {
                int kind = _soundKindBox.SelectedIndex;
                _soundBuiltinBox.Visible = kind == SoundKind.Builtin;
                _soundFileBtn.Visible = kind == SoundKind.File;
                _soundFileLabel.Visible = kind == SoundKind.File;
            }

            private void SelectBuiltin(string id)
            {
                int idx = 0;
                for (int i = 0; i < SoundService.Builtins.Length; i++)
                {
                    if (SoundService.Builtins[i].Id == id) { idx = i; break; }
                }
                _soundBuiltinBox.SelectedIndex = SoundService.Builtins.Length > 0 ? idx : -1;
            }

            private static string FileLabelText(string path)
            {
                if (string.IsNullOrEmpty(path)) return "(файл не выбран)";
                try { return System.IO.Path.GetFileName(path); }
                catch { return path; }
            }

            private void ToggleVisible()
            {
                Indicator.Visible = !Indicator.Visible;
                _visButton.Text = Indicator.Visible ? "Скрыть" : "Показать";
            }

            public void RefreshFromIndicator()
            {
                _suppress = true;
                _sourceBox.SelectedIndex = (int)Indicator.Source;
                PopulateStyleBox(Indicator.Source, Indicator.CurrentStyle);
                _xBox.Value = Indicator.Location.X;
                _yBox.Value = Indicator.Location.Y;
                _widthBox.Value = Indicator.Width;
                _heightBox.Value = Indicator.Height;
                _visButton.Text = Indicator.Visible ? "Скрыть" : "Показать";
                _capsBadgeCb.Checked = Indicator.ShowCapsLockLockBadge;
                _layoutBadgeCb.Checked = Indicator.ShowLayoutSwitchLockBadge;
                _soundKindBox.SelectedIndex = Math.Max(0, Math.Min(2, Indicator.SoundKind));
                SelectBuiltin(Indicator.SoundBuiltinId);
                _soundFileLabel.Text = FileLabelText(Indicator.SoundFilePath);
                UpdateSoundControlsVisibility();
                _suppress = false;
            }
        }

        #endregion

        #region Technical tab

        private void BuildTechTab()
        {
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            Theme.ApplyDarkScrollbar(flow);

            _cbAutostart = MakeCheckBox("Запускать CapsIndicator вместе с Windows");
            _cbAutostart.CheckedChanged += delegate
            {
                if (AutostartHelper.SetEnabled(_cbAutostart.Checked))
                {
                    _config.Autostart = _cbAutostart.Checked;
                    SaveConfig();
                }
            };

            _cbAutoStartIndicators = MakeCheckBox("Открывать сохранённые окна индикаторов автоматически при запуске");
            _cbAutoStartIndicators.Checked = _config.AutoStartIndicatorsWithApp;
            _cbAutoStartIndicators.CheckedChanged += delegate
            {
                _config.AutoStartIndicatorsWithApp = _cbAutoStartIndicators.Checked;
                SaveConfig();
            };

            _cbSaveWindowPositions = MakeCheckBox("Запоминать позицию окон индикаторов");
            _cbSaveWindowPositions.Checked = _config.SaveWindowPositions;
            _cbSaveWindowPositions.CheckedChanged += delegate
            {
                _config.SaveWindowPositions = _cbSaveWindowPositions.Checked;
                SyncConfigFromIndicators();
                SaveConfig();
            };

            _cbSaveSizes = MakeCheckBox("Запоминать размер окон индикаторов");
            _cbSaveSizes.Checked = _config.SaveSizes;
            _cbSaveSizes.CheckedChanged += delegate
            {
                _config.SaveSizes = _cbSaveSizes.Checked;
                SyncConfigFromIndicators();
                SaveConfig();
            };

            _cbSaveStyles = MakeCheckBox("Запоминать выбранный стиль каждого окна");
            _cbSaveStyles.Checked = _config.SaveStyles;
            _cbSaveStyles.CheckedChanged += delegate
            {
                _config.SaveStyles = _cbSaveStyles.Checked;
                SyncConfigFromIndicators();
                SaveConfig();
            };

            _cbShowDragFrame = MakeCheckBox("Показывать рамку границ окна при перетаскивании");
            _cbShowDragFrame.Checked = _config.ShowDragFrame;
            _cbShowDragFrame.CheckedChanged += delegate
            {
                _config.ShowDragFrame = _cbShowDragFrame.Checked;
                foreach (IndicatorForm f in _indicators) f.ApplyShowDragFrame(_config.ShowDragFrame);
                SaveConfig();
            };

            _cbTopmost = MakeCheckBox("Индикаторы поверх всех окон");
            _cbTopmost.Checked = _config.IndicatorsTopmost;
            _cbTopmost.CheckedChanged += delegate
            {
                _config.IndicatorsTopmost = _cbTopmost.Checked;
                foreach (IndicatorForm f in _indicators) f.ApplyTopmost(_config.IndicatorsTopmost);
                SaveConfig();
            };

            _cbCapsLockLock = MakeCheckBox("Блокировать переключение Caps Lock (выключено по умолчанию)");
            _cbCapsLockLock.Checked = _config.CapsLockLockEnabled;
            _cbCapsLockLock.CheckedChanged += delegate
            {
                _config.CapsLockLockEnabled = _cbCapsLockLock.Checked;
                KeyboardLockService.CapsLockLocked = _config.CapsLockLockEnabled;
                SaveConfig();
            };

            _cbLayoutSwitchLock = MakeCheckBox("Блокировать смену раскладки (выключено по умолчанию)");
            _cbLayoutSwitchLock.Checked = _config.LayoutSwitchLockEnabled;
            _cbLayoutSwitchLock.CheckedChanged += delegate
            {
                _config.LayoutSwitchLockEnabled = _cbLayoutSwitchLock.Checked;
                KeyboardLockService.LayoutSwitchLocked = _config.LayoutSwitchLockEnabled;
                SaveConfig();
            };

            flow.Controls.Add(_cbAutostart);
            flow.Controls.Add(_cbAutoStartIndicators);
            flow.Controls.Add(_cbSaveWindowPositions);
            flow.Controls.Add(_cbSaveSizes);
            flow.Controls.Add(_cbSaveStyles);
            flow.Controls.Add(_cbShowDragFrame);
            flow.Controls.Add(_cbTopmost);
            flow.Controls.Add(_cbCapsLockLock);
            flow.Controls.Add(_cbLayoutSwitchLock);

            _cbMuteAll = MakeCheckBox("Без звука (заглушить звук всех индикаторов)");
            _cbMuteAll.Checked = _config.SoundMuted;
            _cbMuteAll.CheckedChanged += delegate
            {
                _config.SoundMuted = _cbMuteAll.Checked;
                SoundService.Muted = _config.SoundMuted;
                SaveConfig();
            };
            flow.Controls.Add(_cbMuteAll);

            Panel volumeRow = new Panel();
            volumeRow.Height = 34;
            volumeRow.Margin = new Padding(0, 4, 0, 8);

            Label volumeLabel = new Label();
            volumeLabel.Text = "Громкость звуков индикаторов:";
            volumeLabel.ForeColor = Theme.TextPrimary;
            volumeLabel.Font = Theme.FontRegular;
            volumeLabel.AutoSize = true;
            volumeLabel.Left = 0; volumeLabel.Top = 9;

            // Every control after the label is positioned from its actually
            // measured width (Right + gap) instead of a hardcoded guess —
            // guessed offsets kept turning out narrower than the real text
            // at this font/DPI, so the slider/value ended up drawn on top of
            // the tail of the label instead of after it.
            _volumeSlider = new TrackBar();
            _volumeSlider.Minimum = 0;
            _volumeSlider.Maximum = 100;
            _volumeSlider.TickFrequency = 10;
            _volumeSlider.Value = Math.Max(0, Math.Min(100, _config.SoundVolume));
            _volumeSlider.Left = volumeLabel.Right + 12; _volumeSlider.Top = 0; _volumeSlider.Width = 160; _volumeSlider.Height = 34;

            _volumeValueLabel = new Label();
            _volumeValueLabel.Text = _volumeSlider.Value + "%";
            _volumeValueLabel.ForeColor = Theme.TextSecondary;
            _volumeValueLabel.Font = Theme.FontRegular;
            _volumeValueLabel.AutoSize = true;
            _volumeValueLabel.Left = _volumeSlider.Right + 10; _volumeValueLabel.Top = 9;
            // Same "row Panel is a child window" reasoning as fpsRow/layoutModeRow below.
            volumeRow.Width = _volumeValueLabel.Right + 10;

            _volumeSlider.ValueChanged += delegate
            {
                _config.SoundVolume = _volumeSlider.Value;
                SoundService.Volume = _volumeSlider.Value;
                _volumeValueLabel.Text = _volumeSlider.Value + "%";
                SaveConfig();
            };

            volumeRow.Controls.Add(volumeLabel);
            volumeRow.Controls.Add(_volumeSlider);
            volumeRow.Controls.Add(_volumeValueLabel);
            flow.Controls.Add(volumeRow);

            Panel fpsRow = new Panel();
            fpsRow.Margin = new Padding(0, 8, 0, 8);

            Label fpsLabel = new Label();
            fpsLabel.Text = "Частота кадров анимации:";
            fpsLabel.ForeColor = Theme.TextPrimary;
            fpsLabel.Font = Theme.FontRegular;
            fpsLabel.AutoSize = true;
            fpsLabel.Left = 0; fpsLabel.Top = 5;

            _fpsBox = new ComboBox();
            _fpsBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _fpsBox.Top = 1; _fpsBox.Width = 90;
            // StyleCombo sets ItemHeight from the real font metrics, which
            // for an owner-drawn DropDownList combo also grows the box's
            // own closed Height beyond the default — read it back only
            // after styling, not before.
            Theme.StyleCombo(_fpsBox);
            // The row Panel is itself a child window whose own Height needs
            // to fit the combo's real (post-StyleCombo) height or it gets
            // clipped instead of just looking cramped. The combo's Left is
            // pinned to the row's right edge in ResizeTechRows below, so it
            // tracks the window edge instead of trailing the label text.
            fpsRow.Height = Math.Max(fpsLabel.Bottom, _fpsBox.Bottom) + 3;
            int[] fpsOptions = new int[] { 30, 60, 90, 120, 144 };
            int selectedIdx = 1;
            for (int i = 0; i < fpsOptions.Length; i++)
            {
                _fpsBox.Items.Add(fpsOptions[i] + " fps");
                if (fpsOptions[i] == _config.TargetFps) selectedIdx = i;
            }
            _fpsBox.SelectedIndex = selectedIdx;
            _fpsBox.SelectedIndexChanged += delegate
            {
                _config.TargetFps = fpsOptions[_fpsBox.SelectedIndex];
                foreach (IndicatorForm f in _indicators) f.ApplyFrameRate(_config.TargetFps);
                SaveConfig();
            };

            fpsRow.Controls.Add(fpsLabel);
            fpsRow.Controls.Add(_fpsBox);
            flow.Controls.Add(fpsRow);

            Panel layoutModeRow = new Panel();
            layoutModeRow.Margin = new Padding(0, 4, 0, 4);

            Label layoutModeLabel = new Label();
            layoutModeLabel.Text = "Режим исправления раскладки:";
            layoutModeLabel.ForeColor = Theme.TextPrimary;
            layoutModeLabel.Font = Theme.FontRegular;
            layoutModeLabel.AutoSize = true;
            layoutModeLabel.Left = 0; layoutModeLabel.Top = 5;

            _layoutModeBox = new ComboBox();
            _layoutModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _layoutModeBox.Top = 1; _layoutModeBox.Width = 150;
            Theme.StyleCombo(_layoutModeBox);
            // Same reasoning as fpsRow above.
            layoutModeRow.Height = Math.Max(layoutModeLabel.Bottom, _layoutModeBox.Bottom) + 3;
            _layoutModeBox.Items.Add("Универсальная");
            _layoutModeBox.Items.Add("Однотипная");
            _layoutModeBox.SelectedIndex = _config.LayoutFixUniversal ? 0 : 1;
            _layoutModeBox.SelectedIndexChanged += delegate
            {
                _config.LayoutFixUniversal = _layoutModeBox.SelectedIndex == 0;
                SaveConfig();
            };

            layoutModeRow.Controls.Add(layoutModeLabel);
            layoutModeRow.Controls.Add(_layoutModeBox);
            flow.Controls.Add(layoutModeRow);

            // Pins both combo boxes to the row's right edge instead of
            // right after the label text, and re-runs on every resize so
            // they track the Settings window's actual width.
            Action resizeTechRows = delegate
            {
                int w = Math.Max(320, flow.ClientSize.Width - 28);
                fpsRow.Width = w;
                _fpsBox.Left = w - _fpsBox.Width - 4;
                layoutModeRow.Width = w;
                _layoutModeBox.Left = w - _layoutModeBox.Width - 4;
            };
            flow.SizeChanged += delegate { resizeTechRows(); };
            resizeTechRows();

            Label layoutModeHint = new Label();
            layoutModeHint.Text = "Универсальная понимает смешанный текст, но не разбирает одиночные ?, &, "
                + "запятую и т.п. вне слов. Однотипная работает только в одну сторону за раз "
                + "(по большинству символов в выделении), зато верно меняет и такие символы.";
            layoutModeHint.ForeColor = Theme.TextSecondary;
            layoutModeHint.Font = Theme.FontSmall;
            layoutModeHint.AutoSize = false;
            layoutModeHint.Width = 460;
            // Height sized from the text's own measured (wrapped) extent
            // instead of a guessed constant — the guess was shorter than
            // three lines actually need, so the last line was silently cut
            // off with no ellipsis, and no amount of resizing the Settings
            // window could ever reveal it since this control's own size
            // never depended on the window's.
            Size measuredHint = TextRenderer.MeasureText(layoutModeHint.Text, layoutModeHint.Font,
                new Size(layoutModeHint.Width, 0), TextFormatFlags.WordBreak);
            layoutModeHint.Height = measuredHint.Height + 6;
            layoutModeHint.Margin = new Padding(0, 0, 0, 8);
            flow.Controls.Add(layoutModeHint);

            FlatButton resetBtn = new FlatButton();
            resetBtn.Text = "Сбросить все настройки к значениям по умолчанию";
            resetBtn.Width = 360;
            resetBtn.Height = 34;
            resetBtn.Margin = new Padding(0, 20, 0, 0);
            resetBtn.Click += delegate { ResetAllSettings(); };
            flow.Controls.Add(resetBtn);

            FlatButton exitBtn = new FlatButton();
            exitBtn.Text = "Выйти из приложения (закрыть все индикаторы)";
            exitBtn.Width = 360;
            exitBtn.Height = 34;
            exitBtn.Margin = new Padding(0, 8, 0, 0);
            exitBtn.ForeColor = Theme.Danger;
            exitBtn.Click += delegate { QuitApplication(); };
            flow.Controls.Add(exitBtn);

            Label info = new Label();
            info.Text = "CapsIndicator — настраиваемый оверлей-индикатор системных переключателей.";
            info.ForeColor = Theme.TextSecondary;
            info.Font = Theme.FontSmall;
            info.AutoSize = true;
            info.Margin = new Padding(0, 24, 0, 0);
            flow.Controls.Add(info);

            _tabTech.Controls.Add(flow);
        }

        private const int CheckBoxWidth = 520;

        // DarkCheckBox wraps its text (it draws via a default StringFormat,
        // which wraps by default) but a fixed one-line Height just clips
        // whatever doesn't fit — so the height has to be measured per label
        // instead of hard-coded, or a long line like "Открывать сохранённые
        // окна индикаторов автоматически при запуске" gets cut off.
        private static CheckBox MakeCheckBox(string text)
        {
            DarkCheckBox cb = new DarkCheckBox();
            cb.Text = text;
            cb.AutoSize = false;
            cb.Width = CheckBoxWidth;
            const int boxAndGap = 24;
            Size measured = TextRenderer.MeasureText(text, Theme.FontRegular,
                new Size(CheckBoxWidth - boxAndGap, 0), TextFormatFlags.WordBreak);
            cb.Height = Math.Max(24, measured.Height + 6);
            cb.Margin = new Padding(0, 8, 0, 8);
            return cb;
        }

        private void ResetAllSettings()
        {
            DialogResult r = MessageBox.Show(this,
                "Сбросить все настройки, хоткеи и закрыть все окна индикаторов?",
                "CapsIndicator", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            SetSelected(null);
            foreach (IndicatorForm f in _indicators.ToArray())
            {
                f.Close();
                f.Dispose();
            }
            _indicators.Clear();
            foreach (WindowRow row in _rows) _windowsFlow.Controls.Remove(row.Root);
            _rows.Clear();

            AutostartHelper.SetEnabled(false);

            _config = AppConfig.CreateDefault();
            SaveConfig();
            ApplyAllHotkeysIndividually();
            RefreshAllHotkeyRows();

            KeyboardLockService.CapsLockLocked = _config.CapsLockLockEnabled;
            KeyboardLockService.LayoutSwitchLocked = _config.LayoutSwitchLockEnabled;
            SoundService.Muted = _config.SoundMuted;
            SoundService.Volume = _config.SoundVolume;

            _cbAutostart.Checked = false;
            _cbAutoStartIndicators.Checked = _config.AutoStartIndicatorsWithApp;
            _cbSaveWindowPositions.Checked = _config.SaveWindowPositions;
            _cbSaveSizes.Checked = _config.SaveSizes;
            _cbSaveStyles.Checked = _config.SaveStyles;
            _cbShowDragFrame.Checked = _config.ShowDragFrame;
            _cbTopmost.Checked = _config.IndicatorsTopmost;
            _cbCapsLockLock.Checked = _config.CapsLockLockEnabled;
            _cbLayoutSwitchLock.Checked = _config.LayoutSwitchLockEnabled;
            _cbMuteAll.Checked = _config.SoundMuted;
            _volumeSlider.Value = _config.SoundVolume;
            _fpsBox.SelectedIndex = 1;
            _layoutModeBox.SelectedIndex = 0;
        }

        #endregion

        #region Help tab

        private void BuildHelpTab()
        {
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            Theme.ApplyDarkScrollbar(flow);
            _tabHelp.Controls.Add(flow);
            _helpFlow = flow;
            // A fixed 720px body width read fine at the window's own default
            // size, but resizing the Settings window narrower gave nothing
            // back — the labels never re-measured against the space they
            // actually had, so text just ran under the scrollbar/edge
            // instead of rewrapping. Re-measuring on every resize is what
            // makes shrinking/growing the window actually change anything.
            flow.SizeChanged += delegate { ResizeHelpItems(); };

            AddHelpIntro(flow, "Общее",
                "Это приложение показывает состояние системных переключателей (Caps Lock, "
                + "раскладка и т.п.) плавающими окнами-индикаторами и умеет исправлять уже "
                + "набранный текст. Все хоткеи необязательны и по умолчанию не заданы — "
                + "назначаются на вкладке «Хоткеи».");

            AddHelpItem(flow, "Показать/скрыть все индикаторы",
                "Скрывает или снова показывает все окна индикаторов разом, не закрывая их.");
            AddHelpItem(flow, "Открыть настройки",
                "Разворачивает и выводит на передний план это окно настроек.");
            AddHelpItem(flow, "Закрыть приложение",
                "Полностью завершает работу: закрывает все окна индикаторов и само приложение.");
            AddHelpItem(flow, "Вкл/выкл автозапуск",
                "Переключает автозапуск приложения вместе с Windows — то же самое, что галочка "
                + "«Запускать CapsIndicator вместе с Windows» на этой вкладке.");
            AddHelpItem(flow, "Выбрать следующий / предыдущий индикатор, Снять выделение",
                "Перебирает окна индикаторов по кругу или снимает выделение. Выделенное окно "
                + "подсвечивается и становится целью хоткеев «...выбранного» ниже, а также кликов "
                + "по галерее стилей на вкладке «Стили».");
            AddHelpItem(flow, "Следующий / предыдущий стиль выбранного",
                "Переключает оформление выделенного окна индикатора на следующий или предыдущий "
                + "стиль из галереи.");
            AddHelpItem(flow, "Сменить источник выбранного",
                "Переключает, какой системный переключатель показывает выделенное окно (Caps "
                + "Lock, раскладка и т.д.).");
            AddHelpItem(flow, "Показать/скрыть выбранный",
                "Скрывает или показывает только одно выделенное окно индикатора, остальные не "
                + "затрагивает.");

            AddHelpItem(flow, "Исправить регистр выделенного текста",
                "Работает как в Punto Switcher: копирует то, что выделено в активном приложении "
                + "(Ctrl+C), меняет регистр каждой буквы на противоположный и вставляет обратно "
                + "(Ctrl+V). Буфер обмена после этого возвращается к тому, что было в нём до "
                + "нажатия хоткея. Если в активном окне ничего не выделено — хоткей ничего не "
                + "делает.");
            AddHelpItem(flow, "Исправить раскладку выделенного текста",
                "Тем же способом (копирование → преобразование → вставка) перебивает выделенный "
                + "текст с английской раскладки на русскую или наоборот, по расположению клавиш. "
                + "Какой из двух режимов преобразования используется — задаётся ниже.");
            AddHelpItem(flow, "Переключить режим исправления раскладки",
                "Переключает режим работы предыдущего хоткея между «Универсальная» (разбирает "
                + "смешанный текст, но не трогает одиночные символы вроде ?, & вне слов) и "
                + "«Однотипная» (работает только в одну сторону за раз по большинству символов "
                + "выделения, зато верно меняет и такие символы) — то же самое, что выпадающий "
                + "список «Режим исправления раскладки» на вкладке «Параметры».");

            AddHelpItem(flow, "Вкл/выкл блокировку Caps Lock",
                "Включает или выключает блокировку физической клавиши Caps Lock — то же самое, "
                + "что галочка «Блокировать переключение Caps Lock» на вкладке «Параметры». Пока "
                + "блокировка включена, нажатие Caps Lock вообще не меняет состояние — ни "
                + "индикатор, ни реальную раскладку регистра — и остаётся так, пока блокировку "
                + "не выключить этим же хоткеем или галочкой. По умолчанию блокировка выключена.");
            AddHelpItem(flow, "Вкл/выкл блокировку смены раскладки",
                "Включает или выключает блокировку переключения раскладки клавиатуры — то же "
                + "самое, что галочка «Блокировать смену раскладки» на вкладке «Параметры». Пока "
                + "блокировка включена, стандартные сочетания переключения раскладки (Alt+Shift, "
                + "Ctrl+Shift, Win+Пробел) не срабатывают, пока блокировку не выключить этим же "
                + "хоткеем или галочкой. По умолчанию блокировка выключена.");

            AddHelpItem(flow, "Как задать хоткей",
                "На вкладке «Хоткеи» кликните по полю нужного действия и нажмите желаемую "
                + "комбинацию клавиш — Esc отменяет захват. Кнопка «Сброс» снимает назначенную "
                + "комбинацию. Если комбинация уже занята другой программой, приложение сообщит "
                + "об этом и оставит хоткей несозданным.");

            ResizeHelpItems();
        }

        private void AddHelpIntro(FlowLayoutPanel flow, string title, string body)
        {
            AddHelpItem(flow, title, body);
        }

        private const int HelpItemWidth = 720;

        private void AddHelpItem(FlowLayoutPanel flow, string title, string body)
        {
            Label header = new Label();
            header.Text = title;
            header.ForeColor = Theme.TextPrimary;
            header.Font = Theme.FontBold;
            header.AutoSize = false;
            header.Width = HelpItemWidth;
            header.Height = 22;
            header.Margin = new Padding(0, 14, 0, 2);
            flow.Controls.Add(header);
            _helpHeaderLabels.Add(header);

            Label body_ = new Label();
            body_.Text = body;
            body_.ForeColor = Theme.TextSecondary;
            body_.Font = Theme.FontRegular;
            body_.AutoSize = false;
            body_.Width = HelpItemWidth;
            Size measured = TextRenderer.MeasureText(body, Theme.FontRegular,
                new Size(HelpItemWidth, 0), TextFormatFlags.WordBreak);
            body_.Height = measured.Height + 6;
            body_.Margin = new Padding(0, 0, 0, 4);
            flow.Controls.Add(body_);
            _helpBodyLabels.Add(body_);
        }

        // Re-measures every help label against the flow panel's *current*
        // width instead of the fixed design-time HelpItemWidth, so shrinking
        // or growing the Settings window actually rewraps the text (and
        // recomputes each body label's height) instead of leaving it clipped
        // at whatever width happened to be assumed when the label was built.
        private void ResizeHelpItems()
        {
            if (_helpFlow == null) return;
            int w = Math.Max(200, _helpFlow.ClientSize.Width - 24);
            foreach (Label l in _helpHeaderLabels) l.Width = w;
            foreach (Label l in _helpBodyLabels)
            {
                l.Width = w;
                Size measured = TextRenderer.MeasureText(l.Text, l.Font, new Size(w, 0), TextFormatFlags.WordBreak);
                l.Height = measured.Height + 6;
            }
        }

        #endregion

        private void SaveConfig()
        {
            bool ok = _config.Save(AppConfig.DefaultPath());
            if (!ok)
            {
                MessageBox.Show(this, "Не удалось сохранить настройки на диск.",
                    "CapsIndicator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SettingsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Closing the Settings window (the X button, Alt+F4, etc.) must
            // NOT take the indicator windows down with it — they keep running
            // independently. Only an explicit quit (hotkey or the Exit
            // button, both of which set _reallyClosing first) actually ends
            // the process.
            if (!_reallyClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            UnregisterAllHotkeys();
            KeyboardLockService.Uninstall();
            MouseSelectionWatcher.OutsideClick -= DeselectViaOutsideClick;
            MouseSelectionWatcher.Uninstall();
            SoundService.Shutdown();
            SyncConfigFromIndicators();
            SaveConfig();
            foreach (IndicatorForm f in _indicators.ToArray())
            {
                f.Close();
            }
        }
    }
}
