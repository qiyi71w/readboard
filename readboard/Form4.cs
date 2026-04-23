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
        private static readonly Size SettingsDefaultClientSize = new Size(560, 386);
        private static readonly Size SettingsMinimumClientSize = new Size(420, 320);
        private readonly MainForm host;
        private bool isApplyingSettingsLayout;

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
            this.chkDisableShowInBoardShortcut.Text = getLangStr("SettingsForm_chkDisableShowInBoardShortcut");
            this.lblColorMode.Text = getLangStr("SettingsForm_lblColorMode");
            this.rdoColorSystem.Text = getLangStr("SettingsForm_rdoColorSystem");
            this.rdoColorDark.Text = getLangStr("SettingsForm_rdoColorDark");
            this.rdoColorLight.Text = getLangStr("SettingsForm_rdoColorLight");

           // this.Size= new Size((int)(461 *Program.factor), (int)(270 * Program.factor));
           

            var toolTip1 = new ToolTip();
            toolTip1.SetToolTip(this.chkEnhanceScreen, getLangStr("SettingsForm_chkEnhanceScreen_ToolTip"));
            var toolTip2 = new ToolTip();
            toolTip2.SetToolTip(this.chkPonder, getLangStr("SettingsForm_chkPonder_ToolTip"));
            ApplySettingsFormUi();
        }

        private void ApplySettingsFormUi()
        {
            if (isApplyingSettingsLayout)
                return;

            isApplyingSettingsLayout = true;
            SuspendLayout();
            try
            {
                DoubleBuffered = true;
                AutoScroll = true;
                AcceptButton = btnConfirm;
                CancelButton = btnCancel;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                label5.Visible = false;
                ConstrainSettingsClientSize();
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
            }
            finally
            {
                ResumeLayout(false);
                PerformLayout();
                isApplyingSettingsLayout = false;
            }
        }

        private void ApplySettingsTheme()
        {
            foreach (CheckBox checkBox in new[] { chkAutoMin, chkPonder, chkMag, chkEnhanceScreen, chkVerifyMove, chkDisableShowInBoardShortcut })
                UiTheme.StyleOption(checkBox);

            foreach (RadioButton radio in new[] { rdoColorSystem, rdoColorDark, rdoColorLight })
                UiTheme.StyleOption(radio);

            lblColorMode.ForeColor = UiTheme.PrimaryText;
            lblColorMode.Font = UiTheme.BodyFont;

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
            if (CanUseLegacySettingsDesktopLayout())
            {
                ArrangeLegacySettingsLayout();
                return;
            }

            ArrangeAdaptiveSettingsLayout();
        }

        private void ArrangeLegacySettingsLayout()
        {
            int left = ScaleValue(18);
            int top = ScaleValue(18);
            int fieldRowGap = ScaleValue(38);
            int footerGap = ScaleValue(14);
            int buttonHeight = ScaleValue(32);
            int buttonGap = ScaleValue(12);
            int optionRowGap = ScaleValue(30);
            int contentWidth = ClientSize.Width - left * 2;
            int labelWidth = ScaleValue(FieldLabelWidth);
            int inputWidth = ScaleValue(FieldInputWidth);
            int fieldGap = ScaleValue(8);
            int right = ClientSize.Width - left - labelWidth - fieldGap - inputWidth;

            ConfigureLegacyOptionCheckBox(chkAutoMin);
            ConfigureLegacyOptionCheckBox(chkPonder);
            ConfigureLegacyOptionCheckBox(chkMag);
            ConfigureLegacyOptionCheckBox(chkEnhanceScreen);
            ConfigureLegacyOptionCheckBox(chkVerifyMove);
            ConfigureLegacyOptionCheckBox(chkDisableShowInBoardShortcut);

            chkAutoMin.Location = new Point(left, top);
            chkPonder.Location = new Point(ScaleValue(170), top);
            chkMag.Location = new Point(left, top + optionRowGap);
            chkEnhanceScreen.Location = new Point(ScaleValue(170), top + optionRowGap);
            chkVerifyMove.Location = new Point(left, top + optionRowGap * 2);
            chkDisableShowInBoardShortcut.Location = new Point(ScaleValue(170), top + optionRowGap * 2);
            LayoutColorModeRow(left, top + optionRowGap * 3);
            int fieldsTop = LayoutWrappedLabel(lblBackForeOnly, left, ScaleValue(140), contentWidth, true) + ScaleValue(20);
            LayoutSettingsField(lblSyncInterval, txtSyncInterval, left, fieldsTop, labelWidth, inputWidth, fieldGap, ScaleValue(24));
            LayoutSettingsField(lblGrayOffsets, txtGrayOffsets, right, fieldsTop, labelWidth, inputWidth, fieldGap, ScaleValue(24));
            LayoutSettingsField(lblBlackOffsets, txtBlackOffsets, left, fieldsTop + fieldRowGap, labelWidth, inputWidth, fieldGap, ScaleValue(24));
            LayoutSettingsField(lblBlackPercents, txtBlackPercents, right, fieldsTop + fieldRowGap, labelWidth, inputWidth, fieldGap, ScaleValue(24));
            LayoutSettingsField(lblWhiteOffsets, txtWhiteOffsets, left, fieldsTop + fieldRowGap * 2, labelWidth, inputWidth, fieldGap, ScaleValue(24));
            LayoutSettingsField(lblWhitePercents, txtWhitePercents, right, fieldsTop + fieldRowGap * 2, labelWidth, inputWidth, fieldGap, ScaleValue(24));
            int tipsTop = fieldsTop + fieldRowGap * 3 + ScaleValue(6);
            tipsTop = LayoutWrappedLabel(lblTips, left, tipsTop, contentWidth, false) + ScaleValue(8);
            tipsTop = LayoutWrappedLabel(lblTips1, left, tipsTop, contentWidth, false) + ScaleValue(8);
            int footerTop = LayoutWrappedLabel(lblTips2, left, tipsTop, contentWidth, false) + footerGap;
            int resetButtonWidth = MeasureButtonWidth(btnReset, 124);
            int cancelButtonWidth = MeasureButtonWidth(btnCancel, 84);
            int confirmButtonWidth = MeasureButtonWidth(btnConfirm, 84);
            int confirmLeft = ClientSize.Width - left - confirmButtonWidth;
            int cancelLeft = confirmLeft - buttonGap - cancelButtonWidth;
            btnReset.SetBounds(left, footerTop, resetButtonWidth, buttonHeight);
            btnCancel.SetBounds(cancelLeft, footerTop, cancelButtonWidth, buttonHeight);
            btnConfirm.SetBounds(confirmLeft, footerTop, confirmButtonWidth, buttonHeight);
            ApplySettingsClientHeight(btnConfirm.Bottom + ScaleValue(18));
        }

        private void ArrangeAdaptiveSettingsLayout()
        {
            int left = ScaleValue(18);
            int top = ScaleValue(18);
            int optionGap = ScaleValue(18);
            int optionRowGap = ScaleValue(10);
            int fieldRowGap = ScaleValue(36);
            int fieldGap = ScaleValue(8);
            int footerGap = ScaleValue(14);
            int buttonHeight = ScaleValue(32);
            int buttonGap = ScaleValue(12);
            int bottomPadding = ScaleValue(18);
            int contentWidth = ClientSize.Width - left * 2;
            int currentTop = top;

            currentTop = LayoutOptionRow(chkAutoMin, chkPonder, left, currentTop, contentWidth, optionGap, optionRowGap);
            currentTop = LayoutOptionRow(chkMag, chkEnhanceScreen, left, currentTop, contentWidth, optionGap, optionRowGap);
            currentTop = LayoutOptionRow(chkVerifyMove, chkDisableShowInBoardShortcut, left, currentTop, contentWidth, optionGap, optionRowGap);

            currentTop = LayoutColorModeRow(left, currentTop) + optionRowGap;

            currentTop = LayoutWrappedLabel(lblBackForeOnly, left, currentTop + ScaleValue(8), contentWidth, true) + ScaleValue(16);

            Label[] fieldLabels = new[]
            {
                lblSyncInterval,
                lblGrayOffsets,
                lblBlackOffsets,
                lblBlackPercents,
                lblWhiteOffsets,
                lblWhitePercents
            };
            int labelWidth = GetMaxPreferredWidth(fieldLabels, ScaleValue(FieldLabelWidth));
            int inputWidth = ScaleValue(FieldInputWidth);
            int fieldColumnWidth = labelWidth + fieldGap + inputWidth;
            int fieldColumnGap = ScaleValue(20);
            bool useTwoFieldColumns = contentWidth >= fieldColumnWidth * 2 + fieldColumnGap;
            if (useTwoFieldColumns)
            {
                int rightColumnLeft = left + fieldColumnWidth + fieldColumnGap;
                LayoutSettingsField(lblSyncInterval, txtSyncInterval, left, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                LayoutSettingsField(lblGrayOffsets, txtGrayOffsets, rightColumnLeft, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                currentTop += fieldRowGap;
                LayoutSettingsField(lblBlackOffsets, txtBlackOffsets, left, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                LayoutSettingsField(lblBlackPercents, txtBlackPercents, rightColumnLeft, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                currentTop += fieldRowGap;
                LayoutSettingsField(lblWhiteOffsets, txtWhiteOffsets, left, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                LayoutSettingsField(lblWhitePercents, txtWhitePercents, rightColumnLeft, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                currentTop += fieldRowGap;
            }
            else
            {
                LayoutSettingsField(lblSyncInterval, txtSyncInterval, left, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                currentTop += fieldRowGap;
                LayoutSettingsField(lblGrayOffsets, txtGrayOffsets, left, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                currentTop += fieldRowGap;
                LayoutSettingsField(lblBlackOffsets, txtBlackOffsets, left, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                currentTop += fieldRowGap;
                LayoutSettingsField(lblBlackPercents, txtBlackPercents, left, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                currentTop += fieldRowGap;
                LayoutSettingsField(lblWhiteOffsets, txtWhiteOffsets, left, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                currentTop += fieldRowGap;
                LayoutSettingsField(lblWhitePercents, txtWhitePercents, left, currentTop, labelWidth, inputWidth, fieldGap, buttonHeight);
                currentTop += fieldRowGap;
            }

            currentTop = LayoutWrappedLabel(lblTips, left, currentTop, contentWidth, false) + ScaleValue(8);
            currentTop = LayoutWrappedLabel(lblTips1, left, currentTop, contentWidth, false) + ScaleValue(8);
            int footerTop = LayoutWrappedLabel(lblTips2, left, currentTop, contentWidth, false) + footerGap;

            int resetButtonWidth = MeasureButtonWidth(btnReset, 124);
            int cancelButtonWidth = MeasureButtonWidth(btnCancel, 84);
            int confirmButtonWidth = MeasureButtonWidth(btnConfirm, 84);
            if (resetButtonWidth + cancelButtonWidth + confirmButtonWidth + buttonGap * 2 <= contentWidth)
            {
                int confirmLeft = ClientSize.Width - left - confirmButtonWidth;
                int cancelLeft = confirmLeft - buttonGap - cancelButtonWidth;
                btnReset.SetBounds(left, footerTop, resetButtonWidth, buttonHeight);
                btnCancel.SetBounds(cancelLeft, footerTop, cancelButtonWidth, buttonHeight);
                btnConfirm.SetBounds(confirmLeft, footerTop, confirmButtonWidth, buttonHeight);
            }
            else
            {
                btnReset.SetBounds(left, footerTop, contentWidth, buttonHeight);
                btnCancel.SetBounds(left, btnReset.Bottom + optionRowGap, contentWidth, buttonHeight);
                btnConfirm.SetBounds(left, btnCancel.Bottom + optionRowGap, contentWidth, buttonHeight);
            }

            ApplySettingsClientHeight(btnConfirm.Bottom + bottomPadding);
        }

        private bool CanUseLegacySettingsDesktopLayout()
        {
            int left = ScaleValue(18);
            int fieldGap = ScaleValue(8);
            int fieldColumnGap = ScaleValue(20);
            int labelWidth = ScaleValue(FieldLabelWidth);
            int inputWidth = ScaleValue(FieldInputWidth);
            int contentWidth = ClientSize.Width - left * 2;
            int optionColumnLeft = ScaleValue(170);
            int requiredOptionWidth = GetLegacyOptionPreferredWidth(chkAutoMin, chkMag, chkVerifyMove);
            int requiredSecondOptionWidth = GetLegacyOptionPreferredWidth(chkPonder, chkEnhanceScreen, chkDisableShowInBoardShortcut);
            int requiredFieldsWidth = labelWidth * 2 + inputWidth * 2 + fieldGap * 2 + fieldColumnGap;
            int requiredFooterWidth =
                MeasureButtonWidth(btnReset, 124)
                + buttonGapForLegacyFooter()
                + MeasureButtonWidth(btnCancel, 84)
                + buttonGapForLegacyFooter()
                + MeasureButtonWidth(btnConfirm, 84);
            return contentWidth >= requiredFieldsWidth
                && optionColumnLeft + requiredSecondOptionWidth <= ClientSize.Width - left
                && left + requiredOptionWidth < optionColumnLeft - ScaleValue(12)
                && contentWidth >= requiredFooterWidth;
        }

        private int LayoutOptionRow(CheckBox primary, CheckBox secondary, int left, int top, int contentWidth, int optionGap, int optionRowGap)
        {
            ConfigureOptionCheckBox(primary);
            ConfigureOptionCheckBox(secondary);

            int primaryWidth = primary.PreferredSize.Width;
            int secondaryWidth = secondary.PreferredSize.Width;
            if (primaryWidth + secondaryWidth + optionGap <= contentWidth)
            {
                primary.Location = new Point(left, top);
                secondary.Location = new Point(left + primaryWidth + optionGap, top);
                return Math.Max(primary.Bottom, secondary.Bottom) + optionRowGap;
            }

            primary.Location = new Point(left, top);
            secondary.Location = new Point(left, primary.Bottom + optionRowGap);
            return secondary.Bottom + optionRowGap;
        }

        private void ConfigureOptionCheckBox(CheckBox checkBox)
        {
            checkBox.AutoSize = true;
            checkBox.MaximumSize = new Size(ClientSize.Width - ScaleValue(36), 0);
        }

        private int LayoutColorModeRow(int left, int top)
        {
            lblColorMode.AutoSize = true;
            lblColorMode.Location = new Point(left, top + ScaleValue(2));
            int radioLeft = lblColorMode.Right + ScaleValue(6);
            foreach (RadioButton radio in new[] { rdoColorSystem, rdoColorDark, rdoColorLight })
            {
                radio.AutoSize = true;
                radio.Location = new Point(radioLeft, top);
                radioLeft = radio.Right + ScaleValue(10);
            }
            return Math.Max(lblColorMode.Bottom, rdoColorLight.Bottom);
        }

        private void ConfigureLegacyOptionCheckBox(CheckBox checkBox)
        {
            checkBox.AutoSize = true;
            checkBox.MaximumSize = Size.Empty;
        }

        private int GetLegacyOptionPreferredWidth(params CheckBox[] checkBoxes)
        {
            int width = 0;
            foreach (CheckBox checkBox in checkBoxes)
            {
                ConfigureLegacyOptionCheckBox(checkBox);
                width = Math.Max(width, checkBox.PreferredSize.Width);
            }

            return width;
        }

        private void LayoutSettingsField(
            Label label,
            TextBox textBox,
            int left,
            int top,
            int labelWidth,
            int inputWidth,
            int fieldGap,
            int inputHeight)
        {
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.SetBounds(left, top + ScaleValue(4), labelWidth, ScaleValue(20));
            textBox.SetBounds(left + labelWidth + fieldGap, top, inputWidth, inputHeight);
        }

        private int LayoutWrappedLabel(Label label, int left, int top, int width, bool notice)
        {
            label.AutoSize = true;
            label.MaximumSize = new Size(width, 0);
            label.MinimumSize = notice ? new Size(width, 0) : Size.Empty;
            label.Location = new Point(left, top);
            return label.Bottom;
        }

        private void ApplyClassicSettingsTheme()
        {
            BackColor = SystemColors.Control;
            ForeColor = SystemColors.ControlText;
            Font = Control.DefaultFont;

            foreach (CheckBox checkBox in new[] { chkAutoMin, chkPonder, chkMag, chkEnhanceScreen, chkVerifyMove, chkDisableShowInBoardShortcut })
            {
                UiTheme.ResetOption(checkBox);
                checkBox.BackColor = SystemColors.Control;
                checkBox.ForeColor = SystemColors.ControlText;
                checkBox.Font = Control.DefaultFont;
                checkBox.Cursor = Cursors.Default;
                checkBox.FlatStyle = FlatStyle.Standard;
                checkBox.UseVisualStyleBackColor = true;
            }

            foreach (RadioButton radio in new[] { rdoColorSystem, rdoColorDark, rdoColorLight })
            {
                UiTheme.ResetOption(radio);
                radio.BackColor = SystemColors.Control;
                radio.ForeColor = SystemColors.ControlText;
                radio.Font = Control.DefaultFont;
                radio.Cursor = Cursors.Default;
                radio.FlatStyle = FlatStyle.Standard;
                radio.UseVisualStyleBackColor = true;
            }

            lblColorMode.BackColor = Color.Transparent;
            lblColorMode.ForeColor = SystemColors.ControlText;
            lblColorMode.Font = Control.DefaultFont;

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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplySettingsFormUi();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            ApplySettingsFormUi();
        }

        private void ConstrainSettingsClientSize()
        {
            Rectangle workingArea = GetCurrentWorkingArea();
            Size defaultSize = ScaleSize(SettingsDefaultClientSize);
            Size minimumSize = ScaleSize(SettingsMinimumClientSize);
            int availableWidth = Math.Max(ScaleValue(320), workingArea.Width - ScaleValue(48));
            int availableHeight = GetMaxSettingsClientHeight();
            int minimumWidth = Math.Min(minimumSize.Width, availableWidth);
            int minimumHeight = Math.Min(minimumSize.Height, availableHeight);
            ClientSize = new Size(
                Math.Max(minimumWidth, Math.Min(defaultSize.Width, availableWidth)),
                Math.Max(minimumHeight, Math.Min(defaultSize.Height, availableHeight)));
            MinimumSize = new Size(minimumWidth, minimumHeight);
        }

        private int GetMaxSettingsClientHeight()
        {
            Rectangle workingArea = GetCurrentWorkingArea();
            return Math.Max(ScaleValue(260), workingArea.Height - ScaleValue(64));
        }

        private void ApplySettingsClientHeight(int desiredHeight)
        {
            int maxHeight = GetMaxSettingsClientHeight();
            int constrainedHeight = Math.Min(desiredHeight, maxHeight);
            AutoScrollMinSize = desiredHeight > constrainedHeight
                ? new Size(0, desiredHeight)
                : Size.Empty;
            ClientSize = new Size(ClientSize.Width, constrainedHeight);
        }

        private Rectangle GetCurrentWorkingArea()
        {
            Point referencePoint = IsHandleCreated
                ? new Point(Left + Width / 2, Top + Height / 2)
                : new Point(Location.X, Location.Y);
            return DisplayScaling.GetScreenWorkingAreaFromPoint(referencePoint);
        }

        private int GetMaxPreferredWidth(Label[] labels, int minimumWidth)
        {
            int width = minimumWidth;
            foreach (Label label in labels)
                width = Math.Max(width, label.PreferredSize.Width);
            return width;
        }

        private int MeasureButtonWidth(Button button, int minimumLogicalWidth)
        {
            int minimumWidth = ScaleValue(minimumLogicalWidth);
            return Math.Max(minimumWidth, TextRenderer.MeasureText(button.Text, button.Font).Width + ScaleValue(28));
        }

        private int buttonGapForLegacyFooter()
        {
            return ScaleValue(12);
        }

        private int ScaleValue(int logicalValue)
        {
            double scale = IsHandleCreated
                ? DisplayScaling.GetScaleForWindow(Handle)
                : DisplayScaling.DefaultScale;
            return (int)Math.Round(logicalValue * DisplayScaling.NormalizeScale(scale));
        }

        private Size ScaleSize(Size logicalSize)
        {
            return new Size(ScaleValue(logicalSize.Width), ScaleValue(logicalSize.Height));
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

            bool colorModeChanged = updatedConfig.ColorMode != Program.CurrentConfig.ColorMode;
            Program.CurrentContext.Config = updatedConfig;
            MainForm mainForm = GetHost();
            mainForm.PersistConfiguration();
            mainForm.RefreshShowInBoardShortcutToolTip();
            mainForm.resetBtnKeepSyncName();
            mainForm.sendPonderStatus();
            Close();
            if (colorModeChanged)
            {
                MessageBox.Show(
                    mainForm,
                    getLangStr("SettingsForm_colorModeRestartTip"),
                    getLangStr("SettingsForm_title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
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
            chkDisableShowInBoardShortcut.Checked = config.DisableShowInBoardShortcut;
            txtGrayOffsets.Text = config.GrayOffset.ToString();
            chkPonder.Checked = config.PlayPonder;
            rdoColorSystem.Checked = config.ColorMode == AppConfig.ColorModeSystem;
            rdoColorDark.Checked = config.ColorMode == AppConfig.ColorModeDark;
            rdoColorLight.Checked = config.ColorMode == AppConfig.ColorModeLight;
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
            updatedConfig.DisableShowInBoardShortcut = chkDisableShowInBoardShortcut.Checked;
            updatedConfig.ColorMode = rdoColorDark.Checked ? AppConfig.ColorModeDark
                : rdoColorLight.Checked ? AppConfig.ColorModeLight
                : AppConfig.ColorModeSystem;
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
