using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using EcoFlowMonitor.Core;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.UI
{
    /// <summary>
    /// Shown when no account is configured yet.
    /// On DialogResult.OK, read Account and DiscoveredDevices.
    /// </summary>
    public class LoginForm : Form
    {
        public AccountConfig             Account           { get; private set; }
        public List<(string sn, string name)> DiscoveredDevices { get; private set; }

        private TextBox _txtEmail;
        private TextBox _txtPassword;
        private Button  _btnSignIn;
        private Label   _lblError;

        public LoginForm()
        {
            InitializeComponent();
            ThemeManager.Apply(this);
            StyleAccentButton(_btnSignIn);
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private void InitializeComponent()
        {
            this.Text            = "EcoFlow Monitor";
            this.Size            = new Size(400, 520);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox     = false;
            this.MinimizeBox     = false;
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.AutoScaleMode   = AutoScaleMode.Dpi;

            // Outer padding panel
            var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(48, 36, 48, 36) };

            // With DockStyle.Top, controls added LAST appear TOPMOST.
            // Add in reverse visual order (bottom → top).

            // ---- Error label (visually last) ----
            _lblError = new Label
            {
                Dock      = DockStyle.Top,
                Height    = 36,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(220, 80, 60),
                Font      = new Font("Segoe UI", 9),
                Visible   = false
            };
            outer.Controls.Add(_lblError);

            // ---- Sign In button ----
            _btnSignIn = new Button
            {
                Text      = "Sign In",
                Dock      = DockStyle.Top,
                Height    = 40,
                Font      = new Font("Segoe UI", 11, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand
            };
            _btnSignIn.FlatAppearance.BorderSize = 0;
            _btnSignIn.Click += (s, e) => _ = SignInAsync();
            outer.Controls.Add(_btnSignIn);

            outer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 20 });

            // ---- Password ----
            _txtPassword = MakeField();
            _txtPassword.PasswordChar = '•';
            _txtPassword.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) _ = SignInAsync(); };
            outer.Controls.Add(_txtPassword);
            outer.Controls.Add(MakeFieldLabel("Password"));

            outer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12 });

            // ---- Email ----
            _txtEmail = MakeField();
            outer.Controls.Add(_txtEmail);
            outer.Controls.Add(MakeFieldLabel("Email"));

            outer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 20 });

            // ---- Sub-title ----
            outer.Controls.Add(MakeStaticLabel(
                "Sign in with your EcoFlow account", 9, FontStyle.Regular, ContentAlignment.TopCenter, 28,
                color: Color.Gray));

            // ---- App title ----
            outer.Controls.Add(MakeStaticLabel(
                "EcoFlow Monitor", 24, FontStyle.Bold, ContentAlignment.MiddleCenter, 40));

            // ---- Logo (visually first / topmost) ----
            var logoBox = new Panel { Dock = DockStyle.Top, Height = 72 };
            logoBox.Paint += PaintLogo;
            outer.Controls.Add(logoBox);

            this.Controls.Add(outer);
            this.AcceptButton = _btnSignIn;
        }

        // ------------------------------------------------------------------
        // Sign-in logic
        // ------------------------------------------------------------------

        private async Task SignInAsync()
        {
            string email    = _txtEmail.Text.Trim();
            string password = _txtPassword.Text;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowError("Please enter your email and password.");
                return;
            }

            SetBusy(true);

            try
            {
                List<(string, string)> devices;
                using (var client = new EcoFlowClient())
                {
                    // No ConfigureAwait(false) — stay on UI thread for safe property access
                    await client.LoginAsync(email, password);
                    devices = await client.GetAllDevicesAsync();
                }

                if (devices.Count == 0)
                {
                    SetBusy(false);
                    ShowError("No devices found on this account.");
                    return;
                }

                Account           = new AccountConfig { Email = email, Password = password };
                DiscoveredDevices = devices;
                DialogResult      = DialogResult.OK;
            }
            catch (Exception ex)
            {
                SetBusy(false);
                ShowError(IsCredentialError(ex)
                    ? "Incorrect email or password."
                    : "Could not connect. Check your internet connection.");
            }
        }

        private static bool IsCredentialError(Exception ex)
        {
            var msg = ex.Message;
            return msg.Contains("401") || msg.Contains("Login failed") || msg.Contains("password");
        }

        private void SetBusy(bool busy)
        {
            _btnSignIn.Enabled = !busy;
            _btnSignIn.Text    = busy ? "Signing in…" : "Sign In";
            _txtEmail.Enabled  = !busy;
            _txtPassword.Enabled = !busy;
            _lblError.Visible  = false;
        }

        private void ShowError(string msg)
        {
            _lblError.Text    = msg;
            _lblError.Visible = true;
        }

        // ------------------------------------------------------------------
        // Paint helpers
        // ------------------------------------------------------------------

        private void PaintLogo(object sender, PaintEventArgs e)
        {
            var p  = (Panel)sender;
            var g  = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int   size = 56;
            float x    = (p.Width - size) / 2f;
            float y    = (p.Height - size) / 2f;

            using (var b = new SolidBrush(ThemeManager.MenuHover))
                g.FillEllipse(b, x, y, size, size);

            using (var f = new Font("Segoe UI", 22, FontStyle.Bold))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("E", f, Brushes.White, new RectangleF(x, y, size, size), sf);
        }

        // ------------------------------------------------------------------
        // Layout helpers
        // ------------------------------------------------------------------

        private static Label MakeStaticLabel(string text, float size, FontStyle style,
            ContentAlignment align, int height, Color? color = null)
        {
            return new Label
            {
                Text      = text,
                Dock      = DockStyle.Top,
                Height    = height,
                Font      = new Font("Segoe UI", size, style),
                TextAlign = align,
                ForeColor = color ?? Color.Empty,
                AutoSize  = false
            };
        }

        private static Label MakeFieldLabel(string text) =>
            new Label
            {
                Text     = text,
                Dock     = DockStyle.Top,
                Height   = 20,
                Font     = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                AutoSize = false
            };

        private static TextBox MakeField() =>
            new TextBox
            {
                Dock        = DockStyle.Top,
                Height      = 30,
                Font        = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle
            };

        private static void StyleAccentButton(Button btn)
        {
            btn.BackColor = ThemeManager.MenuHover;
            btn.ForeColor = Color.White;
            btn.FlatAppearance.BorderColor = ThemeManager.MenuHover;
        }
    }
}
