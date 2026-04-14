using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace readboard
{
    public partial class TipsForm : Form
    {
        public TipsForm()
        {
            InitializeComponent();
            this.Text = getLangStr("TipsForm_title");
            this.lblTips.Text = getLangStr("TipsForm_lblTips");
            this.lblTips1.Text = getLangStr("TipsForm_lblTips1");
            this.btnConfirm.Text = getLangStr("TipsForm_btnConfirm");
            this.btnNotAskAgain.Text = getLangStr("TipsForm_btnNotAskAgain");
            ApplyTipsFormUi();
        }

        private void ApplyTipsFormUi()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            ClientSize = new Size(472, 160);
            AcceptButton = btnConfirm;
            CancelButton = btnConfirm;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            if (Program.uiThemeMode == Program.UiThemeOptimized)
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
            ResumeLayout(false);
            PerformLayout();
        }

        private void ArrangeTipsLayout()
        {
            const int left = 18;
            const int buttonGap = 12;
            int contentWidth = ClientSize.Width - left * 2;

            lblTips.TextAlign = ContentAlignment.MiddleLeft;
            lblTips1.TextAlign = ContentAlignment.MiddleLeft;
            int textBottom = LayoutWrappedLabel(lblTips, left, 18, contentWidth, true) + 10;
            int footerTop = LayoutWrappedLabel(lblTips1, left, textBottom, contentWidth, false) + 18;
            int secondaryWidth = Math.Max(118, TextRenderer.MeasureText(btnNotAskAgain.Text, btnNotAskAgain.Font).Width + 28);
            int primaryWidth = Math.Max(96, TextRenderer.MeasureText(btnConfirm.Text, btnConfirm.Font).Width + 28);
            int confirmLeft = ClientSize.Width - left - primaryWidth;
            btnNotAskAgain.SetBounds(confirmLeft - buttonGap - secondaryWidth, footerTop, secondaryWidth, 32);
            btnConfirm.SetBounds(confirmLeft, footerTop, primaryWidth, 32);
            ClientSize = new Size(472, btnConfirm.Bottom + 18);
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

        private String getLangStr(String itemName)
        {
            String result = "";
            try
            {
                result = Program.langItems[itemName].ToString();
            }
            catch (Exception e)
            {
                MainForm.pcurrentWin.SendError(e.ToString());
            }
            return result;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Program.showInBoardHint = false;
            string result1 = "config_readboard.txt";
            FileStream fs = new FileStream(result1, FileMode.Create);
            StreamWriter wr = null;
            wr = new StreamWriter(fs);
            wr.WriteLine(Program.blackPC.ToString() + "_" + Program.blackZB.ToString() + "_" + Program.whitePC.ToString() + "_" + Program.whiteZB.ToString() + "_" + (Program.useMag ? "1" : "0") + "_" + (Program.verifyMove ? "1" : "0") + "_" + (Program.showScaleHint ? "1" : "0") + "_" + (Program.showInBoard ? "1" : "0") + "_" + (Program.showInBoardHint ? "1" : "0") + "_" + (Program.autoMin ? "1" : "0") + "_" + Environment.GetEnvironmentVariable("computername").Replace("_", "")+"_" + MainForm.type);
            wr.Close();
            this.Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
