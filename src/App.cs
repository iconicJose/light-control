using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LightControl
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "LightControl.SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }

    internal static class Theme
    {
        public static readonly Color Back = Color.FromArgb(24, 25, 28);
        public static readonly Color Border = Color.FromArgb(48, 50, 56);
        public static readonly Color Text = Color.FromArgb(232, 233, 237);
        public static readonly Color Dim = Color.FromArgb(138, 143, 152);
        public static readonly Color Track = Color.FromArgb(52, 55, 61);
        public static readonly Color Fill = Color.FromArgb(226, 228, 233);
        public static readonly Color Knob = Color.White;
        public static readonly Color MenuHover = Color.FromArgb(45, 47, 53);
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly MonitorManager _monitors = new MonitorManager();
        private readonly NotifyIcon _tray;
        private readonly Icon _icon;
        private readonly PanelForm _panel;
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "LightControl";

        public TrayApp()
        {
            _icon = BulbIcon.Create(SystemInformation.SmallIconSize.Width <= 16 ? 16 : 32);
            _panel = new PanelForm(_monitors);

            ToolStripMenuItem startup = new ToolStripMenuItem("Start with Windows");
            startup.Checked = IsAutoStart();
            startup.Click += delegate { startup.Checked = !startup.Checked; SetAutoStart(startup.Checked); };

            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { ExitThread(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Theme.Back;
            menu.ForeColor = Theme.Text;
            menu.ShowImageMargin = false;
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            _tray = new NotifyIcon();
            _tray.Icon = _icon;
            _tray.Text = "Light Control";
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) _panel.Toggle();
            };

            SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            IntPtr warm = _panel.Handle;
            GC.KeepAlive(warm);

            ThreadPool.QueueUserWorkItem(delegate
            {
                _monitors.Refresh();
                try { _panel.BeginInvoke((MethodInvoker)delegate { _panel.Relayout(); }); }
                catch { }
            });
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) OnDisplaysChanged(sender, EventArgs.Empty);
        }

        private void OnDisplaysChanged(object sender, EventArgs e)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                _monitors.Refresh();
                try { _panel.BeginInvoke((MethodInvoker)delegate { _panel.Relayout(); }); }
                catch { }
            });
        }

        private static bool IsAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            catch { return false; }
        }

        private static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                _tray.Visible = false;
                _tray.Dispose();
                _panel.Dispose();
                _monitors.Dispose();
                BulbIcon.Release(_icon);
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class PanelForm : Form
    {
        private const int PAD = 14;
        private const int WIDTH = 300;
        private const int LABEL_H = 16;
        private const int GAP = 8;
        private const int SLIDER_H = 20;
        private const int ROW_GAP = 14;
        private const int KNOB = 7;

        private readonly MonitorManager _monitors;
        private readonly Font _font;
        private readonly Font _fontBold;
        private float _sc = 1f;
        private int _drag = -1;
        private int _hover = -1;
        private int _lastHidden;

        public PanelForm(MonitorManager monitors)
        {
            _monitors = monitors;
            _font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _fontBold = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            TopMost = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(WIDTH, 100);
        }

        protected override bool ShowWithoutActivation { get { return false; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;
                cp.ClassStyle |= 0x00020000;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = Native.DWMWCP_ROUND;
            try { Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int)); }
            catch { }
        }

        public void Toggle()
        {
            if (Visible) { HidePanel(); return; }
            if (Environment.TickCount - _lastHidden < 250) return;
            Relayout();
            Show();
            Activate();

            ThreadPool.QueueUserWorkItem(delegate
            {
                int before = _monitors.Displays.Count;
                if (_drag < 0) _monitors.Refresh(); else _monitors.RefreshValues();
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (_monitors.Displays.Count != before) Relayout();
                        else Invalidate();
                    });
                }
                catch { }
            });
        }

        private void HidePanel()
        {
            _lastHidden = Environment.TickCount;
            _drag = -1;
            Hide();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible) HidePanel();
        }

        public void Relayout()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            _sc = Dpi.Scale;

            int n = _monitors.Displays.Count;
            int h = n == 0
                ? S(PAD * 2 + 22)
                : S(PAD * 2) + n * S(LABEL_H + GAP + SLIDER_H) + (n - 1) * S(ROW_GAP);

            Rectangle wa = screen.WorkingArea;
            int w = S(WIDTH);
            Bounds = new Rectangle(wa.Right - w - S(12), wa.Bottom - h - S(12), w, h);
            Invalidate();
        }

        private int S(int v) { return (int)Math.Round(v * _sc); }

        private Rectangle SliderRect(int index)
        {
            int top = S(PAD) + index * (S(LABEL_H + GAP + SLIDER_H) + S(ROW_GAP)) + S(LABEL_H + GAP);
            return new Rectangle(S(PAD), top, ClientSize.Width - S(PAD) * 2, S(SLIDER_H));
        }

        private int RowAt(Point p)
        {
            int n = _monitors.Displays.Count;
            for (int i = 0; i < n; i++)
            {
                Rectangle r = SliderRect(i);
                r.Inflate(0, S(LABEL_H));
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        private int ValueAt(int index, int x)
        {
            Rectangle r = SliderRect(index);
            int knob = S(KNOB);
            int usable = r.Width - knob * 2;
            if (usable <= 0) return 0;
            double pos = x - (r.Left + knob);
            return MonitorManager.Clamp((int)Math.Round(pos * 100.0 / usable));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int i = RowAt(e.Location);
            if (i < 0) return;
            _drag = i;
            Capture = true;
            Apply(i, ValueAt(i, e.X));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag >= 0) { Apply(_drag, ValueAt(_drag, e.X)); return; }
            int i = RowAt(e.Location);
            if (i != _hover) { _hover = i; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = -1;
            Capture = false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int i = RowAt(e.Location);
            if (i < 0) i = _monitors.Displays.Count == 1 ? 0 : -1;
            if (i < 0) return;
            int step = e.Delta > 0 ? 5 : -5;
            Apply(i, _monitors.Displays[i].Value + step);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { HidePanel(); return; }

            int i = _hover >= 0 ? _hover : (_monitors.Displays.Count > 0 ? 0 : -1);
            if (i < 0) return;
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Apply(i, _monitors.Displays[i].Value - 1);
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Apply(i, _monitors.Displays[i].Value + 1);
        }

        private void Apply(int index, int value)
        {
            List<Display> list = _monitors.Displays;
            if (index >= list.Count) return;
            int v = MonitorManager.Clamp(value);
            if (list[index].Value == v) return;
            _monitors.Set(list[index], v);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (Pen border = new Pen(Theme.Border))
                g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            List<Display> list = _monitors.Displays;
            if (list.Count == 0)
            {
                TextRenderer.DrawText(g, "No adjustable displays", _font,
                    new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                Rectangle s = SliderRect(i);
                Rectangle label = new Rectangle(s.Left, s.Top - S(LABEL_H + GAP), s.Width, S(LABEL_H));

                TextRenderer.DrawText(g, list[i].Name, _font, label, Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, list[i].Value + "%", _fontBold, label, Theme.Text,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                DrawSlider(g, s, list[i].Value);
            }
        }

        private void DrawSlider(Graphics g, Rectangle r, int value)
        {
            int knob = S(KNOB);
            int th = Math.Max(3, S(4));
            int cy = r.Top + r.Height / 2;
            int left = r.Left + knob;
            int usable = r.Width - knob * 2;
            int cx = left + (int)Math.Round(usable * value / 100.0);

            using (SolidBrush track = new SolidBrush(Theme.Track))
                Rounded(g, track, new Rectangle(r.Left, cy - th / 2, r.Width, th), th / 2);

            if (cx > r.Left)
                using (SolidBrush fill = new SolidBrush(Theme.Fill))
                    Rounded(g, fill, new Rectangle(r.Left, cy - th / 2, cx - r.Left + th / 2, th), th / 2);

            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillEllipse(shadow, cx - knob - 1, cy - knob, knob * 2 + 2, knob * 2 + 2);
            using (SolidBrush kb = new SolidBrush(Theme.Knob))
                g.FillEllipse(kb, cx - knob, cy - knob, knob * 2, knob * 2);
        }

        private static void Rounded(Graphics g, Brush b, Rectangle r, int radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            int d = Math.Max(1, radius * 2);
            if (r.Width <= d) { g.FillEllipse(b, r); return; }
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddArc(r.Left, r.Top, d, d, 90, 180);
                p.AddArc(r.Right - d, r.Top, d, d, 270, 180);
                p.CloseFigure();
                g.FillPath(b, p);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _font.Dispose(); _fontBold.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal static class Dpi
    {
        private static float _scale;

        public static float Scale
        {
            get
            {
                if (_scale <= 0f)
                {
                    try
                    {
                        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;
                    }
                    catch { _scale = 1f; }
                    if (_scale <= 0f) _scale = 1f;
                }
                return _scale;
            }
        }
    }

    internal static class BulbIcon
    {
        private static readonly Color Glass = Color.FromArgb(245, 190, 66);
        private static readonly Color Base = Color.FromArgb(198, 202, 210);

        public static Icon Create(int size)
        {
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float s = size;
                    float cx = s * 0.5f;

                    using (SolidBrush glass = new SolidBrush(Glass))
                    {
                        float r = s * 0.30f;
                        g.FillEllipse(glass, cx - r, s * 0.10f, r * 2, r * 2);
                        g.FillPolygon(glass, new PointF[]
                        {
                            new PointF(cx - s * 0.20f, s * 0.60f),
                            new PointF(cx + s * 0.20f, s * 0.60f),
                            new PointF(cx + s * 0.14f, s * 0.74f),
                            new PointF(cx - s * 0.14f, s * 0.74f)
                        });
                    }

                    using (SolidBrush baseBrush = new SolidBrush(Base))
                    {
                        float w = s * 0.28f;
                        float h = Math.Max(1f, s * 0.07f);
                        g.FillRectangle(baseBrush, cx - w / 2, s * 0.75f, w, h);
                        g.FillRectangle(baseBrush, cx - w / 2, s * 0.75f + h * 1.9f, w, h);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        public static void Release(Icon icon)
        {
            if (icon == null) return;
            IntPtr h = icon.Handle;
            icon.Dispose();
            Native.DestroyIcon(h);
        }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.Fill))
            {
                Rectangle r = e.ImageRectangle;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillEllipse(b, r.Left + r.Width / 3, r.Top + r.Height / 3, r.Width / 3, r.Height / 3);
            }
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Theme.MenuHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.MenuHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.MenuHover; } }
            public override Color MenuItemBorder { get { return Theme.MenuHover; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color ToolStripDropDownBackground { get { return Theme.Back; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Back; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Back; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Back; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Border; } }
        }
    }
}
