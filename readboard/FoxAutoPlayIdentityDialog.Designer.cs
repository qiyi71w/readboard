namespace readboard
{
    partial class FoxAutoPlayIdentityDialog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.lblPrompt = new System.Windows.Forms.Label();
            this.lblDetectedNicknames = new System.Windows.Forms.Label();
            this.pnlDetectedPlayerRows = new System.Windows.Forms.Panel();
            this.btnUseOnce = new System.Windows.Forms.Button();
            this.btnSaveAndUse = new System.Windows.Forms.Button();
            this.btnClearSavedIdentity = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // lblPrompt
            //
            this.lblPrompt.Location = new System.Drawing.Point(16, 16);
            this.lblPrompt.Name = "lblPrompt";
            this.lblPrompt.Size = new System.Drawing.Size(500, 36);
            this.lblPrompt.TabIndex = 0;
            this.lblPrompt.Text = "请选择你在野狐当前房间里的玩家行。";
            //
            // lblDetectedNicknames
            //
            this.lblDetectedNicknames.AutoSize = true;
            this.lblDetectedNicknames.Location = new System.Drawing.Point(16, 58);
            this.lblDetectedNicknames.Name = "lblDetectedNicknames";
            this.lblDetectedNicknames.Size = new System.Drawing.Size(65, 12);
            this.lblDetectedNicknames.TabIndex = 1;
            this.lblDetectedNicknames.Text = "可选玩家行";
            //
            // pnlDetectedPlayerRows
            //
            this.pnlDetectedPlayerRows.AutoScroll = true;
            this.pnlDetectedPlayerRows.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlDetectedPlayerRows.Location = new System.Drawing.Point(16, 76);
            this.pnlDetectedPlayerRows.Name = "pnlDetectedPlayerRows";
            this.pnlDetectedPlayerRows.Size = new System.Drawing.Size(500, 154);
            this.pnlDetectedPlayerRows.TabIndex = 2;
            //
            // btnUseOnce
            //
            this.btnUseOnce.Location = new System.Drawing.Point(124, 248);
            this.btnUseOnce.Name = "btnUseOnce";
            this.btnUseOnce.Size = new System.Drawing.Size(90, 26);
            this.btnUseOnce.TabIndex = 3;
            this.btnUseOnce.Text = "本次使用";
            this.btnUseOnce.UseVisualStyleBackColor = true;
            this.btnUseOnce.Click += new System.EventHandler(this.btnUseOnce_Click);
            //
            // btnSaveAndUse
            //
            this.btnSaveAndUse.Location = new System.Drawing.Point(220, 248);
            this.btnSaveAndUse.Name = "btnSaveAndUse";
            this.btnSaveAndUse.Size = new System.Drawing.Size(96, 26);
            this.btnSaveAndUse.TabIndex = 4;
            this.btnSaveAndUse.Text = "保存并使用";
            this.btnSaveAndUse.UseVisualStyleBackColor = true;
            this.btnSaveAndUse.Click += new System.EventHandler(this.btnSaveAndUse_Click);
            //
            // btnClearSavedIdentity
            //
            this.btnClearSavedIdentity.Location = new System.Drawing.Point(322, 248);
            this.btnClearSavedIdentity.Name = "btnClearSavedIdentity";
            this.btnClearSavedIdentity.Size = new System.Drawing.Size(90, 26);
            this.btnClearSavedIdentity.TabIndex = 5;
            this.btnClearSavedIdentity.Text = "清除保存";
            this.btnClearSavedIdentity.UseVisualStyleBackColor = true;
            this.btnClearSavedIdentity.Click += new System.EventHandler(this.btnClearSavedIdentity_Click);
            //
            // btnCancel
            //
            this.btnCancel.Location = new System.Drawing.Point(418, 248);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 26);
            this.btnCancel.TabIndex = 6;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // FoxAutoPlayIdentityDialog
            //
            this.AcceptButton = this.btnSaveAndUse;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(532, 292);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnClearSavedIdentity);
            this.Controls.Add(this.btnSaveAndUse);
            this.Controls.Add(this.btnUseOnce);
            this.Controls.Add(this.pnlDetectedPlayerRows);
            this.Controls.Add(this.lblDetectedNicknames);
            this.Controls.Add(this.lblPrompt);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FoxAutoPlayIdentityDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "野狐自动模式";
            this.TopMost = true;
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label lblPrompt;
        private System.Windows.Forms.Label lblDetectedNicknames;
        private System.Windows.Forms.Panel pnlDetectedPlayerRows;
        private System.Windows.Forms.Button btnUseOnce;
        private System.Windows.Forms.Button btnSaveAndUse;
        private System.Windows.Forms.Button btnClearSavedIdentity;
        private System.Windows.Forms.Button btnCancel;
    }
}
