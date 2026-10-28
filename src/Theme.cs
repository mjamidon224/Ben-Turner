using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TATimesheet
{
    /// <summary>What a control is for, so a theme switch knows how to recolor it.</summary>
    public enum ThemeRole { None, Heading, Muted, Warn, Accent, Card, Banner, Tab }

    /// <summary>
    /// Light and dark palettes plus the code that paints every control with the current one.
    /// WinForms has no dark mode of its own, so each control type is handled here.
    /// </summary>
    public sealed class Theme
    {
        public const string SystemName = "system", LightName = "light", DarkName = "dark";

        public bool Dark;
        public Color Window, Surface, Input, Text, Heading, Muted, Border;
        public Color Accent, AccentTint, Selection, SelectionText, Chart, ChartTravel;
        public Color WarnTint, WarnText, GridBack, WeekendTint, GridLine, Placeholder;
        public Color HeaderBack, HeaderText, Banner, BannerText, Link;
        public Color ButtonBack, ButtonHover, ButtonPressed;

        public static readonly Theme Light = new Theme
        {
            Dark = false,
            Window = SystemColors.Control,
            Surface = Color.White,
            Input = SystemColors.Window,
            Text = SystemColors.ControlText,
            Heading = Color.FromArgb(51, 61, 69),
            Muted = Color.FromArgb(120, 128, 134),
            Border = Color.FromArgb(204, 204, 204),
            Accent = Color.FromArgb(18, 160, 58),
            Chart = Color.FromArgb(18, 160, 58),        // on site; validated with ChartTravel (lightness, CVD, contrast)
            ChartTravel = Color.FromArgb(110, 91, 214), // travel (violet)
            AccentTint = Color.FromArgb(226, 244, 229),
            Selection = Color.FromArgb(198, 234, 205),
            SelectionText = Color.Black,
            WarnTint = Color.FromArgb(255, 232, 196),
            WarnText = Color.FromArgb(176, 92, 0),
            GridBack = SystemColors.Window,
            WeekendTint = Color.FromArgb(247, 248, 249),
            GridLine = Color.FromArgb(226, 229, 232),
            Placeholder = Color.FromArgb(170, 176, 181),
            HeaderBack = Color.FromArgb(51, 61, 69),
            HeaderText = Color.White,
            Banner = Color.FromArgb(255, 196, 0),
            BannerText = Color.Black,
            Link = Color.FromArgb(0, 102, 204),
            ButtonBack = SystemColors.Control,
            ButtonHover = SystemColors.ControlLight,
            ButtonPressed = SystemColors.ControlDark
        };

        public static readonly Theme DarkTheme = new Theme
        {
            Dark = true,
            Window = Color.FromArgb(32, 32, 32),
            Surface = Color.FromArgb(44, 44, 44),
            Input = Color.FromArgb(58, 58, 58),
            Text = Color.FromArgb(235, 235, 235),
            Heading = Color.FromArgb(204, 211, 217),
            Muted = Color.FromArgb(150, 156, 161),
            Border = Color.FromArgb(85, 85, 85),
            Accent = Color.FromArgb(76, 205, 108),
            Chart = Color.FromArgb(54, 172, 86),        // validated pair for the dark surface (the brighter accent is too light for a fill)
            ChartTravel = Color.FromArgb(139, 123, 240),
            AccentTint = Color.FromArgb(31, 58, 39),
            Selection = Color.FromArgb(44, 92, 58),
            SelectionText = Color.White,
            WarnTint = Color.FromArgb(94, 63, 18),
            WarnText = Color.FromArgb(255, 184, 77),
            GridBack = Color.FromArgb(30, 30, 30),
            WeekendTint = Color.FromArgb(37, 37, 37),
            GridLine = Color.FromArgb(52, 52, 52),
            Placeholder = Color.FromArgb(105, 110, 115),
            HeaderBack = Color.FromArgb(45, 53, 60),
            HeaderText = Color.White,
            Banner = Color.FromArgb(230, 172, 0),
            BannerText = Color.Black,
            Link = Color.FromArgb(110, 180, 255),
            ButtonBack = Color.FromArgb(58, 58, 58),
            ButtonHover = Color.FromArgb(72, 72, 72),
            ButtonPressed = Color.FromArgb(88, 88, 88)
        };

        static Theme current = Light;
        public static Theme Current { get { return current; } }

        static readonly ConditionalWeakTable<Control, object> roles = new ConditionalWeakTable<Control, object>();
        static readonly ConditionalWeakTable<Control, object> hooked = new ConditionalWeakTable<Control, object>();

        /// <summary>Turns a setting ("system", "light", "dark") into a palette.</summary>
        public static Theme Resolve(string setting)
        {
            if (setting == DarkName) return DarkTheme;
            if (setting == LightName) return Light;
            return WindowsUsesDarkMode() ? DarkTheme : Light;
        }

        public static void Use(Theme t)
        {
            current = t;
            ToolStripManager.Renderer = t.Dark ? (ToolStripRenderer)new DarkToolStripRenderer() : new ToolStripProfessionalRenderer();
        }

        /// <summary>Windows 10/11 "Choose your app mode" setting.</summary>
        public static bool WindowsUsesDarkMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static T SetRole<T>(T c, ThemeRole role) where T : Control
        {
            roles.Remove(c);
            roles.Add(c, role);
            return c;
        }

        static ThemeRole RoleOf(Control c)
        {
            object r;
            return roles.TryGetValue(c, out r) ? (ThemeRole)r : ThemeRole.None;
        }

        /// <summary>Recolors a control and everything inside it.</summary>
        public static void Apply(Control c)
        {
            var t = current;
            var role = RoleOf(c);

            var form = c as Form;
            if (form != null)
            {
                form.BackColor = t.Window;
                form.ForeColor = t.Text;
                HookNative(form);
            }
            else if (c is DataGridView)
            {
                // Rows are styled by the owner; only its scrollbars need the native treatment.
                foreach (Control child in c.Controls) Apply(child);
                return;
            }
            else if (c is ThemedDatePicker)
            {
                ((ThemedDatePicker)c).RefreshTheme();
            }
            else if (c is ComboBox || c is TextBox || c is ListBox)
            {
                c.BackColor = t.Input;
                c.ForeColor = t.Text;
                var tb = c as TextBox;
                if (tb != null) tb.BorderStyle = t.Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                var lb = c as ListBox;
                if (lb != null) lb.BorderStyle = t.Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                HookNative(c);
            }
            else if (c is Button)
            {
                StyleButton((Button)c, t);
            }
            else if (c is CheckBox || c is RadioButton)
            {
                var b = (ButtonBase)c;
                b.ForeColor = t.Text;
                if (role == ThemeRole.Tab)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = t.Surface;
                    b.FlatAppearance.BorderColor = t.Border;
                    b.FlatAppearance.CheckedBackColor = t.AccentTint;
                    b.FlatAppearance.MouseOverBackColor = t.Dark ? t.ButtonHover : t.Selection;
                }
                else
                {
                    b.FlatStyle = t.Dark ? FlatStyle.Flat : FlatStyle.Standard;
                }
            }
            else if (c is LinkLabel)
            {
                var l = (LinkLabel)c;
                l.LinkColor = l.ActiveLinkColor = l.VisitedLinkColor = t.Link;
            }
            else if (c is Label)
            {
                switch (role)
                {
                    case ThemeRole.Heading: c.ForeColor = t.Heading; break;
                    case ThemeRole.Muted: c.ForeColor = t.Muted; break;
                    case ThemeRole.Warn: c.ForeColor = t.WarnText; break;
                    case ThemeRole.Accent: c.ForeColor = t.Accent; break;
                    case ThemeRole.Banner: c.ForeColor = t.BannerText; c.BackColor = t.Banner; break;
                    default: c.ForeColor = t.Text; break;
                }
            }
            else if (c is ToolStrip)
            {
                var ts = (ToolStrip)c;
                ts.RenderMode = ToolStripRenderMode.ManagerRenderMode;
                ts.BackColor = t.Window;
                ts.ForeColor = t.Text;
            }
            else if (c is ScrollBar)
            {
                HookNative(c);
            }
            else if (role == ThemeRole.Card)
            {
                c.BackColor = t.Surface;
            }

            foreach (Control child in c.Controls) Apply(child);
        }

        static void StyleButton(Button b, Theme t)
        {
            if (t.Dark)
            {
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = t.ButtonBack;
                b.ForeColor = t.Text;
                b.FlatAppearance.BorderColor = t.Border;
                b.FlatAppearance.MouseOverBackColor = t.ButtonHover;
                b.FlatAppearance.MouseDownBackColor = t.ButtonPressed;
            }
            else
            {
                b.FlatStyle = FlatStyle.Standard;
                b.ResetBackColor();
                b.ResetForeColor();
                b.UseVisualStyleBackColor = true;
            }
        }

        // ---- Native Windows bits: dark title bars, scrollbars and dropdown buttons ------------

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>Applies the native look now (if the window exists) and whenever it gets recreated.</summary>
        static void HookNative(Control c)
        {
            object ignored;
            if (!hooked.TryGetValue(c, out ignored))
            {
                hooked.Add(c, true);
                c.HandleCreated += (s, e) => ApplyNative((Control)s);
            }
            if (c.IsHandleCreated) ApplyNative(c);
        }

        static void ApplyNative(Control c)
        {
            bool dark = current.Dark;
            try
            {
                if (c is Form)
                {
                    int on = dark ? 1 : 0;
                    // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 11 / newer 10); 19 on older Windows 10 builds.
                    if (DwmSetWindowAttribute(c.Handle, 20, ref on, 4) != 0)
                        DwmSetWindowAttribute(c.Handle, 19, ref on, 4);
                    const uint NoSizeMoveZOrderFrameChanged = 0x0001 | 0x0002 | 0x0004 | 0x0020;
                    SetWindowPos(c.Handle, IntPtr.Zero, 0, 0, 0, 0, NoSizeMoveZOrderFrameChanged);
                }
                else if (c is ComboBox)
                {
                    SetWindowTheme(c.Handle, dark ? "DarkMode_CFD" : null, null);
                }
                else
                {
                    SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : null, null);
                }
            }
            catch (Exception)
            {
                // Not on Windows, or an older Windows without these: the colors still apply.
            }
        }
    }

    /// <summary>Dark menus, context menus and status bar.</summary>
    public class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var label = e.Item as ToolStripStatusLabel;
            if (label == null || !label.IsLink)
                e.TextColor = e.Item.Enabled ? Theme.Current.Text : Theme.Current.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is StatusStrip)
            {
                // The stock renderer draws a bright white line above the status bar.
                using (var pen = new Pen(Theme.Current.Border))
                    e.Graphics.DrawLine(pen, 0, 0, e.ToolStrip.Width, 0);
                return;
            }
            base.OnRenderToolStripBorder(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Current.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using (var pen = new Pen(Theme.Current.Accent, 2f))
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.55f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f),
                    new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.28f)
                });
            }
        }

        class DarkColors : ProfessionalColorTable
        {
            static Color W { get { return Theme.DarkTheme.Window; } }
            static Color S { get { return Theme.DarkTheme.Surface; } }
            static Color H { get { return Theme.DarkTheme.ButtonHover; } }
            static Color B { get { return Theme.DarkTheme.Border; } }

            public override Color MenuStripGradientBegin { get { return W; } }
            public override Color MenuStripGradientEnd { get { return W; } }
            public override Color StatusStripGradientBegin { get { return W; } }
            public override Color StatusStripGradientEnd { get { return W; } }
            public override Color ToolStripDropDownBackground { get { return S; } }
            public override Color ToolStripBorder { get { return B; } }
            public override Color ToolStripGradientBegin { get { return W; } }
            public override Color ToolStripGradientMiddle { get { return W; } }
            public override Color ToolStripGradientEnd { get { return W; } }
            public override Color ImageMarginGradientBegin { get { return S; } }
            public override Color ImageMarginGradientMiddle { get { return S; } }
            public override Color ImageMarginGradientEnd { get { return S; } }
            public override Color MenuBorder { get { return B; } }
            public override Color MenuItemBorder { get { return H; } }
            public override Color MenuItemSelected { get { return H; } }
            public override Color MenuItemSelectedGradientBegin { get { return H; } }
            public override Color MenuItemSelectedGradientEnd { get { return H; } }
            public override Color MenuItemPressedGradientBegin { get { return S; } }
            public override Color MenuItemPressedGradientMiddle { get { return S; } }
            public override Color MenuItemPressedGradientEnd { get { return S; } }
            public override Color SeparatorDark { get { return B; } }
            public override Color SeparatorLight { get { return S; } }
            public override Color CheckBackground { get { return S; } }
            public override Color CheckSelectedBackground { get { return H; } }
            public override Color CheckPressedBackground { get { return H; } }
            public override Color ButtonSelectedHighlight { get { return H; } }
            public override Color ButtonSelectedGradientBegin { get { return H; } }
            public override Color ButtonSelectedGradientEnd { get { return H; } }
            public override Color GripDark { get { return B; } }
            public override Color GripLight { get { return W; } }
        }
    }

    /// <summary>
    /// A DateTimePicker that can go dark. Windows always paints the date box white, so in dark
    /// mode this draws the box itself; typing and the calendar dropdown still work as normal.
    /// </summary>
    public class ThemedDatePicker : DateTimePicker
    {
        bool customPaint;

        void SyncPaintMode()
        {
            bool want = Theme.Current.Dark;
            if (want == customPaint) return;
            customPaint = want;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, want);
        }

        /// <summary>Switches between native and custom painting to match the current theme.</summary>
        public void RefreshTheme()
        {
            SyncPaintMode();
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SyncPaintMode();
            FitWidth();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FitWidth();
        }

        /// <summary>Never narrower than the longest date it can show, plus the dropdown button.</summary>
        void FitWidth()
        {
            if (Dock == DockStyle.Fill || !IsHandleCreated) return;
            var sample = new DateTime(2026, 12, 30); // Wednesday, two-digit month and day
            string text = Format == DateTimePickerFormat.Custom && !string.IsNullOrEmpty(CustomFormat) ? sample.ToString(CustomFormat)
                        : Format == DateTimePickerFormat.Long ? sample.ToLongDateString()
                        : Format == DateTimePickerFormat.Time ? sample.ToLongTimeString()
                        : sample.ToShortDateString();
            int needed = TextRenderer.MeasureText(text, Font).Width + SystemInformation.VerticalScrollBarWidth + Font.Height;
            if (Width < needed) Width = needed;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!customPaint) { base.OnPaint(e); return; }
            var t = Theme.Current;
            var g = e.Graphics;
            var r = ClientRectangle;
            int button = SystemInformation.VerticalScrollBarWidth + 2;
            using (var back = new SolidBrush(Enabled ? t.Input : t.Surface)) g.FillRectangle(back, r);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(r.X + 4, r.Y, r.Width - button - 6, r.Height),
                Enabled ? t.Text : t.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            int cx = r.Right - button / 2 - 1, cy = r.Top + r.Height / 2;
            using (var arrow = new SolidBrush(t.Text))
                g.FillPolygon(arrow, new[] { new Point(cx - 4, cy - 2), new Point(cx + 5, cy - 2), new Point(cx, cy + 3) });
            using (var pen = new Pen(Focused ? t.Accent : t.Border))
                g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
        }

        protected override void OnValueChanged(EventArgs e) { base.OnValueChanged(e); if (customPaint) Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (customPaint) Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (customPaint) Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (customPaint) Invalidate(); }
    }
}
