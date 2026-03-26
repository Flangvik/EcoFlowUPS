using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EcoFlowMonitor.Config;
using EcoFlowMonitor.Core;
using PowerStatus = EcoFlowMonitor.Core.PowerStatus;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.UI
{
    public class MainForm : Form
    {
        private readonly AppConfig              _config;
        private readonly TrayApplicationContext _ctx;

        // Left panel — device list
        private Panel _devicePanel;

        // Live stats labels
        private Panel _statsContainer;
        private Label _lblNoSelection;
        private Panel _statsPanel;
        private Label _lblBattery;
        private Label _lblStatus;
        private Label _lblStatusSub;
        private Label _lblIn;
        private Label _lblOut;
        private Label _lblRemain;
        private Label _lblTemp;
        private Label _lblUpdated;
        private Label _lblConnected;
        private Label _lblVoltage;
        private Label _lblCycles;
        private Label _lblHealth;
        private Label _lblBatts;
        private Label _lblSolar;
        private Label _lblAcHz;
        private Label _lblUsb;
        private Label _lblUpsMode;
        private Label _lblFan;
        private Label _lblCellSpread;

        // Battery bar + power chart
        private Panel _battBar;
        private Panel _powerChart;
        private readonly List<(int inW, int outW)> _powerHistory = new List<(int, int)>();
        private const int MaxHistory = 60;
        private int         _currentBattPct     = 0;
        private PowerStatus _currentPowerStatus = PowerStatus.Unknown;

        // Rules
        private ListView _lstRules;
        private Button   _btnAddRule;
        private Button   _btnEditRule;
        private Button   _btnRemoveRule;

        private int _selectedDeviceIdx = -1;

        private readonly System.Windows.Forms.Timer _refreshTimer;
        private System.Windows.Forms.Timer _connectingTimer;
        private int _connectingFrame = 0;

        public MainForm(AppConfig config, TrayApplicationContext ctx)
        {
            Logger.Log("MainForm: constructor start");
            _config = config;
            _ctx    = ctx;

            InitializeComponent();
            ThemeManager.Apply(this);
            StyleAccentButton(_btnAddRule);

            ShowNoSelection();

            this.Shown += (s, e) =>
            {
                Logger.Log("MainForm: Shown event fired, calling PopulateDeviceList");
                PopulateDeviceList();
            };

            _ctx.DeviceUpdated += OnDeviceUpdated;

            _connectingTimer          = new System.Windows.Forms.Timer { Interval = 750 };
            _connectingTimer.Tick    += (s, e) =>
            {
                _connectingFrame = (_connectingFrame + 1) % 4;
                if (_lblStatus != null && _lblStatus.Text.StartsWith("Connecting"))
                    _lblStatus.Text = "Connecting" + new string('.', _connectingFrame);
            };

            _refreshTimer       = new System.Windows.Forms.Timer { Interval = 5000 };
            _refreshTimer.Tick += (s, e) =>
            {
                RefreshDeviceDots();
                if (_selectedDeviceIdx >= 0 && _selectedDeviceIdx < _config.Devices.Count)
                {
                    var dev    = _config.Devices[_selectedDeviceIdx];
                    var states = _ctx.GetLiveStates();
                    DeviceState live = null;
                    foreach (var (cfg, state) in states)
                        if (cfg.SerialNumber == dev.SerialNumber) { live = state; break; }
                    UpdateLiveStats(dev, live);
                }
            };
            _refreshTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _ctx.DeviceUpdated -= OnDeviceUpdated;
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _connectingTimer.Stop();
            _connectingTimer.Dispose();
            base.OnFormClosed(e);
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private void InitializeComponent()
        {
            this.Text            = "EcoFlow Monitor";
            this.Size            = new Size(960, 780);
            this.MinimumSize     = new Size(780, 600);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.AutoScaleMode   = AutoScaleMode.Dpi;

            var split = new SplitContainer
            {
                Dock          = DockStyle.Fill,
                Orientation   = Orientation.Vertical,
                SplitterWidth = 1
            };
            this.Load += (s, e) =>
            {
                split.Panel1MinSize = 180;
                split.Panel2MinSize = 480;
                if (split.Width > 680) split.SplitterDistance = 230;
            };

            BuildLeftPanel(split.Panel1);
            BuildRightPanel(split.Panel2);
            this.Controls.Add(split);
        }

        // ------------------------------------------------------------------
        // Left: device list
        // ------------------------------------------------------------------

        private void BuildLeftPanel(SplitterPanel panel)
        {
            var table = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                RowCount    = 3,
                ColumnCount = 1,
                Padding     = Padding.Empty,
                Margin      = Padding.Empty,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // row 0: header
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // row 1: device list
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // row 2: button bar

            // Row 0 — header
            var header = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 8, 0) };
            var title  = new Label
            {
                Text      = "MY DEVICES",
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ThemeManager.SubText,
                TextAlign = ContentAlignment.MiddleLeft
            };
            header.Controls.Add(title);
            table.Controls.Add(header, 0, 0);

            // Row 1 — device list
            _devicePanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = ThemeManager.Surface };
            _devicePanel.SizeChanged += (s, e) => RelayoutDevices();
            table.Controls.Add(_devicePanel, 0, 1);

            // Row 2 — button bar
            var btnBar  = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6) };
            var btnFlow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false
            };
            var btnRefresh  = new Button { Text = "↻ Refresh",  Width = 82, Height = 28, Margin = new Padding(0, 0, 6, 0), FlatStyle = FlatStyle.Flat };
            var btnSettings = new Button { Text = "⚙ Settings", Width = 86, Height = 28, Margin = new Padding(0),          FlatStyle = FlatStyle.Flat };
            btnRefresh.Click += async (s, e) =>
            {
                var btn = (Button)s;
                btn.Enabled = false;
                btn.Text    = "…";
                bool ok = await _ctx.RefreshDevicesAsync();
                PopulateDeviceList();
                btn.Enabled = true;
                btn.Text    = "↻ Refresh";
                if (!ok)
                    MessageBox.Show("Could not reach EcoFlow servers.\nCheck your internet connection.",
                        "Refresh Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            btnSettings.Click += (s, e) => OpenSettings();
            btnFlow.Controls.Add(btnRefresh);
            btnFlow.Controls.Add(btnSettings);
            btnBar.Controls.Add(btnFlow);
            table.Controls.Add(btnBar, 0, 2);

            panel.Controls.Add(table);
        }

        // ------------------------------------------------------------------
        // Right: live stats + rules
        // ------------------------------------------------------------------

        private void BuildRightPanel(SplitterPanel panel)
        {
            var inner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 12) };

            // NOTE: In WinForms, last-added DockStyle.Top control appears topmost.
            // All controls are created first, then added in reverse visual order.

            // ── Rule buttons (Bottom) ────────────────────────────────────────
            var ruleBtnFlow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Bottom,
                Height        = 38,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                Padding       = new Padding(0, 6, 0, 0)
            };
            _btnEditRule   = new Button { Text = "Edit",   Width = 70, Height = 26, Margin = new Padding(0, 0, 6, 0), FlatStyle = FlatStyle.Flat };
            _btnRemoveRule = new Button { Text = "Remove", Width = 76, Height = 26, Margin = new Padding(0),          FlatStyle = FlatStyle.Flat };
            _btnEditRule.Click   += BtnEditRule_Click;
            _btnRemoveRule.Click += BtnRemoveRule_Click;
            ruleBtnFlow.Controls.Add(_btnEditRule);
            ruleBtnFlow.Controls.Add(_btnRemoveRule);

            // ── Stats container (Top) ────────────────────────────────────────
            _statsContainer = new Panel { Dock = DockStyle.Top, Height = 430 };
            _lblNoSelection = new Label
            {
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text      = "Select a device from the list to see live status.",
                ForeColor = ThemeManager.SubText,
                Font      = new Font("Segoe UI", 10, FontStyle.Italic)
            };
            _statsContainer.Controls.Add(_lblNoSelection);
            _statsPanel         = BuildStatsPanel();
            _statsPanel.Visible = false;
            _statsContainer.Controls.Add(_statsPanel);

            // ── Rules header (Top) ───────────────────────────────────────────
            var rulesHeader = new Panel { Dock = DockStyle.Top, Height = 34 };
            var rulesTitle  = new Label
            {
                Text      = "RULES",
                Location  = new Point(0, 8),
                AutoSize  = true,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ThemeManager.SubText
            };
            _btnAddRule = new Button
            {
                Text      = "+ Add Rule",
                Width     = 96,
                Height    = 26,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat
            };
            _btnAddRule.Location = new Point(rulesHeader.Width - _btnAddRule.Width, 4);
            _btnAddRule.Anchor   = AnchorStyles.Top | AnchorStyles.Right;
            _btnAddRule.Click   += BtnAddRule_Click;
            rulesHeader.Controls.Add(rulesTitle);
            rulesHeader.Controls.Add(_btnAddRule);
            rulesHeader.Resize += (s, e) => _btnAddRule.Left = rulesHeader.Width - _btnAddRule.Width;

            // ── Rules list (Fill) ────────────────────────────────────────────
            _lstRules = new ListView
            {
                Dock          = DockStyle.Fill,
                View          = View.Details,
                FullRowSelect = true,
                GridLines     = false,
                MultiSelect   = false,
                BorderStyle   = BorderStyle.None,
                HeaderStyle   = ColumnHeaderStyle.Nonclickable
            };
            _lstRules.Columns.Add("Name",    120);
            _lstRules.Columns.Add("Trigger", 150);
            _lstRules.Columns.Add("Enabled",  65);
            _lstRules.DoubleClick += BtnEditRule_Click;
            ThemeManager.ApplyListView(_lstRules);
            _lstRules.Resize += (s, e) =>
            {
                if (_lstRules.Columns.Count < 3) return;
                int used = _lstRules.Columns[1].Width + _lstRules.Columns[2].Width
                         + SystemInformation.VerticalScrollBarWidth;
                _lstRules.Columns[0].Width = Math.Max(60, _lstRules.ClientSize.Width - used);
            };

            // ── Add in reverse visual order (last-added Top = topmost) ───────
            // Visual order top→bottom: LIVE STATUS | stats | spacer | RULES | list | buttons
            inner.Controls.Add(ruleBtnFlow);                      // Bottom → bottom edge
            inner.Controls.Add(_lstRules);                        // Fill   → remaining space
            inner.Controls.Add(rulesHeader);                      // Top    → innermost (just above fill)
            inner.Controls.Add(MakeSpacer(8));                    // Top    → above rules header
            inner.Controls.Add(_statsContainer);                  // Top    → above spacer
            inner.Controls.Add(MakeSectionHeader("LIVE STATUS")); // Top    → topmost (added last)

            panel.Controls.Add(inner);
        }

        private Panel BuildStatsPanel()
        {
            var p = new Panel { Dock = DockStyle.Fill };

            // Row 1 — large battery % + status/device info
            var row1 = new TableLayoutPanel
            {
                Dock        = DockStyle.Top,
                Height      = 90,
                ColumnCount = 2,
                RowCount    = 1
            };
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row1.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var battCell = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
            _lblBattery = new Label
            {
                Text      = "--",
                Dock      = DockStyle.Top,
                Height    = 48,
                Font      = new Font("Segoe UI", 30, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var pctLabel = new Label
            {
                Text      = "battery",
                Dock      = DockStyle.Top,
                Height    = 18,
                ForeColor = ThemeManager.SubText,
                Font      = new Font("Segoe UI", 8),
                TextAlign = ContentAlignment.TopLeft
            };
            battCell.Controls.Add(pctLabel);
            battCell.Controls.Add(_lblBattery);
            row1.Controls.Add(battCell, 0, 0);

            var statusCell = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 14, 0, 0) };
            _lblStatus = new Label
            {
                Text      = "—",
                Dock      = DockStyle.Top,
                Height    = 36,
                Font      = new Font("Segoe UI", 18, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _lblConnected = new Label
            {
                Text      = "",
                Dock      = DockStyle.Top,
                Height    = 18,
                ForeColor = ThemeManager.SubText,
                Font      = new Font("Segoe UI", 8),
                TextAlign = ContentAlignment.TopLeft
            };
            _lblStatusSub = new Label
            {
                Text      = "",
                Dock      = DockStyle.Top,
                Height    = 18,
                ForeColor = ThemeManager.SubText,
                Font      = new Font("Segoe UI", 8),
                TextAlign = ContentAlignment.TopLeft
            };
            statusCell.Controls.Add(_lblStatusSub);
            statusCell.Controls.Add(_lblConnected);
            statusCell.Controls.Add(_lblStatus);
            row1.Controls.Add(statusCell, 1, 0);
            p.Controls.Add(row1);

            // Battery fill bar
            _battBar = new Panel { Dock = DockStyle.Top, Height = 14 };
            _battBar.Paint += DrawBattBar;
            p.Controls.Add(_battBar);

            // Row 2 — power flow (INPUT / SOLAR / OUTPUT / REMAINING / UPDATED)
            var row2 = new TableLayoutPanel
            {
                Dock        = DockStyle.Top,
                Height      = 72,
                ColumnCount = 5,
                RowCount    = 1,
                Padding     = new Padding(0, 6, 0, 0)
            };
            for (int i = 0; i < 5; i++)
                row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            row2.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _lblIn      = AddStatCell(row2, 0, "INPUT");
            _lblSolar   = AddStatCell(row2, 1, "SOLAR");
            _lblOut     = AddStatCell(row2, 2, "OUTPUT");
            _lblRemain  = AddStatCell(row2, 3, "REMAINING");
            _lblUpdated = AddStatCell(row2, 4, "UPDATED");
            p.Controls.Add(row2);

            // Row 3 — health stats (VOLTAGE / TEMP / CYCLES / HEALTH / BATTS)
            var row3 = new TableLayoutPanel
            {
                Dock        = DockStyle.Top,
                Height      = 72,
                ColumnCount = 5,
                RowCount    = 1,
                Padding     = new Padding(0, 6, 0, 0)
            };
            for (int i = 0; i < 5; i++)
                row3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            row3.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _lblVoltage = AddStatCell(row3, 0, "VOLTAGE");
            _lblTemp    = AddStatCell(row3, 1, "TEMP");
            _lblCycles  = AddStatCell(row3, 2, "CYCLES");
            _lblHealth  = AddStatCell(row3, 3, "HEALTH");
            _lblBatts   = AddStatCell(row3, 4, "BATTS CONN.");
            p.Controls.Add(row3);

            // Row 4 — detail stats (AC HZ / USB OUT / UPS MODE / FAN / CELL ΔmV)
            var row4 = new TableLayoutPanel
            {
                Dock        = DockStyle.Top,
                Height      = 56,
                ColumnCount = 5,
                RowCount    = 1,
                Padding     = new Padding(0, 4, 0, 0)
            };
            for (int i = 0; i < 5; i++)
                row4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            row4.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _lblAcHz      = AddSmallStatCell(row4, 0, "AC HZ");
            _lblUsb       = AddSmallStatCell(row4, 1, "USB OUT");
            _lblUpsMode   = AddSmallStatCell(row4, 2, "UPS MODE");
            _lblFan       = AddSmallStatCell(row4, 3, "FAN");
            _lblCellSpread = AddSmallStatCell(row4, 4, "CELL ΔmV");
            p.Controls.Add(row4);

            // Power history sparkline chart
            _powerChart = new Panel { Dock = DockStyle.Top, Height = 106 };
            _powerChart.Paint += DrawPowerChart;
            p.Controls.Add(_powerChart);

            return p;
        }

        private Label AddStatCell(TableLayoutPanel table, int col, string caption)
        {
            var cell = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
            var val  = new Label
            {
                Text      = "--",
                Dock      = DockStyle.Top,
                Height    = 30,
                Font      = new Font("Segoe UI", 13, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var cap = new Label
            {
                Text      = caption,
                Dock      = DockStyle.Top,
                Height    = 16,
                ForeColor = ThemeManager.SubText,
                Font      = new Font("Segoe UI", 7, FontStyle.Bold),
                TextAlign = ContentAlignment.TopLeft
            };
            cell.Controls.Add(val);
            cell.Controls.Add(cap);
            table.Controls.Add(cell, col, 0);
            return val;
        }

        private Label AddSmallStatCell(TableLayoutPanel table, int col, string caption)
        {
            var cell = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 3, 0, 0) };
            var val  = new Label
            {
                Text      = "--",
                Dock      = DockStyle.Top,
                Height    = 24,
                Font      = new Font("Segoe UI", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var cap = new Label
            {
                Text      = caption,
                Dock      = DockStyle.Top,
                Height    = 14,
                ForeColor = ThemeManager.SubText,
                Font      = new Font("Segoe UI", 6.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.TopLeft
            };
            cell.Controls.Add(val);
            cell.Controls.Add(cap);
            table.Controls.Add(cell, col, 0);
            return val;
        }

        // ------------------------------------------------------------------
        // Battery bar paint
        // ------------------------------------------------------------------

        private void DrawBattBar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ((Panel)sender).ClientRectangle;
            g.Clear(ThemeManager.ControlBg);
            using (var pen = new Pen(ThemeManager.Border))
                g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            if (_currentBattPct > 0)
            {
                int fillW = Math.Max(1, (int)((r.Width - 2) * _currentBattPct / 100.0));
                using (var brush = new SolidBrush(BatteryColor(_currentBattPct, _currentPowerStatus)))
                    g.FillRectangle(brush, 1, 1, fillW, r.Height - 2);
            }
        }

        // ------------------------------------------------------------------
        // Power history sparkline paint
        // ------------------------------------------------------------------

        private void DrawPowerChart(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ((Panel)sender).ClientRectangle;
            g.Clear(ThemeManager.ControlBg);

            const int leftMargin = 42;
            const int top        = 16;
            int       bottom     = r.Height - 4;
            int       chartH     = bottom - top;
            int       chartW     = r.Width - leftMargin - 2;

            // Caption + legend
            using (var captionFont  = new Font("Segoe UI", 7, FontStyle.Bold))
            using (var captionBrush = new SolidBrush(ThemeManager.SubText))
                g.DrawString("POWER HISTORY", captionFont, captionBrush, leftMargin + 2, 2);

            using (var font  = new Font("Segoe UI", 7))
            using (var brush = new SolidBrush(ThemeManager.SubText))
            {
                g.FillRectangle(new SolidBrush(Color.FromArgb(40, 200, 100)),  r.Width - 82, 3, 8, 8);
                g.DrawString("IN",  font, brush, r.Width - 72, 2);
                g.FillRectangle(new SolidBrush(Color.FromArgb(220, 140, 40)), r.Width - 48, 3, 8, 8);
                g.DrawString("OUT", font, brush, r.Width - 38, 2);
            }

            // Determine scale
            int rawMax = _powerHistory.Count > 0
                ? _powerHistory.Max(x => Math.Max(x.inW, x.outW))
                : 0;
            int scale = NiceScale(rawMax);
            int mid   = scale / 2;

            // Y-axis labels + gridlines
            using (var labelFont  = new Font("Segoe UI", 6.5f))
            using (var labelBrush = new SolidBrush(ThemeManager.SubText))
            using (var gridPen    = new Pen(Color.FromArgb(40, ThemeManager.Border.R, ThemeManager.Border.G, ThemeManager.Border.B)))
            {
                // top gridline
                g.DrawLine(gridPen, leftMargin, top, r.Width - 2, top);
                var szMax = g.MeasureString($"{scale}W", labelFont);
                g.DrawString($"{scale}W", labelFont, labelBrush,
                    leftMargin - szMax.Width - 2, top - szMax.Height / 2);

                // mid gridline
                int yMid = top + chartH / 2;
                g.DrawLine(gridPen, leftMargin, yMid, r.Width - 2, yMid);
                var szMid = g.MeasureString($"{mid}W", labelFont);
                g.DrawString($"{mid}W", labelFont, labelBrush,
                    leftMargin - szMid.Width - 2, yMid - szMid.Height / 2);

                // bottom gridline
                g.DrawLine(gridPen, leftMargin, bottom, r.Width - 2, bottom);
                var sz0 = g.MeasureString("0W", labelFont);
                g.DrawString("0W", labelFont, labelBrush,
                    leftMargin - sz0.Width - 2, bottom - sz0.Height / 2);
            }

            if (_powerHistory.Count < 2) return;

            float xStep = (float)chartW / (_powerHistory.Count - 1);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // IN line — green
            using (var pen = new Pen(Color.FromArgb(40, 200, 100), 1.5f))
            {
                var pts = new PointF[_powerHistory.Count];
                for (int i = 0; i < _powerHistory.Count; i++)
                    pts[i] = new PointF(leftMargin + i * xStep,
                        bottom - (float)_powerHistory[i].inW / scale * chartH);
                g.DrawLines(pen, pts);
            }

            // OUT line — orange
            using (var pen = new Pen(Color.FromArgb(220, 140, 40), 1.5f))
            {
                var pts = new PointF[_powerHistory.Count];
                for (int i = 0; i < _powerHistory.Count; i++)
                    pts[i] = new PointF(leftMargin + i * xStep,
                        bottom - (float)_powerHistory[i].outW / scale * chartH);
                g.DrawLines(pen, pts);
            }
        }

        /// <summary>Rounds <paramref name="value"/> up to a human-friendly scale value.</summary>
        private static int NiceScale(int value)
        {
            if (value <= 0)   return 100;
            int[] steps = { 10, 25, 50, 100, 150, 200, 250, 300, 400, 500,
                            600, 750, 1000, 1250, 1500, 2000, 2500, 3000, 5000 };
            foreach (int s in steps)
                if (value <= s) return s;
            return ((value + 999) / 1000) * 1000;
        }

        // ------------------------------------------------------------------
        // Device list — Label rows
        // ------------------------------------------------------------------

        private void PopulateDeviceList()
        {
            Logger.Log($"PopulateDeviceList: devices={_config.Devices.Count}, panel.ClientSize={_devicePanel.ClientSize}");
            _devicePanel.SuspendLayout();
            _devicePanel.Controls.Clear();

            var liveStates = _ctx.GetLiveStates();
            int w = Math.Max(20, _devicePanel.ClientSize.Width);

            for (int i = 0; i < _config.Devices.Count; i++)
            {
                var dev = _config.Devices[i];
                bool connected = liveStates.Any(x => x.Config.SerialNumber == dev.SerialNumber && x.State.IsConnected);
                int capturedIdx = i;
                var row = new Label
                {
                    Text      = (connected ? "● " : "○ ") + (dev.DisplayName ?? "Unnamed"),
                    Location  = new Point(0, i * 36),
                    Size      = new Size(w, 36),
                    Font      = new Font("Segoe UI", 10),
                    ForeColor = ThemeManager.Foreground,
                    BackColor = ThemeManager.Surface,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding   = new Padding(12, 0, 0, 0),
                    Cursor    = Cursors.Hand,
                    Tag       = capturedIdx
                };
                row.Click      += (s, e) => SelectDeviceRow(capturedIdx);
                row.MouseEnter += (s, e) => { if ((int)row.Tag != _selectedDeviceIdx) row.BackColor = ThemeManager.ControlBg; };
                row.MouseLeave += (s, e) => { row.BackColor = (int)row.Tag == _selectedDeviceIdx ? ThemeManager.MenuHover : ThemeManager.Surface; };
                _devicePanel.Controls.Add(row);
                Logger.Log($"  row[{i}] Text='{row.Text}' Bounds={row.Bounds} ForeColor={row.ForeColor} BackColor={row.BackColor} Visible={row.Visible}");
            }

            Logger.Log($"PopulateDeviceList: added {_config.Devices.Count} row(s), panel.Bounds={_devicePanel.Bounds}");
            _devicePanel.ResumeLayout(true);
            _devicePanel.Invalidate(true);
            _devicePanel.Update();

            // Auto-select first device if nothing is selected yet
            if (_config.Devices.Count > 0 && _selectedDeviceIdx < 0)
                SelectDeviceRow(0);
        }

        private void RelayoutDevices()
        {
            int w = Math.Max(20, _devicePanel.ClientSize.Width);
            foreach (Control c in _devicePanel.Controls)
                if (c is Label) c.Width = w;
        }

        private void SelectDeviceRow(int idx)
        {
            _selectedDeviceIdx = idx;
            foreach (Control c in _devicePanel.Controls)
                if (c is Label lbl && lbl.Tag != null)
                    lbl.BackColor = (int)lbl.Tag == idx ? ThemeManager.MenuHover : ThemeManager.Surface;
            SelectDevice(idx);
        }

        private void RefreshDeviceDots()
        {
            if (_devicePanel.Controls.Count != _config.Devices.Count)
            {
                PopulateDeviceList();
                return;
            }

            var liveStates = _ctx.GetLiveStates();
            foreach (Control c in _devicePanel.Controls)
            {
                if (!(c is Label lbl) || lbl.Tag == null) continue;
                int idx = (int)lbl.Tag;
                if (idx >= _config.Devices.Count) continue;
                var dev = _config.Devices[idx];
                bool connected = liveStates.Any(x => x.Config.SerialNumber == dev.SerialNumber && x.State.IsConnected);
                lbl.Text = (connected ? "● " : "○ ") + (dev.DisplayName ?? "Unnamed");
            }
        }

        // ------------------------------------------------------------------
        // Device selection
        // ------------------------------------------------------------------

        private void SelectDevice(int idx)
        {
            _selectedDeviceIdx = idx;

            if (idx < 0 || idx >= _config.Devices.Count)
            {
                ShowNoSelection();
                _lstRules.Items.Clear();
                return;
            }

            var dev    = _config.Devices[idx];
            var states = _ctx.GetLiveStates();
            DeviceState live = null;
            foreach (var (cfg, state) in states)
                if (cfg.SerialNumber == dev.SerialNumber) { live = state; break; }

            UpdateLiveStats(dev, live);
            RefreshRuleList(dev);
        }

        // ------------------------------------------------------------------
        // Live stats update
        // ------------------------------------------------------------------

        private void OnDeviceUpdated(object sender, TrayApplicationContext.DeviceStateEventArgs e)
        {
            if (_selectedDeviceIdx < 0 || _selectedDeviceIdx >= _config.Devices.Count) return;
            var selected = _config.Devices[_selectedDeviceIdx];
            if (selected.SerialNumber != e.Config.SerialNumber) return;

            UpdateLiveStats(e.Config, e.State);
            RefreshDeviceDots();
        }

        private void UpdateLiveStats(DeviceConfig dev, DeviceState state)
        {
            _lblNoSelection.Visible = false;
            _statsPanel.Visible     = true;

            if (state == null || !state.IsConnected)
            {
                bool isConnecting = state != null; // state exists but not yet connected

                _lblBattery.Text      = "--";
                _lblBattery.ForeColor = ThemeManager.SubText;
                _lblStatus.Text       = isConnecting ? "Connecting" : "No Data";
                _lblStatus.ForeColor  = isConnecting
                    ? Color.FromArgb(220, 170, 40)
                    : ThemeManager.SubText;
                _lblConnected.Text    = isConnecting ? "MQTT stream starting…" : "";
                _lblConnected.ForeColor = ThemeManager.SubText;
                _lblStatusSub.Text    = isConnecting ? "First data may take ~30s" : "";
                _lblStatusSub.ForeColor = ThemeManager.SubText;
                _lblIn.Text = _lblSolar.Text = _lblOut.Text = _lblRemain.Text = "--";
                _lblIn.ForeColor = _lblSolar.ForeColor = _lblOut.ForeColor = ThemeManager.SubText;
                _lblUpdated.Text   = "";
                _lblVoltage.Text   = _lblCycles.Text = _lblHealth.Text = _lblBatts.Text = "--";
                _lblAcHz.Text      = _lblUsb.Text = _lblUpsMode.Text = _lblFan.Text = _lblCellSpread.Text = "--";

                if (isConnecting) _connectingTimer.Start();
                else              _connectingTimer.Stop();

                _currentBattPct = 0;
                _battBar?.Invalidate();
                return;
            }

            _connectingTimer.Stop();

            int batt = state.Bms != null ? (int)(state.Bms.BatteryPct ?? 0) : -1;
            _lblBattery.Text      = batt >= 0 ? $"{batt}%" : "--";
            _lblBattery.ForeColor = batt >= 0 ? BatteryColor(batt, state.Power.Status) : ThemeManager.SubText;

            string statusText;
            Color  statusColor;
            switch (state.Power.Status)
            {
                case PowerStatus.Charging:
                    statusText  = "⚡ Charging";
                    statusColor = Color.FromArgb(40, 200, 100);
                    break;
                case PowerStatus.PowerLost:
                    statusText  = "⚠ On Battery";
                    statusColor = Color.FromArgb(220, 100, 40);
                    break;
                case PowerStatus.Idle:
                    statusText  = "● Idle";
                    statusColor = ThemeManager.SubText;
                    break;
                default:
                    statusText  = "— Unknown";
                    statusColor = ThemeManager.SubText;
                    break;
            }
            _lblStatus.Text         = statusText;
            _lblStatus.ForeColor    = statusColor;
            _lblConnected.Text      = "● Connected";
            _lblConnected.ForeColor = Color.FromArgb(40, 200, 100);
            _lblStatusSub.Text      = dev.DisplayName;
            _lblStatusSub.ForeColor = ThemeManager.SubText;

            // Row 2 — power flow
            int inW    = state.Display?.TotalInW  ?? state.Bms?.InputW  ?? 0;
            int outW   = state.Display?.TotalOutW ?? state.Bms?.OutputW ?? 0;
            int remain = state.Bms?.RemainMin ?? -1;
            int solar  = state.Display?.SolarInHighW ?? state.Display?.SolarInLowW ?? 0;

            _lblIn.Text      = $"{inW} W";
            _lblOut.Text     = $"{outW} W";
            _lblSolar.Text   = solar > 0 ? $"{solar} W" : "--";
            _lblIn.ForeColor    = inW   > 0 ? Color.FromArgb(40, 200, 100) : ThemeManager.Foreground;
            _lblOut.ForeColor   = outW  > 0 ? Color.FromArgb(220, 140, 40) : ThemeManager.Foreground;
            _lblSolar.ForeColor = solar > 0 ? Color.FromArgb(255, 215, 0)  : ThemeManager.SubText;
            _lblRemain.Text  = remain >= 0 ? FormatRemain(remain) : "--";
            _lblUpdated.Text = state.LastUpdated == default ? "--" : state.LastUpdated.ToString("HH:mm:ss");

            // Row 3 — battery health
            _lblVoltage.Text = state.Bms?.VoltageV.HasValue == true ? $"{state.Bms.VoltageV:0.0} V" : "--";
            _lblTemp.Text    = state.Bms?.TempC.HasValue    == true ? $"{state.Bms.TempC:0}°C"      : "--";
            _lblCycles.Text  = state.Bms?.Cycles.HasValue   == true ? $"{state.Bms.Cycles}"          : "--";
            _lblHealth.Text  = state.Bms?.SohPct.HasValue   == true ? $"{state.Bms.SohPct}%"         : "--";

            // Batteries connected count from EMS
            var conn = state.Ems?.BmsConnected;
            if (conn != null && conn.Length > 0)
            {
                int connCount = 0;
                foreach (int c in conn) if (c != 0) connCount++;
                _lblBatts.Text = $"{connCount} / {conn.Length}";
                _lblBatts.ForeColor = connCount > 0 ? Color.FromArgb(40, 200, 100) : ThemeManager.SubText;
            }
            else
            {
                _lblBatts.Text      = "--";
                _lblBatts.ForeColor = ThemeManager.SubText;
            }

            // Row 4 — detail stats
            int? acHz = state.Display?.AcInFreqHz;
            _lblAcHz.Text      = acHz.HasValue && acHz > 0 ? $"{acHz} Hz" : "--";
            _lblAcHz.ForeColor = acHz.HasValue && acHz > 0 ? ThemeManager.Foreground : ThemeManager.SubText;

            int usbTotal = (state.Display?.UsbA1W ?? 0)
                         + (state.Display?.UsbA2W ?? 0)
                         + (state.Display?.UsbC1W ?? 0)
                         + (state.Display?.UsbC2W ?? 0);
            _lblUsb.Text      = usbTotal > 0 ? $"{usbTotal} W" : "--";
            _lblUsb.ForeColor = usbTotal > 0 ? Color.FromArgb(220, 140, 40) : ThemeManager.SubText;

            int? upsMode = state.Ems?.UpsMode;
            _lblUpsMode.Text      = upsMode.HasValue ? (upsMode == 0 ? "Normal" : $"UPS {upsMode}") : "--";
            _lblUpsMode.ForeColor = upsMode == 1 ? Color.FromArgb(220, 100, 40) : ThemeManager.Foreground;

            int? fan = state.Ems?.FanLevel;
            _lblFan.Text      = fan.HasValue ? (fan == 0 ? "Off" : $"Lvl {fan}") : "--";
            _lblFan.ForeColor = fan.HasValue && fan > 0 ? Color.FromArgb(40, 200, 100) : ThemeManager.SubText;

            int? maxCell = state.Bms?.MaxCellMv;
            int? minCell = state.Bms?.MinCellMv;
            if (!maxCell.HasValue && state.Bms?.CellVolsMv?.Length > 0)
            {
                int mx = state.Bms.CellVolsMv[0], mn = state.Bms.CellVolsMv[0];
                foreach (int cv in state.Bms.CellVolsMv) { if (cv > mx) mx = cv; if (cv < mn) mn = cv; }
                maxCell = mx; minCell = mn;
            }
            if (maxCell.HasValue && minCell.HasValue)
            {
                int spread = maxCell.Value - minCell.Value;
                _lblCellSpread.Text      = $"{spread} mV";
                _lblCellSpread.ForeColor = spread > 20 ? Color.FromArgb(220, 140, 40) : ThemeManager.Foreground;
            }
            else
            {
                _lblCellSpread.Text      = "--";
                _lblCellSpread.ForeColor = ThemeManager.SubText;
            }

            // Power history + chart refresh
            if (_powerHistory.Count >= MaxHistory) _powerHistory.RemoveAt(0);
            _powerHistory.Add((inW, outW));
            _currentBattPct     = batt;
            _currentPowerStatus = state.Power.Status;
            _battBar?.Invalidate();
            _powerChart?.Invalidate();
        }

        private void ShowNoSelection()
        {
            _lblNoSelection.Visible = true;
            _statsPanel.Visible     = false;
        }

        // ------------------------------------------------------------------
        // Rules
        // ------------------------------------------------------------------

        private void RefreshRuleList(DeviceConfig dev)
        {
            _lstRules.Items.Clear();
            if (dev?.Rules == null) return;

            foreach (var rule in dev.Rules)
            {
                var item = new ListViewItem(rule.Name ?? "");
                item.SubItems.Add(TriggerSummary(rule));
                item.SubItems.Add(rule.Enabled ? "Yes" : "No");
                _lstRules.Items.Add(item);
            }
        }

        private static string TriggerSummary(RuleConfig rule)
        {
            if (rule.Trigger == null) return "";
            switch (rule.Trigger.Type)
            {
                case Triggers.TriggerType.BatteryBelow:       return $"Battery < {rule.Trigger.Threshold}%";
                case Triggers.TriggerType.TimeRemainingBelow: return $"Remain < {rule.Trigger.Threshold} min";
                default:                                       return rule.Trigger.Type.ToString();
            }
        }

        private void BtnAddRule_Click(object sender, EventArgs e)
        {
            if (_selectedDeviceIdx < 0) return;
            using (var wiz = new RuleWizardForm(new RuleConfig { Name = "New Rule" }))
            {
                if (wiz.ShowDialog(this) != DialogResult.OK) return;
                _config.Devices[_selectedDeviceIdx].Rules.Add(wiz.Result);
                ConfigManager.Save(_config);
                RefreshRuleList(_config.Devices[_selectedDeviceIdx]);
            }
        }

        private void BtnEditRule_Click(object sender, EventArgs e)
        {
            if (_selectedDeviceIdx < 0 || _lstRules.SelectedItems.Count == 0) return;
            int idx  = _lstRules.SelectedItems[0].Index;
            var rule = _config.Devices[_selectedDeviceIdx].Rules[idx];
            using (var wiz = new RuleWizardForm(rule))
            {
                if (wiz.ShowDialog(this) != DialogResult.OK) return;
                _config.Devices[_selectedDeviceIdx].Rules[idx] = wiz.Result;
                ConfigManager.Save(_config);
                RefreshRuleList(_config.Devices[_selectedDeviceIdx]);
            }
        }

        private void BtnRemoveRule_Click(object sender, EventArgs e)
        {
            if (_selectedDeviceIdx < 0 || _lstRules.SelectedItems.Count == 0) return;
            if (MessageBox.Show("Remove this rule?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int idx = _lstRules.SelectedItems[0].Index;
            _config.Devices[_selectedDeviceIdx].Rules.RemoveAt(idx);
            ConfigManager.Save(_config);
            RefreshRuleList(_config.Devices[_selectedDeviceIdx]);
        }

        // ------------------------------------------------------------------
        // Settings
        // ------------------------------------------------------------------

        private void OpenSettings()
        {
            using (var form = new SettingsForm(_config))
                form.ShowDialog(this);
            ConfigManager.Save(_config);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static Color BatteryColor(int pct, PowerStatus status)
        {
            if (status == PowerStatus.PowerLost)
                return pct < 20 ? Color.FromArgb(220, 60, 60) : Color.FromArgb(220, 140, 40);
            if (pct < 20) return Color.FromArgb(220, 60, 60);
            if (pct < 50) return Color.FromArgb(220, 170, 40);
            return Color.FromArgb(40, 200, 100);
        }

        private static string FormatRemain(int minutes)
        {
            if (minutes < 60) return $"{minutes} min";
            return $"{minutes / 60}h {minutes % 60}m";
        }

        private Panel MakeSectionHeader(string title)
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 28 };
            var lbl   = new Label
            {
                Text      = title,
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ThemeManager.SubText,
                TextAlign = ContentAlignment.BottomLeft,
                Padding   = new Padding(0, 0, 0, 3)
            };
            var line = new Panel { Dock = DockStyle.Bottom, Height = 1 };
            line.Paint += (s, e) => e.Graphics.DrawLine(new Pen(ThemeManager.Border), 0, 0, line.Width, 0);
            panel.Controls.Add(lbl);
            panel.Controls.Add(line);
            return panel;
        }

        private static Panel MakeSpacer(int h) => new Panel { Dock = DockStyle.Top, Height = h };

        private static void StyleAccentButton(Button btn)
        {
            btn.BackColor = ThemeManager.MenuHover;
            btn.ForeColor = Color.White;
            if (btn.FlatAppearance != null) btn.FlatAppearance.BorderColor = ThemeManager.MenuHover;
        }
    }
}
