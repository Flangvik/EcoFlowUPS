using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EcoFlowMonitor.UI
{
    /// <summary>
    /// Applies a dark or light palette to WinForms controls recursively.
    /// Call <see cref="SetMode"/> once at startup (from config), then call
    /// <see cref="Apply(Form)"/> on every form when it opens.
    /// </summary>
    public static class ThemeManager
    {
        // ------------------------------------------------------------------
        // Win32 — dark title bar (Windows 10 20H1+ / Windows 11)
        // ------------------------------------------------------------------
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        // ------------------------------------------------------------------
        // Current mode
        // ------------------------------------------------------------------
        public static bool IsDark { get; private set; } = true;

        public static void SetMode(bool dark) => IsDark = dark;

        // ------------------------------------------------------------------
        // Color palette
        // ------------------------------------------------------------------
        public static Color Background  => IsDark ? Color.FromArgb(30,  30,  30)  : SystemColors.Control;
        public static Color Surface     => IsDark ? Color.FromArgb(37,  37,  38)  : SystemColors.ControlLight;
        public static Color ControlBg   => IsDark ? Color.FromArgb(51,  51,  55)  : SystemColors.Window;
        public static Color ButtonBg    => IsDark ? Color.FromArgb(62,  62,  66)  : SystemColors.ButtonFace;
        public static Color Border      => IsDark ? Color.FromArgb(80,  80,  80)  : SystemColors.ControlDark;
        public static Color Foreground  => IsDark ? Color.FromArgb(220, 220, 220) : SystemColors.ControlText;
        public static Color SubText     => IsDark ? Color.FromArgb(150, 150, 150) : Color.DimGray;
        public static Color MenuHover   => IsDark ? Color.FromArgb(0,   120, 212) : SystemColors.Highlight;
        public static Color MenuHoverFg => Color.White;

        // ------------------------------------------------------------------
        // Apply to a Form (title bar + all controls)
        // ------------------------------------------------------------------
        public static void Apply(Form form)
        {
            ApplyControl(form);
            // Dark title bar — call after handle is created
            if (form.IsHandleCreated)
                SetTitleBarDark(form, IsDark);
            else
                form.HandleCreated += (s, e) => SetTitleBarDark(form, IsDark);
        }

        // ------------------------------------------------------------------
        // Apply to a ContextMenuStrip (tray right-click menu)
        // ------------------------------------------------------------------
        public static void Apply(ContextMenuStrip menu)
        {
            if (IsDark)
                menu.Renderer = new DarkMenuRenderer();
            else
                menu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
        }

        // ------------------------------------------------------------------
        // Recursive control theming
        // ------------------------------------------------------------------
        private static void ApplyControl(Control c)
        {
            switch (c)
            {
                case Form f:
                    f.BackColor = Background;
                    f.ForeColor = Foreground;
                    break;

                case TabControl tc:
                    tc.BackColor = Surface;
                    tc.ForeColor = Foreground;
                    break;

                case TabPage tp:
                    tp.BackColor = Surface;
                    tp.ForeColor = Foreground;
                    break;

                case SplitContainer sc:
                    sc.BackColor = Surface;
                    sc.ForeColor = Foreground;
                    // The SplitterPanel children will be picked up by the loop below
                    break;

                case Panel p:
                    p.BackColor = Surface;
                    p.ForeColor = Foreground;
                    break;

                case TextBox tb:
                    tb.BackColor = ControlBg;
                    tb.ForeColor = Foreground;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case RichTextBox rtb:
                    rtb.BackColor = ControlBg;
                    rtb.ForeColor = Foreground;
                    break;

                case ListBox lb:
                    lb.BackColor = ControlBg;
                    lb.ForeColor = Foreground;
                    break;

                case ListView lv:
                    lv.BackColor = ControlBg;
                    lv.ForeColor = Foreground;
                    break;

                case ComboBox cb:
                    cb.BackColor = ControlBg;
                    cb.ForeColor = Foreground;
                    cb.FlatStyle = FlatStyle.Flat;
                    break;

                case NumericUpDown nud:
                    nud.BackColor = ControlBg;
                    nud.ForeColor = Foreground;
                    break;

                case Button btn:
                    btn.BackColor = ButtonBg;
                    btn.ForeColor = Foreground;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = Border;
                    break;

                case CheckBox chk:
                    chk.ForeColor = Foreground;
                    chk.BackColor = Color.Transparent;
                    break;

                case RadioButton rb:
                    rb.ForeColor = Foreground;
                    rb.BackColor = Color.Transparent;
                    break;

                case Label lbl:
                    // Preserve explicitly set label colours (status badges, notes).
                    // DefaultForeColor == SystemColors.ControlText for un-styled labels.
                    if (lbl.ForeColor == SystemColors.ControlText)
                        lbl.ForeColor = Foreground;
                    lbl.BackColor = Color.Transparent;
                    break;

                case GroupBox gb:
                    gb.BackColor = Surface;
                    gb.ForeColor = Foreground;
                    break;

                default:
                    c.BackColor = Surface;
                    c.ForeColor = Foreground;
                    break;
            }

            foreach (Control child in c.Controls)
                ApplyControl(child);
        }

        // ------------------------------------------------------------------
        // Win32 title bar colouring
        // ------------------------------------------------------------------
        private static void SetTitleBarDark(Form form, bool dark)
        {
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref value, Marshal.SizeOf(value));
            }
            catch { /* DWM not available — pre-Win10 */ }
        }

        // ------------------------------------------------------------------
        // Dark ContextMenuStrip renderer
        // ------------------------------------------------------------------
        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColorTable()) { }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(Background))
                    e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var bg = e.Item.Selected ? MenuHover : Background;
                using (var brush = new SolidBrush(bg))
                    e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Foreground : SubText;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (var pen = new Pen(Border))
                    e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
            }
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuBorder                    => Border;
            public override Color MenuItemBorder               => Color.Transparent;
            public override Color MenuItemSelected             => MenuHover;
            public override Color ToolStripDropDownBackground  => Background;
            public override Color ImageMarginGradientBegin     => Background;
            public override Color ImageMarginGradientMiddle    => Background;
            public override Color ImageMarginGradientEnd       => Background;
            public override Color SeparatorDark                => Border;
            public override Color SeparatorLight               => Border;
        }
    }
}
