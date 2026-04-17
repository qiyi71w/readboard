using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace readboard
{
    public partial class SettingsForm : Form
    {
        private const int FieldLabelWidth = 110;
        private const int FieldInputWidth = 88;
        private readonly MainForm host;

        internal SettingsForm(MainForm host)
        {
            InitializeComponent();
            this.host = RequireHost(host);
            LoadConfigValues(Program.CurrentConfig);

            this.Text = getLangStr("SettingsForm_title");
            this.chkPonder.Text = getLangStr("SettingsForm_chkPonder");
            this.chkMag.Text = getLangStr("SettingsForm_chkMag");
            this.chkVerifyMove.Text = getLangStr("SettingsForm_chkVerifyMove");
            this.chkAutoMin.Text = getLangStr("SettingsForm_chkAutoMin");
            this.lblBackForeOnly.Text = getLangStr("SettingsForm_lblBackForeOnly");
            this.lblBlackOffsets.Text = getLangStr("SettingsForm_lblBlackOffsets");
            this.lblBlackPercents.Text = getLangStr("SettingsForm_lblBlackPercents");
            this.lblWhiteOffsets.Text = getLangStr("SettingsForm_lblWhiteffsets");
            this.lblWhitePercents.Text = getLangStr("SettingsForm_lblWhitePercents");
            this.lblGrayOffsets.Text = getLangStr("SettingsForm_lblGrayOffsets");
            this.lblTips.Text = getLangStr("SettingsForm_lblTips");
            this.lblTips1.Text = getLangStr("SettingsForm_lblTips1");
            this.lblTips2.Text = getLangStr("SettingsForm_lblTips2");
            this.lblSyncInterval.Text = getLangStr("SettingsForm_lblSyncInterval");
            this.btnReset.Text = getLangStr("SettingsForm_btnReset");
            this.btnConfirm.Text = getLangStr("SettingsForm_btnConfirm");
            this.btnCancel.Text = getLangStr("SettingsForm_btnCancel");
            this.chkEnhanceScreen.Text = getLangStr("SettingsForm_chkEnhanceScreen");

           // this.Size= new Size((int)(461 *Program.factor), (int)(270 * Program.factor));
           

            var toolTip1 = new ToolTip();
            toolTip1.SetToolTip(this.chkEnhanceScreen, getLangStr("SettingsForm_chkEnhanceScreen_ToolTip"));
            var toolTip2 = new ToolTip();
            toolTip2.SetToolTip(this.chkPonder, getLangStr("SettingsForm_chkPonder_ToolTip"));
            ApplySettingsFormUi();
        }

        private void ApplySettingsFormUi()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            ClientSize = new Size(560, 386);
            AcceptButton = btnConfirm;
            CancelButton = btnCancel;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            label5.Visible = false;
            if (Program.CurrentConfig.UiThemeMode == Program.UiThemeOptimized)
            {
                UiTheme.ApplyWindow(this);
                ApplySettingsTheme();
            }
            else
            {
                ApplyClassicSettingsTheme();
            }
            ArrangeSettingsLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        private void ApplySettingsTheme()
        {
            foreach (CheckBox checkBox in new[] { chkAutoMin, chkPonder, chkMag, chkEnhanceScreen, chkVerifyMove })
                UiTheme.StyleOption(checkBox);

            foreach (TextBox textBox in new[] { txtSyncInterval, txtGrayOffsets, txtBlackOffsets, txtBlackPercents, txtWhiteOffsets, txtWhitePercents })
            {
                UiTheme.StyleInput(textBox);
                textBox.TextAlign = HorizontalAlignment.Center;
            }

            foreach (Label label in new[] { lblSyncInterval, lblGrayOffsets, lblBlackOffsets, lblBlackPercents, lblWhiteOffsets, lblWhitePercents })
            {
                label.ForeColor = UiTheme.PrimaryText;
                label.Font = UiTheme.BodyFont;
            }

            foreach (Label label in new[] { lblTips, lblTips1, lblTips2 })
                UiTheme.StyleSubtleLabel(label);

            UiTheme.StyleNoticeLabel(lblBackForeOnly);
            UiTheme.StyleDangerButton(btnReset);
            UiTheme.StyleSecondaryButton(btnCancel);
            UiTheme.StylePrimaryButton(btnConfirm);
        }

        private void ArrangeSettingsLayout()
        {
            const int left = 18;
            const int top = 18;
            const int fieldRowGap = 38;
            const int footerGap = 14;
            const int buttonHeight = 32;
            const int confirmButtonWidth = 84;
            const int resetButtonWidth = 124;
            const int buttonGap = 12;
            int contentWidth = ClientSize.Width - left * 2;
            int right = ClientSize.Width - left - FieldLabelWidth - 8 - FieldInputWidth;

            chkAutoMin.Location = new Point(left, top);
            chkPonder.Location = new Point(170, top);
            chkMag.Location = new Point(left, top + 30);
            chkEnhanceScreen.Location = new Point(170, top + 30);
            chkVerifyMove.Location = new Point(left, top + 60);
            chkVerifyMove.Size = new Size(240, 20);
            int fieldsTop = LayoutWrappedLabel(lblBackForeOnly, left, 110, contentWidth, true) + 20;
            LayoutSettingsField(lblSyncInterval, txtSyncInterval, left, fieldsTop);
            LayoutSettingsField(lblGrayOffsets, txtGrayOffsets, right, fieldsTop);
            LayoutSettingsField(lblBlackOffsets, txtBlackOffsets, left, fieldsTop + fieldRowGap);
            LayoutSettingsField(lblBlackPercents, txtBlackPercents, right, fieldsTop + fieldRowGap);
            LayoutSettingsField(lblWhiteOffsets, txtWhiteOffsets, left, fieldsTop + fieldRowGap * 2);
            LayoutSettingsField(lblWhitePercents, txtWhitePercents, right, fieldsTop + fieldRowGap * 2);
            int tipsTop = fieldsTop + 120;
            tipsTop = LayoutWrappedLabel(lblTips, left, tipsTop, contentWidth, false) + 8;
            tipsTop = LayoutWrappedLabel(lblTips1, left, tipsTop, contentWidth, false) + 8;
            int footerTop = LayoutWrappedLabel(lblTips2, left, tipsTop, contentWidth, false) + footerGap;
            int confirmLeft = ClientSize.Width - left - confirmButtonWidth;
            int cancelLeft = confirmLeft - buttonGap - confirmButtonWidth;
            btnReset.SetBounds(left, footerTop, resetButtonWidth, buttonHeight);
            btnCancel.SetBounds(cancelLeft, footerTop, confirmButtonWidth, buttonHeight);
            btnConfirm.SetBounds(confirmLeft, footerTop, confirmButtonWidth, buttonHeight);
            ClientSize = new Size(560, btnConfirm.Bottom + 18);
        }

        private void LayoutSettingsField(Label label, TextBox textBox, int left, int top)
        {
            label.SetBounds(left, top + 4, FieldLabelWidth, 20);
            textBox.SetBounds(left + FieldLabelWidth + 8, top, FieldInputWidth, 24);
        }

        private int LayoutWrappedLabel(Label label, int left, int top, int width, bool notice)
        {
            label.AutoSize = true;
            label.MaximumSize = new Size(width, 0);
            label.Location = new Point(left, top);
            if (notice)
                label.MinimumSize = new Size(width, 0);
            return label.Bottom;
        }

        private void ApplyClassicSettingsTheme()
        {
            BackColor = SystemColors.Control;
            ForeColor = SystemColors.ControlText;
            Font = Control.DefaultFont;

            foreach (CheckBox checkBox in new[] { chkAutoMin, chkPonder, chkMag, chkEnhanceScreen, chkVerifyMove })
            {
                checkBox.BackColor = SystemColors.Control;
                checkBox.ForeColor = SystemColors.ControlText;
                checkBox.Font = Control.DefaultFont;
                checkBox.Cursor = Cursors.Default;
                checkBox.FlatStyle = FlatStyle.Standard;
                checkBox.UseVisualStyleBackColor = true;
            }

            foreach (TextBox textBox in new[] { txtSyncInterval, txtGrayOffsets, txtBlackOffsets, txtBlackPercents, txtWhiteOffsets, txtWhitePercents })
            {
                textBox.BackColor = SystemColors.Window;
                textBox.ForeColor = SystemColors.WindowText;
                textBox.Font = Control.DefaultFont;
                textBox.BorderStyle = BorderStyle.Fixed3D;
                textBox.TextAlign = HorizontalAlignment.Center;
            }

            foreach (Label label in new[] { lblSyncInterval, lblGrayOffsets, lblBlackOffsets, lblBlackPercents, lblWhiteOffsets, lblWhitePercents, lblBackForeOnly, lblTips, lblTips1, lblTips2 })
            {
                label.BackColor = Color.Transparent;
                label.ForeColor = SystemColors.ControlText;
                label.Font = Control.DefaultFont;
                label.BorderStyle = BorderStyle.None;
                label.Padding = Padding.Empty;
            }

            foreach (Button button in new[] { btnReset, btnCancel, btnConfirm })
            {
                button.FlatStyle = FlatStyle.System;
                button.UseVisualStyleBackColor = true;
                button.Font = Control.DefaultFont;
                button.Cursor = Cursors.Default;
            }
        }

        private String getLangStr(String itemName)
        {
            String result = "";
            try
            {
                result = Program.CurrentContext.LanguageItems[itemName].ToString();
            }
            catch (Exception e)
            {
                GetHost().SendError(e.ToString());
            }
            return result;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            AppConfig updatedConfig;
            if (!TryBuildUpdatedConfig(out updatedConfig))
                return;

            Program.CurrentContext.Config = updatedConfig;
            MainForm mainForm = GetHost();
            mainForm.PersistConfiguration();
            mainForm.resetBtnKeepSyncName();
            mainForm.sendPonderStatus();
            Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();          
        }

        private void button4_Click(object sender, EventArgs e)
        {
            AppConfig currentConfig = Program.CurrentConfig;
            AppConfig defaultConfig = AppConfig.CreateDefault(currentConfig.ProtocolVersion, currentConfig.MachineKey);
            Program.SaveAppConfig(defaultConfig);
            MessageBox.Show(getLangStr("SettingsForm_resetDefaultTip"));// Program.isChn ? "已恢复默认设置,请重新打开": "Reset successfully,please restart.");
            GetHost().shutdown(false);
        }

        private void LoadConfigValues(AppConfig config)
        {
            txtBlackOffsets.Text = config.BlackOffset.ToString();
            txtBlackPercents.Text = config.BlackPercent.ToString();
            txtWhiteOffsets.Text = config.WhiteOffset.ToString();
            txtWhitePercents.Text = config.WhitePercent.ToString();
            chkMag.Checked = config.UseMagnifier;
            chkVerifyMove.Checked = config.VerifyMove;
            chkAutoMin.Checked = config.AutoMinimize;
            txtSyncInterval.Text = config.SyncIntervalMs.ToString();
            chkEnhanceScreen.Checked = config.UseEnhanceScreen;
            txtGrayOffsets.Text = config.GrayOffset.ToString();
            chkPonder.Checked = config.PlayPonder;
        }

        private bool TryBuildUpdatedConfig(out AppConfig updatedConfig)
        {
            updatedConfig = Program.CurrentConfig.Clone();
            int blackOffset;
            int blackPercent;
            int whiteOffset;
            int whitePercent;
            int syncInterval;
            int grayOffset;
            if (!int.TryParse(txtBlackOffsets.Text, out blackOffset)
                || !int.TryParse(txtBlackPercents.Text, out blackPercent)
                || !int.TryParse(txtWhiteOffsets.Text, out whiteOffset)
                || !int.TryParse(txtWhitePercents.Text, out whitePercent)
                || !int.TryParse(txtSyncInterval.Text, out syncInterval)
                || !int.TryParse(txtGrayOffsets.Text, out grayOffset))
            {
                MessageBox.Show(getLangStr("SettingsForm_mustBeInteger"));
                return false;
            }

            updatedConfig.BlackOffset = blackOffset;
            updatedConfig.BlackPercent = blackPercent;
            updatedConfig.WhiteOffset = whiteOffset;
            updatedConfig.WhitePercent = whitePercent;
            updatedConfig.SyncIntervalMs = syncInterval;
            updatedConfig.GrayOffset = grayOffset;
            updatedConfig.UseMagnifier = chkMag.Checked;
            updatedConfig.VerifyMove = chkVerifyMove.Checked;
            updatedConfig.AutoMinimize = chkAutoMin.Checked;
            updatedConfig.UseEnhanceScreen = chkEnhanceScreen.Checked;
            updatedConfig.PlayPonder = chkPonder.Checked;
            if (IsOffsetOrPercentOutOfRange(updatedConfig))
            {
                MessageBox.Show(getLangStr("SettingsForm_outOfRange"));
                return false;
            }
            return true;
        }

        private static bool IsOffsetOrPercentOutOfRange(AppConfig config)
        {
            return config.BlackOffset > 255 || config.BlackOffset < 0
                || config.BlackPercent > 100 || config.BlackPercent < 0
                || config.WhiteOffset > 255 || config.WhiteOffset < 0
                || config.WhitePercent > 100 || config.WhitePercent < 0;
        }

        private MainForm GetHost()
        {
            if (host.IsDisposed)
                throw new InvalidOperationException("MainForm host is unavailable.");
            return host;
        }

        private static MainForm RequireHost(MainForm host)
        {
            if (host == null || host.IsDisposed)
                throw new InvalidOperationException("MainForm host is unavailable.");
            return host;
        }
    }
}
