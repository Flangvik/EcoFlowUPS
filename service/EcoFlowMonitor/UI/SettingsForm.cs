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
        private AppConfig _config;

        // Tab control
        private TabControl _tabs;

        // ---- Devices tab controls ----
        private ListBox _deviceList;
        private Button _btnAddDevice;
        private Button _btnRemoveDevice;

        // Device detail panel
        private Panel _deviceDetailPanel;
        private TextBox _txtName;
        private TextBox _txtEmail;
        private TextBox _txtPassword;
        private TextBox _txtSn;
        private ListView _ruleList;
        private Button _btnAddRule;
        private Button _btnEditRule;
        private Button _btnRemoveRule;

        // ---- General tab controls ----
        private CheckBox _chkStartup;
        private TextBox _txtLogPath;
        private Button _btnBrowseLog;

        // ---- Bottom buttons ----
        private Button _btnSave;
        private Button _btnCancel;

        // Track which device is being shown in the detail panel
        private int _selectedDeviceIndex = -1;

        public SettingsForm(AppConfig config)
        {
            _config = config;
            InitializeComponent();
            LoadConfig();
        }

        // ------------------------------------------------------------------
        // InitializeComponent
        // ------------------------------------------------------------------

        private void InitializeComponent()
        {
            this.Text = "EcoFlow Monitor — Settings";
            this.Size = new Size(740, 560);
            this.MinimumSize = new Size(640, 480);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterScreen;

            // ---- Tab control ----
            _tabs = new TabControl { Dock = DockStyle.Fill };

            var devTab = new TabPage("Devices");
            var genTab = new TabPage("General");

            _tabs.TabPages.Add(devTab);
            _tabs.TabPages.Add(genTab);

            BuildDevicesTab(devTab);
            BuildGeneralTab(genTab);

            // ---- Bottom button panel ----
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                Padding = new Padding(8, 6, 8, 6)
            };

            _btnSave = new Button
            {
                Text = "Save",
                Width = 90,
                Height = 30,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            _btnSave.Click += BtnSave_Click;

            _btnCancel = new Button
            {
                Text = "Cancel",
                Width = 90,
                Height = 30,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            _btnCancel.Click += (s, e) => this.Close();

            // Position buttons right-aligned
            _btnCancel.Location = new Point(bottomPanel.Width - _btnCancel.Width - 8, 7);
            _btnSave.Location   = new Point(bottomPanel.Width - _btnSave.Width - _btnCancel.Width - 16, 7);

            bottomPanel.Controls.Add(_btnSave);
            bottomPanel.Controls.Add(_btnCancel);
            bottomPanel.Resize += (s, e) =>
            {
                _btnCancel.Location = new Point(bottomPanel.Width - _btnCancel.Width - 8, 7);
                _btnSave.Location   = new Point(bottomPanel.Width - _btnSave.Width - _btnCancel.Width - 16, 7);
            };

            this.Controls.Add(_tabs);
            this.Controls.Add(bottomPanel);
        }

        // ------------------------------------------------------------------
        // Devices tab
        // ------------------------------------------------------------------

        private void BuildDevicesTab(TabPage tab)
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 210,
                Panel1MinSize = 150,
                Panel2MinSize = 300
            };

            // ---- Left: device list ----
            _deviceList = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false
            };
            _deviceList.SelectedIndexChanged += DeviceList_SelectedIndexChanged;

            _btnAddDevice = new Button { Text = "+ Add", Width = 70, Height = 26, Dock = DockStyle.Left };
            _btnRemoveDevice = new Button { Text = "Remove", Width = 70, Height = 26, Dock = DockStyle.Right };
            _btnAddDevice.Click += BtnAddDevice_Click;
            _btnRemoveDevice.Click += BtnRemoveDevice_Click;

            var leftButtonPanel = new Panel { Dock = DockStyle.Bottom, Height = 32 };
            leftButtonPanel.Controls.Add(_btnAddDevice);
            leftButtonPanel.Controls.Add(_btnRemoveDevice);

            split.Panel1.Controls.Add(_deviceList);
            split.Panel1.Controls.Add(leftButtonPanel);

            // ---- Right: device detail ----
            _deviceDetailPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            BuildDeviceDetailPanel(_deviceDetailPanel);
            _deviceDetailPanel.Enabled = false; // disabled until a device is selected

            split.Panel2.Controls.Add(_deviceDetailPanel);

            tab.Controls.Add(split);
        }

        private void BuildDeviceDetailPanel(Panel panel)
        {
            int y = 8;
            const int labelWidth = 110;
            const int fieldHeight = 22;
            const int gap = 8;

            // Display Name
            panel.Controls.Add(MakeLabel("Display Name:", 8, y, labelWidth));
            _txtName = MakeTextBox(labelWidth + 16, y, panel.Width - labelWidth - 32, fieldHeight);
            _txtName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(_txtName);
            y += fieldHeight + gap;

            // Email
            panel.Controls.Add(MakeLabel("Email:", 8, y, labelWidth));
            _txtEmail = MakeTextBox(labelWidth + 16, y, panel.Width - labelWidth - 32, fieldHeight);
            _txtEmail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(_txtEmail);
            y += fieldHeight + gap;

            // Password
            panel.Controls.Add(MakeLabel("Password:", 8, y, labelWidth));
            _txtPassword = MakeTextBox(labelWidth + 16, y, panel.Width - labelWidth - 32, fieldHeight);
            _txtPassword.PasswordChar = '*';
            _txtPassword.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(_txtPassword);
            y += fieldHeight + gap;

            // Serial Number
            panel.Controls.Add(MakeLabel("Serial Number:", 8, y, labelWidth));
            _txtSn = MakeTextBox(labelWidth + 16, y, panel.Width - labelWidth - 32, fieldHeight);
            _txtSn.PlaceholderText = "Leave blank to auto-detect";
            _txtSn.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(_txtSn);
            y += fieldHeight + gap + 4;

            // Rules label
            var rulesLabel = MakeLabel("Rules:", 8, y, 60);
            rulesLabel.Font = new Font(rulesLabel.Font, FontStyle.Bold);
            panel.Controls.Add(rulesLabel);
            y += 20;

            // Rules ListView
            _ruleList = new ListView
            {
                Location = new Point(8, y),
                Size = new Size(panel.Width - 16, 120),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            _ruleList.Columns.Add("Rule Name", 150);
            _ruleList.Columns.Add("Trigger", 120);
            _ruleList.Columns.Add("Enabled", 60);
            _ruleList.DoubleClick += BtnEditRule_Click;
            panel.Controls.Add(_ruleList);
            y += 128;

            // Rule buttons
            _btnAddRule = new Button { Text = "+ Add Rule", Width = 90, Height = 26, Location = new Point(8, y) };
            _btnEditRule = new Button { Text = "Edit Rule", Width = 80, Height = 26, Location = new Point(106, y) };
            _btnRemoveRule = new Button { Text = "Remove", Width = 75, Height = 26, Location = new Point(194, y) };

            _btnAddRule.Click += BtnAddRule_Click;
            _btnEditRule.Click += BtnEditRule_Click;
            _btnRemoveRule.Click += BtnRemoveRule_Click;

            panel.Controls.Add(_btnAddRule);
            panel.Controls.Add(_btnEditRule);
            panel.Controls.Add(_btnRemoveRule);
        }

        // ------------------------------------------------------------------
        // General tab
        // ------------------------------------------------------------------

        private void BuildGeneralTab(TabPage tab)
        {
            int y = 16;
            const int gap = 14;
            bool isAdmin = ElevationHelper.IsAdministrator();

            // --- Admin status badge ---
            var badgeText = isAdmin ? "Running as Administrator" : "Not running as Administrator";
            var badgeColor = isAdmin ? Color.FromArgb(0, 128, 0) : Color.FromArgb(180, 60, 0);
            var lblAdmin = new Label
            {
                Text = (isAdmin ? "\u2713 " : "\u26a0 ") + badgeText,
                Location = new Point(16, y),
                AutoSize = true,
                ForeColor = badgeColor,
                Font = new Font(Font, FontStyle.Bold)
            };
            tab.Controls.Add(lblAdmin);
            y += 24;

            if (!isAdmin)
            {
                var btnElevate = new Button
                {
                    Text = "Restart as Administrator",
                    Location = new Point(16, y),
                    Width = 190,
                    Height = 26
                };
                btnElevate.Click += (s, e) => ElevationHelper.RestartElevated();
                tab.Controls.Add(btnElevate);
                y += 34;
            }

            y += gap;

            // --- Start with Windows ---
            _chkStartup = new CheckBox
            {
                Text = "Start with Windows",
                Location = new Point(16, y),
                AutoSize = true,
                Enabled = isAdmin
            };
            tab.Controls.Add(_chkStartup);
            y += 22;

            if (!isAdmin)
            {
                // Note explaining why the checkbox is disabled
                var lblNote = new Label
                {
                    Text = "Requires administrator privileges (uses Task Scheduler for elevated autostart)",
                    Location = new Point(36, y),
                    AutoSize = false,
                    Width = tab.Width - 56,
                    Height = 30,
                    ForeColor = Color.Gray,
                    Font = new Font(Font.FontFamily, Font.Size - 0.5f, FontStyle.Italic),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };
                tab.Controls.Add(lblNote);
                y += 34;
            }
            else
            {
                var lblNote = new Label
                {
                    Text = "Creates a Task Scheduler entry that launches elevated at logon (no UAC prompt on boot)",
                    Location = new Point(36, y),
                    AutoSize = false,
                    Width = tab.Width - 56,
                    Height = 30,
                    ForeColor = Color.Gray,
                    Font = new Font(Font.FontFamily, Font.Size - 0.5f, FontStyle.Italic),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };
                tab.Controls.Add(lblNote);
                y += 34;
            }

            y += gap;

            // --- Error log path ---
            tab.Controls.Add(MakeLabel("Error log path:", 16, y, 100));
            y += 20;

            _txtLogPath = new TextBox
            {
                Location = new Point(16, y),
                Width = tab.Width - 120,
                Height = 22,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tab.Controls.Add(_txtLogPath);

            _btnBrowseLog = new Button
            {
                Text = "Browse...",
                Location = new Point(tab.Width - 96, y - 1),
                Width = 80,
                Height = 24,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnBrowseLog.Click += BtnBrowseLog_Click;
            tab.Controls.Add(_btnBrowseLog);
        }

        // ------------------------------------------------------------------
        // Load / save config
        // ------------------------------------------------------------------

        private void LoadConfig()
        {
            // Devices list box
            _deviceList.Items.Clear();
            if (_config.Devices != null)
            {
                foreach (var d in _config.Devices)
                    _deviceList.Items.Add(d.DisplayName ?? "Unnamed Device");
            }

            // General tab
            _chkStartup.Checked = StartupManager.IsEnabled();
            _txtLogPath.Text = _config.General?.ErrorLogPath ?? "";

            // Clear detail panel
            ClearDeviceDetail();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            // Flush any edits for the currently selected device
            CommitCurrentDeviceEdits();

            // Update general settings
            if (_config.General == null)
                _config.General = new GeneralSettings();

            _config.General.ErrorLogPath = _txtLogPath.Text.Trim();

            // Startup manager (Task Scheduler when admin, registry fallback otherwise)
            if (_chkStartup.Enabled) // checkbox is only enabled when running as admin
            {
                bool startupWanted = _chkStartup.Checked;
                bool startupEnabled = StartupManager.IsEnabled();
                if (startupWanted && !startupEnabled)
                {
                    if (!StartupManager.Enable())
                        MessageBox.Show("Could not create the startup task. Make sure the app is running as Administrator.",
                            "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (!startupWanted && startupEnabled)
                {
                    StartupManager.Disable();
                }
            }

            ConfigManager.Save(_config);

            MessageBox.Show("Settings saved. Restart required for changes to take effect.",
                "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------------
        // Device list events
        // ------------------------------------------------------------------

        private void DeviceList_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Commit edits for the previously selected device before switching
            if (_selectedDeviceIndex >= 0 && _selectedDeviceIndex < _config.Devices.Count)
                CommitCurrentDeviceEdits();

            _selectedDeviceIndex = _deviceList.SelectedIndex;

            if (_selectedDeviceIndex < 0 || _selectedDeviceIndex >= _config.Devices.Count)
            {
                ClearDeviceDetail();
                return;
            }

            PopulateDeviceDetail(_config.Devices[_selectedDeviceIndex]);
            _deviceDetailPanel.Enabled = true;
        }

        private void BtnAddDevice_Click(object sender, EventArgs e)
        {
            // Commit current before adding
            if (_selectedDeviceIndex >= 0 && _selectedDeviceIndex < _config.Devices.Count)
                CommitCurrentDeviceEdits();

            var newDevice = new DeviceConfig { DisplayName = "New Device" };
            _config.Devices.Add(newDevice);

            _deviceList.Items.Add(newDevice.DisplayName);
            _deviceList.SelectedIndex = _deviceList.Items.Count - 1;
        }

        private void BtnRemoveDevice_Click(object sender, EventArgs e)
        {
            int idx = _deviceList.SelectedIndex;
            if (idx < 0) return;

            var result = MessageBox.Show("Remove the selected device and all its rules?",
                "Confirm Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;

            _config.Devices.RemoveAt(idx);
            _deviceList.Items.RemoveAt(idx);
            _selectedDeviceIndex = -1;
            ClearDeviceDetail();
        }

        // ------------------------------------------------------------------
        // Rule events
        // ------------------------------------------------------------------

        private void BtnAddRule_Click(object sender, EventArgs e)
        {
            if (_selectedDeviceIndex < 0) return;

            var newRule = new RuleConfig { Name = "New Rule" };
            using (var dlg = new RuleEditorDialog(newRule))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _config.Devices[_selectedDeviceIndex].Rules.Add(dlg.Result);
                    RefreshRuleList(_config.Devices[_selectedDeviceIndex]);
                }
            }
        }

        private void BtnEditRule_Click(object sender, EventArgs e)
        {
            if (_selectedDeviceIndex < 0 || _ruleList.SelectedItems.Count == 0) return;

            int ruleIdx = _ruleList.SelectedItems[0].Index;
            var rule = _config.Devices[_selectedDeviceIndex].Rules[ruleIdx];

            using (var dlg = new RuleEditorDialog(rule))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _config.Devices[_selectedDeviceIndex].Rules[ruleIdx] = dlg.Result;
                    RefreshRuleList(_config.Devices[_selectedDeviceIndex]);
                }
            }
        }

        private void BtnRemoveRule_Click(object sender, EventArgs e)
        {
            if (_selectedDeviceIndex < 0 || _ruleList.SelectedItems.Count == 0) return;

            int ruleIdx = _ruleList.SelectedItems[0].Index;
            _config.Devices[_selectedDeviceIndex].Rules.RemoveAt(ruleIdx);
            RefreshRuleList(_config.Devices[_selectedDeviceIndex]);
        }

        // ------------------------------------------------------------------
        // General tab events
        // ------------------------------------------------------------------

        private void BtnBrowseLog_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Select error log file",
                Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = Path.GetFileName(_txtLogPath.Text),
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

        private void PopulateDeviceDetail(DeviceConfig device)
        {
            _txtName.Text = device.DisplayName ?? "";
            _txtEmail.Text = device.Email ?? "";
            _txtPassword.Text = device.Password ?? "";
            _txtSn.Text = device.SerialNumber ?? "";

            RefreshRuleList(device);
        }

        private void RefreshRuleList(DeviceConfig device)
        {
            _ruleList.Items.Clear();
            if (device.Rules == null) return;

            foreach (var rule in device.Rules)
            {
                var item = new ListViewItem(rule.Name ?? "");
                item.SubItems.Add(rule.Trigger?.Type.ToString() ?? "");
                item.SubItems.Add(rule.Enabled ? "Yes" : "No");
                _ruleList.Items.Add(item);
            }
        }

        private void CommitCurrentDeviceEdits()
        {
            if (_selectedDeviceIndex < 0 || _selectedDeviceIndex >= _config.Devices.Count)
                return;

            var device = _config.Devices[_selectedDeviceIndex];
            device.DisplayName = _txtName.Text.Trim();
            device.Email = _txtEmail.Text.Trim();
            device.Password = _txtPassword.Text;
            device.SerialNumber = _txtSn.Text.Trim();

            // Keep display name in sync
            _deviceList.Items[_selectedDeviceIndex] = device.DisplayName.Length > 0
                ? device.DisplayName
                : "Unnamed Device";
        }

        private void ClearDeviceDetail()
        {
            _txtName.Text = "";
            _txtEmail.Text = "";
            _txtPassword.Text = "";
            _txtSn.Text = "";
            _ruleList.Items.Clear();
            _deviceDetailPanel.Enabled = false;
            _selectedDeviceIndex = -1;
        }

        private static Label MakeLabel(string text, int x, int y, int width)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y + 3),
                Width = width,
                AutoSize = false
            };
        }

        private static TextBox MakeTextBox(int x, int y, int width, int height)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Width = Math.Max(width, 60),
                Height = height
            };
        }
    }
}
