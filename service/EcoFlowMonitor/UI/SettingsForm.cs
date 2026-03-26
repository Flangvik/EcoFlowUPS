using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Core;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.UI
{
    public class SettingsForm : Form
    {
        private readonly AppConfig _config;

        private CheckBox _chkStartup;
        private CheckBox _chkDarkMode;
        private TextBox  _txtLogPath;
        private Button   _btnBrowseLog;
        private Button   _btnSave;
        private Button   _btnCancel;

        public SettingsForm(AppConfig config)
        {
            _config = config;
            InitializeComponent();
            LoadConfig();
            ThemeManager.Apply(this);
            _btnSave.BackColor = ThemeManager.MenuHover;
            _btnSave.ForeColor = Color.White;
            _btnSave.FlatAppearance.BorderColor = ThemeManager.MenuHover;
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private void InitializeComponent()
        {
            this.Text            = "EcoFlow Monitor — Settings";
            this.Size            = new Size(480, 420);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox     = false;
            this.StartPosition   = FormStartPosition.CenterParent;
            this.AutoScaleMode   = AutoScaleMode.Dpi;

            // ---- Bottom button bar ----
            var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 54 };
            var topLine   = new Panel { Dock = DockStyle.Top, Height = 1 };
            topLine.Paint += (s, e) => e.Graphics.DrawLine(new System.Drawing.Pen(ThemeManager.Border), 0, 0, topLine.Width, 0);

            var btnRow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents  = false,
                Padding       = new Padding(12, 10, 12, 10)
            };
            _btnCancel = new Button { Text = "Cancel", Width = 90, Height = 32, Margin = new Padding(8, 0, 0, 0), FlatStyle = FlatStyle.Flat };
            _btnSave   = new Button { Text = "Save",   Width = 90, Height = 32, Margin = new Padding(0),          FlatStyle = FlatStyle.Flat };
            _btnCancel.Click += (s, e) => this.Close();
            _btnSave.Click   += BtnSave_Click;
            btnRow.Controls.Add(_btnCancel);
            btnRow.Controls.Add(_btnSave);
            bottomBar.Controls.Add(btnRow);
            bottomBar.Controls.Add(topLine);
            this.Controls.Add(bottomBar);

            // ---- Scrollable content ----
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24, 18, 24, 18) };

            bool isAdmin = ElevationHelper.IsAdministrator();

            // Admin status badge
            var badgeColor = isAdmin ? Color.FromArgb(30, 160, 80) : Color.FromArgb(200, 80, 30);
            var badgeText  = isAdmin ? "\u2713  Running as Administrator" : "\u26a0  Not running as Administrator";
            var badgeLabel = new Label
            {
                Text      = badgeText,
                Dock      = DockStyle.Top,
                Height    = 28,
                AutoSize  = false,
                ForeColor = badgeColor,
                Font      = new Font(Font.FontFamily, Font.Size, FontStyle.Bold)
            };
            scroll.Controls.Add(badgeLabel);

            if (!isAdmin)
            {
                var elevFlow  = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(0, 6, 0, 0), WrapContents = false };
                var btnElev   = new Button { Text = "Restart as Administrator", Width = 200, Height = 26, FlatStyle = FlatStyle.Flat };
                btnElev.Click += (s, e) => ElevationHelper.RestartElevated();
                elevFlow.Controls.Add(btnElev);
                scroll.Controls.Add(elevFlow);
            }

            scroll.Controls.Add(MakeSpacer(14));
            scroll.Controls.Add(MakeSectionHeader("Startup"));

            _chkStartup = new CheckBox
            {
                Text     = "Start with Windows",
                Dock     = DockStyle.Top,
                Height   = 26,
                AutoSize = false,
                Enabled  = isAdmin
            };
            scroll.Controls.Add(_chkStartup);

            var startupNote = new Label
            {
                Text      = isAdmin
                    ? "Creates a Task Scheduler entry that launches elevated at logon — no UAC prompt on boot."
                    : "Requires administrator privileges (uses Task Scheduler for elevated autostart).",
                Dock      = DockStyle.Top,
                Height    = 26,
                AutoSize  = false,
                ForeColor = Color.Gray,
                Font      = new Font(Font.FontFamily, Font.Size - 0.5f, FontStyle.Italic),
                Padding   = new Padding(22, 0, 0, 0)
            };
            scroll.Controls.Add(startupNote);

            scroll.Controls.Add(MakeSpacer(14));
            scroll.Controls.Add(MakeSectionHeader("Appearance"));

            _chkDarkMode = new CheckBox
            {
                Text     = "Dark mode",
                Dock     = DockStyle.Top,
                Height   = 26,
                AutoSize = false
            };
            scroll.Controls.Add(_chkDarkMode);

            scroll.Controls.Add(MakeSpacer(14));
            scroll.Controls.Add(MakeSectionHeader("Logging"));

            var logLabel = new Label { Text = "Error log path:", Dock = DockStyle.Top, Height = 20, AutoSize = false };
            scroll.Controls.Add(logLabel);

            var logRow = new Panel { Dock = DockStyle.Top, Height = 30 };
            _txtLogPath   = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
            _btnBrowseLog = new Button  { Text = "Browse…", Dock = DockStyle.Right, Width = 82, FlatStyle = FlatStyle.Flat };
            _btnBrowseLog.Click += BtnBrowseLog_Click;
            logRow.Controls.Add(_txtLogPath);
            logRow.Controls.Add(_btnBrowseLog);
            scroll.Controls.Add(logRow);

            this.Controls.Add(scroll);
        }

        // ------------------------------------------------------------------
        // Load / save
        // ------------------------------------------------------------------

        private void LoadConfig()
        {
            _chkStartup.Checked  = StartupManager.IsEnabled();
            _chkDarkMode.Checked = _config.General?.DarkMode ?? true;
            _txtLogPath.Text     = string.IsNullOrWhiteSpace(_config.General?.ErrorLogPath)
                ? Logger.DefaultPath
                : _config.General.ErrorLogPath;
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (_config.General == null) _config.General = new GeneralSettings();

            _config.General.ErrorLogPath = _txtLogPath.Text.Trim();
            _config.General.DarkMode     = _chkDarkMode.Checked;
            ThemeManager.SetMode(_chkDarkMode.Checked);
            ThemeManager.Apply(this);
            _btnSave.BackColor = ThemeManager.MenuHover;
            _btnSave.ForeColor = Color.White;

            if (_chkStartup.Enabled)
            {
                bool want    = _chkStartup.Checked;
                bool current = StartupManager.IsEnabled();
                if (want && !current)
                {
                    if (!StartupManager.Enable())
                        MessageBox.Show(
                            "Could not create the startup task. Make sure the app is running as Administrator.",
                            "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (!want && current)
                {
                    StartupManager.Disable();
                }
            }

            ConfigManager.Save(_config);
            this.Close();
        }

        private void BtnBrowseLog_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Title            = "Select error log file",
                Filter           = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName         = Path.GetFileName(_txtLogPath.Text),
                InitialDirectory = string.IsNullOrWhiteSpace(_txtLogPath.Text)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                    : Path.GetDirectoryName(_txtLogPath.Text)
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    _txtLogPath.Text = dlg.FileName;
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private Panel MakeSectionHeader(string title)
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 30 };
            var lbl   = new Label
            {
                Text      = title.ToUpperInvariant(),
                Dock      = DockStyle.Fill,
                Font      = new Font(Font.FontFamily, Font.Size - 0.5f, FontStyle.Bold),
                ForeColor = ThemeManager.SubText,
                TextAlign = ContentAlignment.BottomLeft,
                Padding   = new Padding(0, 0, 0, 3)
            };
            var line = new Panel { Dock = DockStyle.Bottom, Height = 1 };
            line.Paint += (s, e) => e.Graphics.DrawLine(new System.Drawing.Pen(ThemeManager.Border), 0, 0, line.Width, 0);
            panel.Controls.Add(lbl);
            panel.Controls.Add(line);
            return panel;
        }

        private static Panel MakeSpacer(int height) =>
            new Panel { Dock = DockStyle.Top, Height = height };
    }
}
