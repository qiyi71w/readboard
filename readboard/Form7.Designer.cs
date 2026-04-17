namespace readboard
{
    partial class TipsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TipsForm));
            this.btnConfirm = new System.Windows.Forms.Button();
            this.btnNotAskAgain = new System.Windows.Forms.Button();
            this.lblTips = new System.Windows.Forms.Label();
            this.lblTips1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // btnConfirm
            // 
            this.btnConfirm.Location = new System.Drawing.Point(167, 52);
            this.btnConfirm.Margin = new System.Windows.Forms.Padding(2);
            this.btnConfirm.Name = "btnConfirm";
            this.btnConfirm.Size = new System.Drawing.Size(69, 22);
            this.btnConfirm.TabIndex = 0;
            this.btnConfirm.Text = "确定";
            this.btnConfirm.UseVisualStyleBackColor = true;
            this.btnConfirm.Click += new System.EventHandler(this.button1_Click);
            // 
            // btnNotAskAgain
            // 
            this.btnNotAskAgain.Location = new System.Drawing.Point(240, 52);
            this.btnNotAskAgain.Margin = new System.Windows.Forms.Padding(2);
            this.btnNotAskAgain.Name = "btnNotAskAgain";
            this.btnNotAskAgain.Size = new System.Drawing.Size(84, 22);
            this.btnNotAskAgain.TabIndex = 1;
            this.btnNotAskAgain.Text = "不再提示";
            this.btnNotAskAgain.UseVisualStyleBackColor = true;
            this.btnNotAskAgain.Click += new System.EventHandler(this.button2_Click);
            // 
            // lblTips
            // 
            this.lblTips.AutoSize = true;
            this.lblTips.Location = new System.Drawing.Point(10, 10);
            this.lblTips.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblTips.Name = "lblTips";
            this.lblTips.Size = new System.Drawing.Size(503, 12);
            this.lblTips.TabIndex = 2;
            this.lblTips.Text = "注: 快捷键Ctrl+D,[前台]方式同步时不支持此功能,选点显示在原棋盘上后,原棋盘将无法落子";
            // 
            // lblTips1
            // 
            this.lblTips1.AutoSize = true;
            this.lblTips1.Location = new System.Drawing.Point(33, 30);
            this.lblTips1.Name = "lblTips1";
            this.lblTips1.Size = new System.Drawing.Size(209, 12);
            this.lblTips1.TabIndex = 3;
            this.lblTips1.Text = "可通过勾选双向同步选项恢复落子功能";
            // 
            // TipsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(513, 82);
            this.Controls.Add(this.lblTips1);
            this.Controls.Add(this.lblTips);
            this.Controls.Add(this.btnNotAskAgain);
            this.Controls.Add(this.btnConfirm);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "TipsForm";
            this.Text = "提示";
            this.TopMost = true;
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnConfirm;
        private System.Windows.Forms.Button btnNotAskAgain;
        private System.Windows.Forms.Label lblTips;
        private System.Windows.Forms.Label lblTips1;
    }
}