using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace readboard
{
    public partial class TipsForm : Form
    {
        private static readonly Size TipsDefaultClientSize = new Size(513, 118);
        private bool isApplyingTipsLayout;

        internal TipsForm(MainForm host)
        {
            InitializeComponent();
            this.host = RequireHost(host);
            this.Text = getLangStr("TipsForm_title");
            this.lblTips.Text = getLangStr("TipsForm_lblTips");
            this.lblTips1.Text = getLangStr("TipsForm_lblTips1");
            this.btnConfirm.Text = getLangStr("TipsForm_btnConfirm");
            this.btnNotAskAgain.Text = getLangStr("TipsForm_btnNotAskAgain");
            ApplyTipsFormUi();
        }

        private readonly MainForm host;

        private void ApplyTipsFormUi()
        {
            if (isApplyingTipsLayout)
                return;

            isApplyingTipsLayout = true;
            SuspendLayout();
            try
            {
                DoubleBuffered = true;
                AutoScroll = true;
                AcceptButton = btnConfirm;
                CancelButton = btnConfirm;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterParent;
                ConstrainTipsClientSize();
                if (Program.CurrentConfig.UiThemeMode == Program.UiThemeOptimized)
                {
                    UiTheme.ApplyWindow(this);
                    UiTheme.StyleNoticeLabel(lblTips);
                    UiTheme.StyleSubtleLabel(lblTips1);
                    UiTheme.StyleSecondaryButton(btnNotAskAgain);
                    UiTheme.StylePrimaryButton(btnConfirm);
                }
                else
                {
                    ApplyClassicTipsTheme();
                }
                ArrangeTipsLayout();
            }
            finally
            {
                ResumeLayout(false);
                PerformLayout();
                isApplyingTipsLayout = false;
            }
        }

        private void ArrangeTipsLayout()
        {
            if (CanUseLegacyTipsDesktopLayout())
            {
                ArrangeLegacyTipsLayout();
                return;
            }

            ArrangeAdaptiveTipsLayout();
        }

        private void ArrangeLegacyTipsLayout()
        {
            int left = ScaleValue(10);
            int top = ScaleValue(10);
            int secondaryIndent = ScaleValue(23);
            int buttonGap = ScaleValue(8);
            int buttonHeight = ScaleValue(30);
            int bottomPadding = ScaleValue(10);
            int contentWidth = ClientSize.Width - left;
            int secondaryTextLeft = left + secondaryIndent;
            int secondaryTextWidth = Math.Max(ScaleValue(180), ClientSize.Width - secondaryTextLeft - ScaleValue(12));
            int primaryWidth = MeasureButtonWidth(btnConfirm, 76);
            int secondaryWidth = MeasureButtonWidth(btnNotAskAgain, 92);
            int footerWidth = primaryWidth + buttonGap + secondaryWidth;

            lblTips.TextAlign = ContentAlignment.MiddleLeft;
            lblTips1.TextAlign = ContentAlignment.MiddleLeft;
            int textBottom = LayoutWrappedLabel(lblTips, left, top, contentWidth, true) + ScaleValue(8);
            int footerTop = LayoutWrappedLabel(lblTips1, secondaryTextLeft, textBottom, secondaryTextWidth, false) + ScaleValue(10);
            int footerLeft = Math.Max(left, (ClientSize.Width - footerWidth) / 2);
            btnConfirm.SetBounds(footerLeft, footerTop, primaryWidth, buttonHeight);
            btnNotAskAgain.SetBounds(btnConfirm.Right + buttonGap, footerTop, secondaryWidth, buttonHeight);
            ApplyTipsClientHeight(Math.Max(btnConfirm.Bottom, btnNotAskAgain.Bottom) + bottomPadding);
        }

        private void ArrangeAdaptiveTipsLayout()
        {
            int left = ScaleValue(18);
            int top = ScaleValue(18);
            int buttonGap = ScaleValue(12);
            int rowGap = ScaleValue(10);
            int buttonHeight = ScaleValue(32);
            int bottomPadding = ScaleValue(18);
            int contentWidth = ClientSize.Width - left * 2;
            int primaryWidth = MeasureButtonWidth(btnConfirm, 96);
            int secondaryWidth = MeasureButtonWidth(btnNotAskAgain, 118);

            lblTips.TextAlign = ContentAlignment.MiddleLeft;
            lblTips1.TextAlign = ContentAlignment.MiddleLeft;
            int textBottom = LayoutWrappedLabel(lblTips, left, top, contentWidth, true) + ScaleValue(10);
            int footerTop = LayoutWrappedLabel(lblTips1, left, textBottom, contentWidth, false) + ScaleValue(18);
            if (primaryWidth + secondaryWidth + buttonGap <= contentWidth)
            {
                int footerLeft = ClientSize.Width - left - primaryWidth - buttonGap - secondaryWidth;
                btnConfirm.SetBounds(footerLeft, footerTop, primaryWidth, buttonHeight);
                btnNotAskAgain.SetBounds(btnConfirm.Right + buttonGap, footerTop, secondaryWidth, buttonHeight);
            }
            else
            {
                btnConfirm.SetBounds(left, footerTop, contentWidth, buttonHeight);
                btnNotAskAgain.SetBounds(left, btnConfirm.Bottom + rowGap, contentWidth, buttonHeight);
            }

            ApplyTipsClientHeight(Math.Max(btnConfirm.Bottom, btnNotAskAgain.Bottom) + bottomPadding);
        }

        private bool CanUseLegacyTipsDesktopLayout()
        {
            int left = ScaleValue(10);
            int secondaryIndent = ScaleValue(23);
            int requiredFooterWidth = MeasureButtonWidth(btnConfirm, 76) + ScaleValue(8) + MeasureButtonWidth(btnNotAskAgain, 92);
            int requiredTextWidth = Math.Max(
                MeasureSingleLineLabelWidth(lblTips) + left,
                MeasureSingleLineLabelWidth(lblTips1) + left + secondaryIndent);
            return ClientSize.Width >= Math.Max(ScaleValue(513), Math.Max(requiredFooterWidth + ScaleValue(36), requiredTextWidth));
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

        private void ApplyClassicTipsTheme()
        {
            BackColor = SystemColors.Control;
            ForeColor = SystemColors.ControlText;
            Font = Control.DefaultFont;

            foreach (Label label in new[] { lblTips, lblTips1 })
            {
                label.BackColor = Color.Transparent;
                label.ForeColor = SystemColors.ControlText;
                label.Font = Control.DefaultFont;
                label.BorderStyle = BorderStyle.None;
                label.Padding = Padding.Empty;
            }

            foreach (Button button in new[] { btnNotAskAgain, btnConfirm })
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
            ApplyTipsFormUi();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            ApplyTipsFormUi();
        }

        private void ConstrainTipsClientSize()
        {
            Rectangle workingArea = GetCurrentWorkingArea();
            Size defaultSize = ScaleSize(TipsDefaultClientSize);
            int availableWidth = Math.Max(ScaleValue(320), workingArea.Width - ScaleValue(48));
            int minimumWidth = Math.Min(ScaleValue(320), availableWidth);
            int targetWidth = Math.Max(minimumWidth, Math.Min(defaultSize.Width, availableWidth));
            ClientSize = new Size(targetWidth, Math.Min(defaultSize.Height, GetMaxTipsClientHeight()));
        }

        private int GetMaxTipsClientHeight()
        {
            Rectangle workingArea = GetCurrentWorkingArea();
            return Math.Max(ScaleValue(120), workingArea.Height - ScaleValue(48));
        }

        private void ApplyTipsClientHeight(int desiredHeight)
        {
            int maxHeight = GetMaxTipsClientHeight();
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

        private int MeasureButtonWidth(Button button, int minimumLogicalWidth)
        {
            int minimumWidth = ScaleValue(minimumLogicalWidth);
            return Math.Max(minimumWidth, TextRenderer.MeasureText(button.Text, button.Font).Width + ScaleValue(28));
        }

        private int MeasureSingleLineLabelWidth(Label label)
        {
            return TextRenderer.MeasureText(label.Text, label.Font).Width;
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

        private void button2_Click(object sender, EventArgs e)
        {
            AppConfig updatedConfig = Program.CurrentConfig.Clone();
            updatedConfig.ShowInBoardHint = false;
            Program.CurrentContext.Config = updatedConfig;
            GetHost().PersistConfiguration();
            Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Close();
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
