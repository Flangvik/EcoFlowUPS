using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EcoFlowMonitor.Actions;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.UI
{
    public class RuleEditorDialog : Form
    {
        private RuleConfig _rule;

        public RuleConfig Result => _rule;

        // ---- Rule name / enabled ----
        private TextBox _txtRuleName;
        private CheckBox _chkEnabled;

        // ---- Trigger section ----
        private ComboBox _cmbTriggerType;
        private Label _lblThreshold;
        private NumericUpDown _nudThreshold;

        // ---- Actions section ----
        private ListView _actionList;
        private Button _btnAddAction;
        private Button _btnMoveUp;
        private Button _btnMoveDown;
        private Button _btnRemoveAction;

        // ---- Dialog buttons ----
        private Button _btnOk;
        private Button _btnCancel;

        public RuleEditorDialog(RuleConfig rule)
        {
            _rule = Clone(rule);
            InitializeComponent();
            ThemeManager.Apply(this);
            LoadRule();
        }

        // ------------------------------------------------------------------
        // Deep clone
        // ------------------------------------------------------------------

        private RuleConfig Clone(RuleConfig r)
        {
            var c = new RuleConfig
            {
                Id = r.Id,
                Name = r.Name,
                Enabled = r.Enabled,
                Trigger = r.Trigger == null ? new TriggerConfig() : new TriggerConfig
                {
                    Type = r.Trigger.Type,
                    Threshold = r.Trigger.Threshold
                },
                Actions = new List<ActionConfig>()
            };

            if (r.Actions != null)
            {
                foreach (var a in r.Actions)
                {
                    c.Actions.Add(new ActionConfig
                    {
                        Type = a.Type,
                        ScriptPath = a.ScriptPath,
                        NotificationTitle = a.NotificationTitle,
                        NotificationBody = a.NotificationBody,
                        LogPath = a.LogPath,
                        LogMessage = a.LogMessage
                    });
                }
            }

            return c;
        }

        // ------------------------------------------------------------------
        // InitializeComponent
        // ------------------------------------------------------------------

        private void InitializeComponent()
        {
            this.Text = "Edit Rule";
            this.Size = new Size(560, 520);
            this.MinimumSize = new Size(480, 440);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterParent;

            int y = 12;
            const int lw = 100; // label width
            const int fh = 24;  // field height
            const int gap = 10;
            int fw = this.ClientSize.Width - lw - 36; // field width

            // ---- Rule Name ----
            this.Controls.Add(MakeLabel("Rule Name:", 12, y, lw));
            _txtRuleName = new TextBox { Location = new Point(lw + 20, y), Width = fw, Height = fh, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            this.Controls.Add(_txtRuleName);
            y += fh + gap;

            // ---- Enabled checkbox ----
            _chkEnabled = new CheckBox { Text = "Enabled", Location = new Point(lw + 20, y), AutoSize = true };
            this.Controls.Add(_chkEnabled);
            y += 26 + gap;

            // ---- Separator ----
            this.Controls.Add(MakeSeparator(12, y, this.ClientSize.Width - 24));
            y += 14;

            // ---- Trigger heading ----
            var trigHeading = MakeLabel("Trigger", 12, y, 80);
            trigHeading.Font = new Font(trigHeading.Font, FontStyle.Bold);
            this.Controls.Add(trigHeading);
            y += 22;

            // Trigger type combo
            this.Controls.Add(MakeLabel("Trigger:", 12, y, lw));
            _cmbTriggerType = new ComboBox
            {
                Location = new Point(lw + 20, y),
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbTriggerType.Items.AddRange(new object[]
            {
                TriggerType.PowerLost,
                TriggerType.PowerRestored,
                TriggerType.BatteryBelow,
                TriggerType.TimeRemainingBelow
            });
            _cmbTriggerType.SelectedIndexChanged += CmbTriggerType_SelectedIndexChanged;
            this.Controls.Add(_cmbTriggerType);
            y += fh + gap;

            // Threshold
            _lblThreshold = MakeLabel("Threshold:", 12, y, lw);
            this.Controls.Add(_lblThreshold);
            _nudThreshold = new NumericUpDown
            {
                Location = new Point(lw + 20, y),
                Width = 80,
                Height = fh,
                Minimum = 0,
                Maximum = 1440,
                DecimalPlaces = 0
            };
            this.Controls.Add(_nudThreshold);
            y += fh + gap;

            // ---- Separator ----
            this.Controls.Add(MakeSeparator(12, y, this.ClientSize.Width - 24));
            y += 14;

            // ---- Actions heading ----
            var actHeading = MakeLabel("Actions", 12, y, 80);
            actHeading.Font = new Font(actHeading.Font, FontStyle.Bold);
            this.Controls.Add(actHeading);
            y += 22;

            // Actions list
            int listHeight = this.ClientSize.Height - y - 60;
            _actionList = new ListView
            {
                Location = new Point(12, y),
                Size = new Size(this.ClientSize.Width - 120, listHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            _actionList.Columns.Add("Order", 50);
            _actionList.Columns.Add("Type", 120);
            _actionList.Columns.Add("Details", 250);
            this.Controls.Add(_actionList);

            // Action buttons (right of list)
            int bx = this.ClientSize.Width - 100;
            _btnAddAction    = new Button { Text = "+ Add",    Location = new Point(bx, y),       Width = 88, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnMoveUp       = new Button { Text = "Move Up",  Location = new Point(bx, y + 32),  Width = 88, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnMoveDown     = new Button { Text = "Move Down",Location = new Point(bx, y + 64),  Width = 88, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnRemoveAction = new Button { Text = "Remove",   Location = new Point(bx, y + 96),  Width = 88, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right };

            _btnAddAction.Click    += BtnAddAction_Click;
            _btnMoveUp.Click       += BtnMoveUp_Click;
            _btnMoveDown.Click     += BtnMoveDown_Click;
            _btnRemoveAction.Click += BtnRemoveAction_Click;

            this.Controls.Add(_btnAddAction);
            this.Controls.Add(_btnMoveUp);
            this.Controls.Add(_btnMoveDown);
            this.Controls.Add(_btnRemoveAction);

            // ---- OK / Cancel ----
            _btnOk = new Button
            {
                Text = "OK",
                Width = 80, Height = 28,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.OK
            };
            _btnOk.Click += BtnOk_Click;

            _btnCancel = new Button
            {
                Text = "Cancel",
                Width = 80, Height = 28,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.Cancel
            };

            this.Controls.Add(_btnOk);
            this.Controls.Add(_btnCancel);

            this.AcceptButton = _btnOk;
            this.CancelButton = _btnCancel;

            // Position bottom buttons on resize
            this.Resize += (s, e) => PositionBottomButtons();
            PositionBottomButtons();
        }

        private void PositionBottomButtons()
        {
            int y = this.ClientSize.Height - 38;
            _btnCancel.Location = new Point(this.ClientSize.Width - _btnCancel.Width - 12, y);
            _btnOk.Location     = new Point(this.ClientSize.Width - _btnOk.Width - _btnCancel.Width - 20, y);

            // Also reposition action buttons column
            if (_actionList != null && _btnAddAction != null)
            {
                int bx = this.ClientSize.Width - 100;
                int ay = _actionList.Top;
                _btnAddAction.Left    = bx;
                _btnMoveUp.Left       = bx;
                _btnMoveDown.Left     = bx;
                _btnRemoveAction.Left = bx;

                _actionList.Width = bx - _actionList.Left - 8;
            }
        }

        // ------------------------------------------------------------------
        // Load / save rule
        // ------------------------------------------------------------------

        private void LoadRule()
        {
            _txtRuleName.Text = _rule.Name ?? "";
            _chkEnabled.Checked = _rule.Enabled;

            if (_rule.Trigger != null)
            {
                _cmbTriggerType.SelectedItem = _rule.Trigger.Type;
                _nudThreshold.Value = _rule.Trigger.Threshold;
            }
            else
            {
                _cmbTriggerType.SelectedIndex = 0;
            }

            UpdateThresholdVisibility();
            RefreshActionList();
        }

        private void SaveRule()
        {
            _rule.Name = _txtRuleName.Text.Trim();
            _rule.Enabled = _chkEnabled.Checked;

            if (_rule.Trigger == null) _rule.Trigger = new TriggerConfig();
            _rule.Trigger.Type = (TriggerType)_cmbTriggerType.SelectedItem;
            _rule.Trigger.Threshold = (int)_nudThreshold.Value;
        }

        // ------------------------------------------------------------------
        // Trigger type change
        // ------------------------------------------------------------------

        private void CmbTriggerType_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateThresholdVisibility();
        }

        private void UpdateThresholdVisibility()
        {
            if (_cmbTriggerType.SelectedItem == null) return;

            var type = (TriggerType)_cmbTriggerType.SelectedItem;
            bool showThreshold = (type == TriggerType.BatteryBelow || type == TriggerType.TimeRemainingBelow);

            _lblThreshold.Visible = showThreshold;
            _nudThreshold.Visible = showThreshold;

            if (showThreshold)
            {
                if (type == TriggerType.BatteryBelow)
                {
                    _lblThreshold.Text = "Threshold (%):";
                    _nudThreshold.Maximum = 100;
                    _nudThreshold.Minimum = 0;
                }
                else // TimeRemainingBelow
                {
                    _lblThreshold.Text = "Threshold (min):";
                    _nudThreshold.Maximum = 1440;
                    _nudThreshold.Minimum = 0;
                }
            }
        }

        // ------------------------------------------------------------------
        // Action list management
        // ------------------------------------------------------------------

        private void RefreshActionList()
        {
            _actionList.Items.Clear();
            if (_rule.Actions == null) return;

            for (int i = 0; i < _rule.Actions.Count; i++)
            {
                var a = _rule.Actions[i];
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(a.Type.ToString());
                item.SubItems.Add(ActionSummary(a));
                _actionList.Items.Add(item);
            }
        }

        private string ActionSummary(ActionConfig a)
        {
            switch (a.Type)
            {
                case ActionType.RunScript:
                    return a.ScriptPath ?? "(no path)";
                case ActionType.Notification:
                    return $"{a.NotificationTitle}: {a.NotificationBody}".TrimEnd();
                case ActionType.WriteLog:
                    return $"{a.LogPath}: {a.LogMessage}".TrimEnd();
                default:
                    return "";
            }
        }

        private void BtnAddAction_Click(object sender, EventArgs e)
        {
            AddAction();
        }

        private void BtnMoveUp_Click(object sender, EventArgs e)
        {
            int idx = SelectedActionIndex();
            if (idx <= 0) return;
            var actions = _rule.Actions;
            var tmp = actions[idx];
            actions[idx] = actions[idx - 1];
            actions[idx - 1] = tmp;
            RefreshActionList();
            _actionList.Items[idx - 1].Selected = true;
        }

        private void BtnMoveDown_Click(object sender, EventArgs e)
        {
            int idx = SelectedActionIndex();
            if (idx < 0 || idx >= _rule.Actions.Count - 1) return;
            var actions = _rule.Actions;
            var tmp = actions[idx];
            actions[idx] = actions[idx + 1];
            actions[idx + 1] = tmp;
            RefreshActionList();
            _actionList.Items[idx + 1].Selected = true;
        }

        private void BtnRemoveAction_Click(object sender, EventArgs e)
        {
            int idx = SelectedActionIndex();
            if (idx < 0) return;
            _rule.Actions.RemoveAt(idx);
            RefreshActionList();
        }

        private int SelectedActionIndex()
        {
            if (_actionList.SelectedItems.Count == 0) return -1;
            return _actionList.SelectedItems[0].Index;
        }

        // ------------------------------------------------------------------
        // Add action flow
        // ------------------------------------------------------------------

        private void AddAction()
        {
            // Step 1: pick action type
            ActionType? picked = PickActionType();
            if (picked == null) return;

            // Step 2: configure the action
            ActionConfig cfg = ConfigureAction(picked.Value);
            if (cfg == null) return;

            if (_rule.Actions == null) _rule.Actions = new List<ActionConfig>();
            _rule.Actions.Add(cfg);
            RefreshActionList();
        }

        private ActionType? PickActionType()
        {
            using (var dlg = new Form())
            {
                dlg.Text = "Add Action";
                dlg.Size = new Size(280, 140);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                var lbl = new Label { Text = "Action type:", Location = new Point(12, 16), AutoSize = true };
                var cmb = new ComboBox
                {
                    Location = new Point(12, 36),
                    Width = 240,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                cmb.Items.AddRange(new object[]
                {
                    ActionType.RunScript,
                    ActionType.Shutdown,
                    ActionType.Hibernate,
                    ActionType.Sleep,
                    ActionType.Notification,
                    ActionType.WriteLog
                });
                cmb.SelectedIndex = 0;

                var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 70, Location = new Point(182, 68) };
                var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 70, Location = new Point(104, 68) };

                dlg.Controls.AddRange(new Control[] { lbl, cmb, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog() == DialogResult.OK)
                    return (ActionType)cmb.SelectedItem;
            }
            return null;
        }

        private ActionConfig ConfigureAction(ActionType type)
        {
            var cfg = new ActionConfig { Type = type };

            switch (type)
            {
                case ActionType.RunScript:
                    return ConfigureRunScript(cfg);
                case ActionType.Notification:
                    return ConfigureNotification(cfg);
                case ActionType.WriteLog:
                    return ConfigureWriteLog(cfg);
                case ActionType.Shutdown:
                case ActionType.Hibernate:
                case ActionType.Sleep:
                    // No additional config needed
                    return cfg;
                default:
                    return cfg;
            }
        }

        // ---- RunScript config dialog ----
        private ActionConfig ConfigureRunScript(ActionConfig cfg)
        {
            using (var dlg = new Form())
            {
                dlg.Text = "Configure: Run Script";
                dlg.Size = new Size(460, 140);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                var lbl = new Label { Text = "Script path:", Location = new Point(12, 16), AutoSize = true };
                var txt = new TextBox { Location = new Point(12, 36), Width = 330, Height = 22 };
                var btnBrowse = new Button { Text = "Browse...", Location = new Point(350, 35), Width = 80, Height = 24 };

                btnBrowse.Click += (s, e) =>
                {
                    using (var fd = new OpenFileDialog
                    {
                        Title = "Select script or executable",
                        Filter = "Executables & Scripts|*.exe;*.cmd;*.bat;*.ps1|All files|*.*"
                    })
                    {
                        if (fd.ShowDialog() == DialogResult.OK)
                            txt.Text = fd.FileName;
                    }
                };

                var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 70, Location = new Point(370, 70) };
                var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 70, Location = new Point(292, 70) };

                dlg.Controls.AddRange(new Control[] { lbl, txt, btnBrowse, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    cfg.ScriptPath = txt.Text.Trim();
                    return cfg;
                }
            }
            return null;
        }

        // ---- Notification config dialog ----
        private ActionConfig ConfigureNotification(ActionConfig cfg)
        {
            using (var dlg = new Form())
            {
                dlg.Text = "Configure: Notification";
                dlg.Size = new Size(440, 280);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                var lblTitle = new Label { Text = "Title:", Location = new Point(12, 16), AutoSize = true };
                var txtTitle = new TextBox { Location = new Point(12, 36), Width = 400, Height = 22, Text = "EcoFlow Alert" };

                var lblBody = new Label { Text = "Body (vars: {device}, {battery}, {remain}, {status}, {in_w}, {out_w}):", Location = new Point(12, 68), Width = 410 };
                var txtBody = new TextBox
                {
                    Location = new Point(12, 88),
                    Width = 400,
                    Height = 90,
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical
                };

                var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 70, Location = new Point(342, 192) };
                var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 70, Location = new Point(264, 192) };

                dlg.Controls.AddRange(new Control[] { lblTitle, txtTitle, lblBody, txtBody, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    cfg.NotificationTitle = txtTitle.Text.Trim();
                    cfg.NotificationBody = txtBody.Text.Trim();
                    return cfg;
                }
            }
            return null;
        }

        // ---- WriteLog config dialog ----
        private ActionConfig ConfigureWriteLog(ActionConfig cfg)
        {
            using (var dlg = new Form())
            {
                dlg.Text = "Configure: Write Log";
                dlg.Size = new Size(460, 220);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                var lblPath = new Label { Text = "Log file path:", Location = new Point(12, 16), AutoSize = true };
                var txtPath = new TextBox { Location = new Point(12, 36), Width = 330, Height = 22 };
                var btnBrowse = new Button { Text = "Browse...", Location = new Point(350, 35), Width = 80, Height = 24 };

                btnBrowse.Click += (s, e) =>
                {
                    using (var fd = new SaveFileDialog
                    {
                        Title = "Select log file",
                        Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files|*.*",
                        FileName = txtPath.Text
                    })
                    {
                        if (fd.ShowDialog() == DialogResult.OK)
                            txtPath.Text = fd.FileName;
                    }
                };

                var lblMsg = new Label { Text = "Message (vars: {device}, {battery}, {remain}, {status}, {in_w}, {out_w}):", Location = new Point(12, 68), Width = 430 };
                var txtMsg = new TextBox { Location = new Point(12, 88), Width = 420, Height = 22 };

                var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 70, Location = new Point(370, 124) };
                var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 70, Location = new Point(292, 124) };

                dlg.Controls.AddRange(new Control[] { lblPath, txtPath, btnBrowse, lblMsg, txtMsg, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    cfg.LogPath = txtPath.Text.Trim();
                    cfg.LogMessage = txtMsg.Text.Trim();
                    return cfg;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // OK button handler
        // ------------------------------------------------------------------

        private void BtnOk_Click(object sender, EventArgs e)
        {
            SaveRule();
        }

        // ------------------------------------------------------------------
        // UI helpers
        // ------------------------------------------------------------------

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

        private static Panel MakeSeparator(int x, int y, int width)
        {
            return new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, 2),
                BorderStyle = BorderStyle.Fixed3D
            };
        }
    }
}
