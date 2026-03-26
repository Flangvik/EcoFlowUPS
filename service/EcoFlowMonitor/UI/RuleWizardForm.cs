using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EcoFlowMonitor.Actions;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.UI
{
    /// <summary>
    /// Four-step wizard for creating or editing a rule.
    /// Steps: 1-Name  2-Trigger  3-Threshold(conditional)  4-Actions
    /// Read Result on DialogResult.OK.
    /// </summary>
    public class RuleWizardForm : Form
    {
        public RuleConfig Result { get; private set; }

        private readonly RuleConfig _rule;

        // Header
        private Label _lblStepTitle;
        private Panel _pnlBreadcrumb;

        // Content container — cleared and repopulated per step
        private Panel _pnlContent;

        // Navigation
        private Button _btnBack;
        private Button _btnNext;
        private Button _btnCancel;

        // Step 1 controls (created fresh each ShowStep)
        private TextBox  _txtName;
        private CheckBox _chkEnabled;

        // Step 2 — trigger selection
        private TriggerType? _selectedTrigger;
        private TriggerType? _hoveredTrigger;
        private List<Panel>  _triggerCards;

        // Step 3 — threshold
        private Label         _lblThresholdDesc;
        private NumericUpDown _nudThreshold;
        private Label         _lblThresholdUnit;

        // Step 4 — action list
        private Panel _pnlActionList;
        private int   _selectedActionIdx = -1;

        // Navigation state
        private int  _currentStep = 1;
        private bool _hasThreshold;

        private int TotalSteps => _hasThreshold ? 4 : 3;

        // ------------------------------------------------------------------
        // Trigger definitions
        // ------------------------------------------------------------------

        private static readonly (TriggerType Type, string Icon, string Title, string Desc)[] TriggerDefs =
        {
            (TriggerType.PowerLost,          "⚡", "Power Lost",                "Grid power cuts out"),
            (TriggerType.PowerRestored,      "↺",  "Power Restored",            "Grid power comes back"),
            (TriggerType.BatteryBelow,       "🔋", "Battery Below …%",          "Battery drops under a set percentage"),
            (TriggerType.TimeRemainingBelow, "⏱", "Time Remaining Below …min", "Runtime drops under a set threshold"),
        };

        // ------------------------------------------------------------------
        // Constructor
        // ------------------------------------------------------------------

        public RuleWizardForm(RuleConfig rule)
        {
            _rule = Clone(rule);

            if (_rule.Trigger != null)
            {
                _selectedTrigger = _rule.Trigger.Type;
                _hasThreshold    = (_rule.Trigger.Type == TriggerType.BatteryBelow ||
                                    _rule.Trigger.Type == TriggerType.TimeRemainingBelow);
            }

            InitializeComponent();
            ThemeManager.Apply(this);
            StyleAccentButton(_btnNext);
            ShowStep(1);
        }

        // ------------------------------------------------------------------
        // Layout (persistent chrome only)
        // ------------------------------------------------------------------

        private void InitializeComponent()
        {
            this.Text            = "Rule Editor";
            this.Size            = new Size(480, 460);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition   = FormStartPosition.CenterParent;
            this.AutoScaleMode   = AutoScaleMode.Dpi;
            this.MaximizeBox     = false;
            this.MinimizeBox     = false;

            // ---- Header (68px) ----
            var pnlHeader = new TableLayoutPanel
            {
                Dock        = DockStyle.Top,
                Height      = 68,
                RowCount    = 2,
                ColumnCount = 1,
                Padding     = new Padding(20, 8, 20, 0),
                Margin      = Padding.Empty
            };
            pnlHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pnlHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            pnlHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            _lblStepTitle = new Label
            {
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 12, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlHeader.Controls.Add(_lblStepTitle, 0, 0);

            _pnlBreadcrumb = new Panel { Dock = DockStyle.Fill };
            _pnlBreadcrumb.Paint += PaintBreadcrumb;
            pnlHeader.Controls.Add(_pnlBreadcrumb, 0, 1);

            var headerLine = new Panel { Dock = DockStyle.Bottom, Height = 1 };
            headerLine.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(ThemeManager.Border), 0, 0, headerLine.Width, 0);

            this.Controls.Add(headerLine);
            this.Controls.Add(pnlHeader);

            // ---- Navigation (52px) ----
            var pnlNav  = new Panel { Dock = DockStyle.Bottom, Height = 52 };
            var navLine = new Panel { Dock = DockStyle.Top, Height = 1 };
            navLine.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(ThemeManager.Border), 0, 0, navLine.Width, 0);

            var navFlow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents  = false,
                Padding       = new Padding(12, 10, 12, 10)
            };

            _btnNext   = new Button { Text = "Next >", Width = 86, Height = 30, Margin = new Padding(0),           FlatStyle = FlatStyle.Flat };
            _btnBack   = new Button { Text = "< Back", Width = 86, Height = 30, Margin = new Padding(0, 0, 6, 0),  FlatStyle = FlatStyle.Flat };
            _btnCancel = new Button { Text = "Cancel", Width = 86, Height = 30, Margin = new Padding(0, 0, 24, 0), FlatStyle = FlatStyle.Flat };

            _btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
            _btnBack.Click   += (s, e) => Navigate(-1);
            _btnNext.Click   += (s, e) => Navigate(+1);

            navFlow.Controls.Add(_btnNext);
            navFlow.Controls.Add(_btnBack);
            navFlow.Controls.Add(_btnCancel);
            pnlNav.Controls.Add(navFlow);
            pnlNav.Controls.Add(navLine);
            this.Controls.Add(pnlNav);

            // ---- Content (Fill) ----
            _pnlContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 8) };
            this.Controls.Add(_pnlContent);
        }

        // ------------------------------------------------------------------
        // Step management
        // ------------------------------------------------------------------

        private void ShowStep(int step)
        {
            _currentStep = step;
            _pnlContent.SuspendLayout();
            _pnlContent.Controls.Clear();

            var panel = BuildStep(step);
            panel.Dock = DockStyle.Fill;
            ApplyDarkInputs(panel);
            _pnlContent.Controls.Add(panel);

            _pnlContent.ResumeLayout(true);
            UpdateHeader();
            _btnBack.Enabled = step > 1;
            _btnNext.Text    = (step == TotalSteps) ? "Finish" : "Next >";
        }

        // Applies dark theme only to input controls (TextBox, NumericUpDown, ComboBox, Button)
        // that are built dynamically after the initial ThemeManager.Apply() call.
        private static void ApplyDarkInputs(Control c)
        {
            switch (c)
            {
                case TextBox tb:
                    tb.BackColor = ThemeManager.ControlBg;
                    tb.ForeColor = ThemeManager.Foreground;
                    break;
                case NumericUpDown nud:
                    nud.BackColor = ThemeManager.ControlBg;
                    nud.ForeColor = ThemeManager.Foreground;
                    break;
                case ComboBox cb:
                    cb.BackColor = ThemeManager.ControlBg;
                    cb.ForeColor = ThemeManager.Foreground;
                    cb.FlatStyle = FlatStyle.Flat;
                    break;
                case Button btn:
                    btn.BackColor = ThemeManager.ButtonBg;
                    btn.ForeColor = ThemeManager.Foreground;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = ThemeManager.Border;
                    break;
            }
            foreach (Control child in c.Controls)
                ApplyDarkInputs(child);
        }

        private Control BuildStep(int step)
        {
            switch (step)
            {
                case 1: return BuildStepName();
                case 2: return BuildStepTrigger();
                case 3: return _hasThreshold ? BuildStepThreshold() : BuildStepActions();
                case 4: return BuildStepActions();
                default: return new Panel();
            }
        }

        private void UpdateHeader()
        {
            _lblStepTitle.Text = $"Step {_currentStep} of {TotalSteps} — {StepTitleFull(_currentStep)}";
            _pnlBreadcrumb.Invalidate();
        }

        private string StepTitleFull(int step)
        {
            switch (step)
            {
                case 1: return "Name Your Rule";
                case 2: return "Choose a Trigger";
                case 3: return _hasThreshold ? "Set the Threshold" : "Add Actions";
                case 4: return "Add Actions";
                default: return "";
            }
        }

        // ------------------------------------------------------------------
        // Step 1 — Name
        // ------------------------------------------------------------------

        private Control BuildStepName()
        {
            var tbl = MakeTable(6, 1);
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // title
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); // "Rule Name" label
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // textbox
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); // spacer
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // checkbox
            tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // filler

            tbl.Controls.Add(MakeTitle("Name Your Rule"), 0, 0);
            tbl.Controls.Add(MakeSubLabel("Rule Name"), 0, 1);

            _txtName = new TextBox
            {
                Dock        = DockStyle.Fill,
                Font        = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle,
                Text        = _rule.Name ?? ""
            };
            tbl.Controls.Add(_txtName, 0, 2);
            tbl.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 3);

            _chkEnabled = new CheckBox
            {
                Text      = "Enable this rule",
                Dock      = DockStyle.Fill,
                AutoSize  = false,
                Checked   = _rule.Enabled,
                Font      = new Font("Segoe UI", 9.5f),
                ForeColor = ThemeManager.Foreground,
                BackColor = Color.Transparent
            };
            tbl.Controls.Add(_chkEnabled, 0, 4);
            tbl.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 5);

            return tbl;
        }

        // ------------------------------------------------------------------
        // Step 2 — Trigger cards
        // ------------------------------------------------------------------

        private Control BuildStepTrigger()
        {
            int cardCount = TriggerDefs.Length;
            var tbl = MakeTable(3 + cardCount + 1, 1);
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // title
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); // desc
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 10)); // spacer
            for (int i = 0; i < cardCount; i++)
                tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); // cards
            tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // filler

            tbl.Controls.Add(MakeTitle("Choose a Trigger"), 0, 0);
            tbl.Controls.Add(MakeSubLabel("When should this rule fire?"), 0, 1);
            tbl.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 2);

            _triggerCards = new List<Panel>();
            for (int i = 0; i < TriggerDefs.Length; i++)
            {
                var def  = TriggerDefs[i];
                var card = BuildTriggerCard(def.Type, def.Icon, def.Title, def.Desc);
                _triggerCards.Add(card);
                tbl.Controls.Add(card, 0, 3 + i);
            }
            tbl.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 3 + cardCount);

            return tbl;
        }

        private Panel BuildTriggerCard(TriggerType type, string icon, string title, string desc)
        {
            var card = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = ThemeManager.ControlBg,
                Cursor    = Cursors.Hand,
                Margin    = new Padding(0, 0, 0, 4)
            };

            var inner = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 2,
                RowCount    = 1,
                Margin      = Padding.Empty,
                Padding     = new Padding(6, 4, 6, 4),
                BackColor   = Color.Transparent
            };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var lblIcon = new Label
            {
                Text      = icon,
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 14),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = ThemeManager.Foreground,
                BackColor = Color.Transparent
            };
            inner.Controls.Add(lblIcon, 0, 0);

            var textTbl = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 1,
                RowCount    = 2,
                Margin      = Padding.Empty,
                Padding     = Padding.Empty,
                BackColor   = Color.Transparent
            };
            textTbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            textTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            textTbl.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

            var lblTitle = new Label
            {
                Text      = title,
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.BottomLeft,
                ForeColor = ThemeManager.Foreground,
                BackColor = Color.Transparent
            };
            var lblDesc = new Label
            {
                Text      = desc,
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 8.5f),
                TextAlign = ContentAlignment.TopLeft,
                ForeColor = ThemeManager.SubText,
                BackColor = Color.Transparent
            };
            textTbl.Controls.Add(lblTitle, 0, 0);
            textTbl.Controls.Add(lblDesc,  0, 1);
            inner.Controls.Add(textTbl, 1, 0);
            card.Controls.Add(inner);

            // All visual state drawn here — no BackColor manipulation elsewhere
            card.Paint += (s, e) =>
            {
                bool sel = _selectedTrigger == type;
                bool hov = _hoveredTrigger  == type;

                Color bg = sel ? Color.FromArgb(40, ThemeManager.MenuHover.R, ThemeManager.MenuHover.G, ThemeManager.MenuHover.B)
                         : hov ? Color.FromArgb(20, ThemeManager.MenuHover.R, ThemeManager.MenuHover.G, ThemeManager.MenuHover.B)
                         : ThemeManager.ControlBg;
                using var bgBrush = new SolidBrush(bg);
                e.Graphics.FillRectangle(bgBrush, 0, 0, card.Width, card.Height);

                Color borderColor = sel ? ThemeManager.MenuHover : ThemeManager.Border;
                int   thick       = sel ? 2 : 1;
                using var pen = new Pen(borderColor, thick);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            // Click to select
            void Select(object s, EventArgs e)
            {
                _selectedTrigger = type;
                if (_triggerCards != null)
                    foreach (var c in _triggerCards) c.Invalidate();
            }

            card.Click     += Select;
            inner.Click    += Select;
            lblIcon.Click  += Select;
            textTbl.Click  += Select;
            lblTitle.Click += Select;
            lblDesc.Click  += Select;

            // Hover — tracked in field so Paint can read it for any card
            void Enter(object s, EventArgs e) { _hoveredTrigger = type; card.Invalidate(); }
            void Leave(object s, EventArgs e) { _hoveredTrigger = null; card.Invalidate(); }

            card.MouseEnter  += Enter;
            card.MouseLeave  += Leave;
            inner.MouseEnter += Enter;
            inner.MouseLeave += Leave;

            return card;
        }

        // ------------------------------------------------------------------
        // Step 3 — Threshold
        // ------------------------------------------------------------------

        private Control BuildStepThreshold()
        {
            var tbl = MakeTable(5, 1);
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // title
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // desc
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); // spacer
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // NUD row
            tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // filler

            tbl.Controls.Add(MakeTitle("Set the Threshold"), 0, 0);

            _lblThresholdDesc = new Label
            {
                Dock      = DockStyle.Fill,
                ForeColor = ThemeManager.SubText,
                Font      = new Font("Segoe UI", 9),
                BackColor = Color.Transparent
            };
            tbl.Controls.Add(_lblThresholdDesc, 0, 1);
            tbl.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 2);

            var row = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                BackColor     = Color.Transparent
            };
            _nudThreshold = new NumericUpDown
            {
                Width         = 80,
                Height        = 28,
                Minimum       = 0,
                Maximum       = 100,
                DecimalPlaces = 0,
                Font          = new Font("Segoe UI", 10),
                Margin        = new Padding(0, 4, 6, 0)
            };
            _lblThresholdUnit = new Label
            {
                AutoSize  = false,
                Width     = 50,
                Height    = 28,
                Font      = new Font("Segoe UI", 10),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ThemeManager.Foreground,
                BackColor = Color.Transparent,
                Margin    = new Padding(0, 4, 0, 0)
            };
            row.Controls.Add(_nudThreshold);
            row.Controls.Add(_lblThresholdUnit);
            tbl.Controls.Add(row, 0, 3);
            tbl.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 4);

            if (_selectedTrigger.HasValue) ConfigureThresholdStep(_selectedTrigger.Value);
            if (_rule.Trigger != null && _hasThreshold)
                _nudThreshold.Value = Math.Max(_nudThreshold.Minimum,
                    Math.Min(_nudThreshold.Maximum, _rule.Trigger.Threshold));

            return tbl;
        }

        // ------------------------------------------------------------------
        // Step 4 — Actions
        // ------------------------------------------------------------------

        private Control BuildStepActions()
        {
            var tbl = MakeTable(4, 1);
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // title
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); // desc
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // buttons
            tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // list

            tbl.Controls.Add(MakeTitle("Add Actions"), 0, 0);
            tbl.Controls.Add(MakeSubLabel("What should happen when the rule fires?"), 0, 1);

            var btnRow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                BackColor     = Color.Transparent
            };
            var btnAdd    = new Button { Text = "+ Add Action", Width = 110, Height = 26, Margin = new Padding(0, 4, 8, 0), FlatStyle = FlatStyle.Flat };
            var btnRemove = new Button { Text = "Remove",       Width = 80,  Height = 26, Margin = new Padding(0, 4, 0, 0), FlatStyle = FlatStyle.Flat };
            btnAdd.Click    += BtnAddAction_Click;
            btnRemove.Click += BtnRemoveAction_Click;
            btnRow.Controls.Add(btnAdd);
            btnRow.Controls.Add(btnRemove);
            tbl.Controls.Add(btnRow, 0, 2);

            _pnlActionList = new Panel
            {
                Dock        = DockStyle.Fill,
                AutoScroll  = true,
                BorderStyle = BorderStyle.None,
                BackColor   = Color.Transparent
            };
            tbl.Controls.Add(_pnlActionList, 0, 3);
            RefreshActionList();

            return tbl;
        }

        // ------------------------------------------------------------------
        // Breadcrumb paint
        // ------------------------------------------------------------------

        private void PaintBreadcrumb(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            string[] names = _hasThreshold
                ? new[] { "Name", "Trigger", "Threshold", "Actions" }
                : new[] { "Name", "Trigger", "Actions" };

            int   total   = TotalSteps;
            int   display = _currentStep;
            float r       = 7f;
            float spacing = 72f;
            float startX  = 12f;
            float cy      = e.ClipRectangle.Height / 2f;

            using var fnt       = new Font("Segoe UI", 7.5f);
            using var fgBrush   = new SolidBrush(ThemeManager.Foreground);
            using var dimBrush  = new SolidBrush(ThemeManager.SubText);
            using var accBrush  = new SolidBrush(ThemeManager.MenuHover);
            using var doneBrush = new SolidBrush(Color.FromArgb(40, 200, 100));
            using var bgBrush   = new SolidBrush(ThemeManager.Background);

            for (int i = 1; i <= total; i++)
            {
                float cx = startX + (i - 1) * spacing;

                // Connector line to next step
                if (i < total)
                {
                    using var linePen = new Pen(i < display ? Color.FromArgb(40, 200, 100) : ThemeManager.Border, 1f);
                    g.DrawLine(linePen, cx + r, cy, cx + spacing - r, cy);
                }

                var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);

                if (i < display) // done
                {
                    g.FillEllipse(doneBrush, rect);
                    using var ckPen = new Pen(Color.White, 1.5f);
                    g.DrawLine(ckPen, cx - 3f, cy,        cx - 0.5f, cy + 2.5f);
                    g.DrawLine(ckPen, cx - 0.5f, cy + 2.5f, cx + 3.5f, cy - 2.5f);
                }
                else if (i == display) // active
                {
                    g.FillEllipse(accBrush, rect);
                    using var wPen = new Pen(Color.White, 1.5f);
                    g.DrawEllipse(wPen, rect);
                }
                else // future
                {
                    g.FillEllipse(bgBrush, rect);
                    using var bPen = new Pen(ThemeManager.Border, 1f);
                    g.DrawEllipse(bPen, rect);
                }

                // Step label below circle
                string lbl = (i <= names.Length) ? names[i - 1] : "";
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(lbl, fnt, i == display ? fgBrush : dimBrush, cx, cy + r + 1f, sf);
                sf.Dispose();
            }
        }

        // ------------------------------------------------------------------
        // Navigation
        // ------------------------------------------------------------------

        private void Navigate(int direction)
        {
            if (direction > 0 && !ValidateCurrentStep()) return;
            if (direction > 0) CommitCurrentStep();

            int next = _currentStep + direction;
            if (next < 1) next = 1;
            if (next > TotalSteps)
            {
                Finish();
                return;
            }
            ShowStep(next);
        }

        private bool ValidateCurrentStep()
        {
            if (_currentStep == 1 && string.IsNullOrWhiteSpace(_txtName?.Text))
            {
                MessageBox.Show("Please enter a name for this rule.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtName?.Focus();
                return false;
            }
            if (_currentStep == 2 && !_selectedTrigger.HasValue)
            {
                MessageBox.Show("Please choose a trigger.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private void CommitCurrentStep()
        {
            switch (_currentStep)
            {
                case 1:
                    _rule.Name    = _txtName.Text.Trim();
                    _rule.Enabled = _chkEnabled.Checked;
                    break;

                case 2:
                    _rule.Trigger = new Models.TriggerConfig { Type = _selectedTrigger.Value };
                    _hasThreshold = (_rule.Trigger.Type == TriggerType.BatteryBelow ||
                                     _rule.Trigger.Type == TriggerType.TimeRemainingBelow);
                    break;

                case 3:
                    if (_hasThreshold && _nudThreshold != null)
                        _rule.Trigger.Threshold = (int)_nudThreshold.Value;
                    break;
            }
        }

        private void Finish()
        {
            CommitCurrentStep();
            Result       = _rule;
            DialogResult = DialogResult.OK;
        }

        // ------------------------------------------------------------------
        // Threshold step configuration
        // ------------------------------------------------------------------

        private void ConfigureThresholdStep(TriggerType type)
        {
            if (_nudThreshold == null || _lblThresholdDesc == null) return;
            if (type == TriggerType.BatteryBelow)
            {
                _lblThresholdDesc.Text = "Fire when battery drops below this percentage:";
                _nudThreshold.Maximum  = 100;
                _nudThreshold.Minimum  = 1;
                _lblThresholdUnit.Text = "%";
            }
            else
            {
                _lblThresholdDesc.Text = "Fire when estimated runtime drops below this many minutes:";
                _nudThreshold.Maximum  = 1440;
                _nudThreshold.Minimum  = 1;
                _lblThresholdUnit.Text = "min";
            }
        }

        // ------------------------------------------------------------------
        // Step 4 — action list management
        // ------------------------------------------------------------------

        private void BtnAddAction_Click(object sender, EventArgs e)
        {
            var cfg = PickAndConfigureAction();
            if (cfg == null) return;
            if (_rule.Actions == null) _rule.Actions = new List<ActionConfig>();
            _rule.Actions.Add(cfg);
            _selectedActionIdx = _rule.Actions.Count - 1;
            RefreshActionList();
        }

        private void BtnRemoveAction_Click(object sender, EventArgs e)
        {
            if (_pnlActionList == null || _selectedActionIdx < 0) return;
            if (_rule.Actions == null || _selectedActionIdx >= _rule.Actions.Count) return;
            _rule.Actions.RemoveAt(_selectedActionIdx);
            _selectedActionIdx = Math.Min(_selectedActionIdx, _rule.Actions.Count - 1);
            RefreshActionList();
        }

        private void RefreshActionList()
        {
            if (_pnlActionList == null) return;
            _pnlActionList.Controls.Clear();
            if (_rule.Actions == null) return;

            int y = 0;
            for (int i = 0; i < _rule.Actions.Count; i++)
            {
                int idx = i;
                var a   = _rule.Actions[i];
                var row = new Panel
                {
                    Location  = new Point(0, y),
                    Height    = 36,
                    Width     = _pnlActionList.ClientSize.Width,
                    Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                    BackColor = (idx == _selectedActionIdx) ? ThemeManager.MenuHover : Color.Transparent
                };
                var lbl = new Label
                {
                    Text      = $"{i + 1}. {a.Type} — {ActionSummary(a)}",
                    Dock      = DockStyle.Fill,
                    Font      = new Font("Segoe UI", 9),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding   = new Padding(8, 0, 0, 0),
                    ForeColor = ThemeManager.Foreground,
                    BackColor = Color.Transparent
                };
                row.Click += (s, ev) => { _selectedActionIdx = idx; RefreshActionList(); };
                lbl.Click += (s, ev) => { _selectedActionIdx = idx; RefreshActionList(); };
                row.Controls.Add(lbl);
                _pnlActionList.Controls.Add(row);
                y += 36;
            }
        }

        private static string ActionSummary(ActionConfig a) =>
            a.Type switch
            {
                ActionType.RunScript    => a.ScriptPath ?? "(no path)",
                ActionType.Notification => $"{a.NotificationTitle}: {a.NotificationBody}".TrimEnd(':').Trim(),
                ActionType.WriteLog     => $"{a.LogPath}: {a.LogMessage}".TrimEnd(':').Trim(),
                _                       => ""
            };

        // ------------------------------------------------------------------
        // Action picker + config mini-dialogs
        // ------------------------------------------------------------------

        private ActionConfig PickAndConfigureAction()
        {
            ActionType? type = PickActionType();
            if (type == null) return null;
            return ConfigureAction(type.Value);
        }

        private ActionType? PickActionType()
        {
            using (var dlg = MakeMiniDialog("Add Action", 280, 140))
            {
                var lbl = new Label { Text = "Action type:", Location = new Point(12, 14), AutoSize = true };
                var cmb = new ComboBox
                {
                    Location      = new Point(12, 34),
                    Width         = 244,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                cmb.Items.AddRange(new object[]
                {
                    ActionType.Notification, ActionType.RunScript,
                    ActionType.Shutdown, ActionType.Hibernate, ActionType.Sleep,
                    ActionType.WriteLog
                });
                cmb.SelectedIndex = 0;
                var ok     = MiniOk(190, 76);
                var cancel = MiniCancel(112, 76);
                dlg.Controls.AddRange(new Control[] { lbl, cmb, ok, cancel });
                return dlg.ShowDialog() == DialogResult.OK ? (ActionType?)cmb.SelectedItem : null;
            }
        }

        private ActionConfig ConfigureAction(ActionType type)
        {
            var cfg = new ActionConfig { Type = type };
            switch (type)
            {
                case ActionType.RunScript:    return ConfigureScript(cfg);
                case ActionType.Notification: return ConfigureNotification(cfg);
                case ActionType.WriteLog:     return ConfigureWriteLog(cfg);
                default:                      return cfg;
            }
        }

        private ActionConfig ConfigureScript(ActionConfig cfg)
        {
            using (var dlg = MakeMiniDialog("Run Script", 460, 140))
            {
                var lbl    = new Label { Text = "Script path:", Location = new Point(12, 14), AutoSize = true };
                var txt    = new TextBox { Location = new Point(12, 34), Width = 330, Height = 24 };
                var browse = new Button { Text = "Browse…", Location = new Point(348, 33), Width = 80, Height = 26, FlatStyle = FlatStyle.Flat };
                browse.Click += (s, e) =>
                {
                    using (var fd = new OpenFileDialog { Filter = "Scripts & Executables|*.exe;*.cmd;*.bat;*.ps1|All files|*.*" })
                        if (fd.ShowDialog() == DialogResult.OK) txt.Text = fd.FileName;
                };
                var ok = MiniOk(368, 76); var cancel = MiniCancel(280, 76);
                dlg.Controls.AddRange(new Control[] { lbl, txt, browse, ok, cancel });
                if (dlg.ShowDialog() == DialogResult.OK) { cfg.ScriptPath = txt.Text.Trim(); return cfg; }
            }
            return null;
        }

        private ActionConfig ConfigureNotification(ActionConfig cfg)
        {
            using (var dlg = MakeMiniDialog("Notification", 440, 230))
            {
                var lblT = new Label { Text = "Title:",                 Location = new Point(12, 14), AutoSize = true };
                var txtT = new TextBox { Location = new Point(12, 32), Width = 406, Height = 24, Text = "EcoFlow Alert" };
                var lblB = new Label { Text = "Body (supports {device}, {battery}, {remain}, {status}):",
                                       Location = new Point(12, 66), Width = 400, AutoSize = false };
                var txtB = new TextBox { Location = new Point(12, 82), Width = 406, Height = 76, Multiline = true, ScrollBars = ScrollBars.Vertical };
                var ok = MiniOk(346, 168); var cancel = MiniCancel(252, 168);
                dlg.Controls.AddRange(new Control[] { lblT, txtT, lblB, txtB, ok, cancel });
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    cfg.NotificationTitle = txtT.Text.Trim();
                    cfg.NotificationBody  = txtB.Text.Trim();
                    return cfg;
                }
            }
            return null;
        }

        private ActionConfig ConfigureWriteLog(ActionConfig cfg)
        {
            using (var dlg = MakeMiniDialog("Write Log", 460, 170))
            {
                var lblP   = new Label { Text = "Log file path:", Location = new Point(12, 14), AutoSize = true };
                var txtP   = new TextBox { Location = new Point(12, 32), Width = 330, Height = 24 };
                var browse = new Button { Text = "Browse…", Location = new Point(348, 31), Width = 80, Height = 26, FlatStyle = FlatStyle.Flat };
                browse.Click += (s, e) =>
                {
                    using (var fd = new SaveFileDialog { Filter = "Log files (*.log)|*.log|Text files|*.txt|All files|*.*" })
                        if (fd.ShowDialog() == DialogResult.OK) txtP.Text = fd.FileName;
                };
                var lblM = new Label { Text = "Message:", Location = new Point(12, 66), AutoSize = true };
                var txtM = new TextBox { Location = new Point(12, 82), Width = 420, Height = 24 };
                var ok = MiniOk(368, 108); var cancel = MiniCancel(280, 108);
                dlg.Controls.AddRange(new Control[] { lblP, txtP, browse, lblM, txtM, ok, cancel });
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    cfg.LogPath    = txtP.Text.Trim();
                    cfg.LogMessage = txtM.Text.Trim();
                    return cfg;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Deep clone
        // ------------------------------------------------------------------

        private static RuleConfig Clone(RuleConfig r)
        {
            var c = new RuleConfig
            {
                Id      = r.Id,
                Name    = r.Name,
                Enabled = r.Enabled,
                Trigger = r.Trigger == null ? new Models.TriggerConfig()
                    : new Models.TriggerConfig { Type = r.Trigger.Type, Threshold = r.Trigger.Threshold },
                Actions = new List<ActionConfig>()
            };
            if (r.Actions != null)
                foreach (var a in r.Actions)
                    c.Actions.Add(new ActionConfig
                    {
                        Type              = a.Type,
                        ScriptPath        = a.ScriptPath,
                        NotificationTitle = a.NotificationTitle,
                        NotificationBody  = a.NotificationBody,
                        LogPath           = a.LogPath,
                        LogMessage        = a.LogMessage
                    });
            return c;
        }

        // ------------------------------------------------------------------
        // UI helpers
        // ------------------------------------------------------------------

        private static TableLayoutPanel MakeTable(int rows, int cols) =>
            new TableLayoutPanel
            {
                RowCount    = rows,
                ColumnCount = cols,
                BackColor   = Color.Transparent,
                Padding     = Padding.Empty,
                Margin      = Padding.Empty
            };

        private static Label MakeTitle(string text) =>
            new Label
            {
                Text      = text,
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 13, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ThemeManager.Foreground,
                BackColor = Color.Transparent
            };

        private static Label MakeSubLabel(string text) =>
            new Label
            {
                Text      = text,
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 9),
                ForeColor = ThemeManager.SubText,
                BackColor = Color.Transparent
            };

        private Form MakeMiniDialog(string title, int w, int h)
        {
            var dlg = new Form
            {
                Text            = title,
                Size            = new Size(w, h),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition   = FormStartPosition.CenterParent,
                MaximizeBox     = false,
                MinimizeBox     = false
            };
            ThemeManager.Apply(dlg);
            return dlg;
        }

        private static Button MiniOk(int x, int y) =>
            new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(x, y), Width = 72, Height = 26, FlatStyle = FlatStyle.Flat };

        private static Button MiniCancel(int x, int y) =>
            new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(x, y), Width = 72, Height = 26, FlatStyle = FlatStyle.Flat };

        private static void StyleAccentButton(Button btn)
        {
            btn.BackColor = ThemeManager.MenuHover;
            btn.ForeColor = Color.White;
            if (btn.FlatAppearance != null) btn.FlatAppearance.BorderColor = ThemeManager.MenuHover;
        }
    }
}
