namespace readboard
{
    partial class FormUpdate
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.Button btnDownload;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private System.Windows.Forms.Label lblCurrentVersion;
        private System.Windows.Forms.Label lblCurrentVersionValue;
        private System.Windows.Forms.Label lblLatestVersion;
        private System.Windows.Forms.Label lblLatestVersionValue;
        private System.Windows.Forms.Label lblReleaseDate;
        private System.Windows.Forms.Label lblReleaseDateValue;
        private System.Windows.Forms.Label lblReleaseNotes;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.TableLayoutPanel infoPanel;
        private System.Windows.Forms.TableLayoutPanel rootPanel;
        private System.Windows.Forms.TextBox txtReleaseNotes;

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
        /// Required method for Designer support.
        /// </summary>
        private void InitializeComponent()
        {
            btnClose = new System.Windows.Forms.Button();
            btnDownload = new System.Windows.Forms.Button();
            buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            lblCurrentVersion = new System.Windows.Forms.Label();
            lblCurrentVersionValue = new System.Windows.Forms.Label();
            lblLatestVersion = new System.Windows.Forms.Label();
            lblLatestVersionValue = new System.Windows.Forms.Label();
            lblReleaseDate = new System.Windows.Forms.Label();
            lblReleaseDateValue = new System.Windows.Forms.Label();
            lblReleaseNotes = new System.Windows.Forms.Label();
            lblTitle = new System.Windows.Forms.Label();
            infoPanel = new System.Windows.Forms.TableLayoutPanel();
            rootPanel = new System.Windows.Forms.TableLayoutPanel();
            txtReleaseNotes = new System.Windows.Forms.TextBox();
            buttonPanel.SuspendLayout();
            infoPanel.SuspendLayout();
            rootPanel.SuspendLayout();
            SuspendLayout();
            InitializeButtons();
            InitializeInfoPanel();
            InitializeRootPanel();
            InitializeForm();
            buttonPanel.ResumeLayout(false);
            infoPanel.ResumeLayout(false);
            infoPanel.PerformLayout();
            rootPanel.ResumeLayout(false);
            rootPanel.PerformLayout();
            ResumeLayout(false);
        }

        private void InitializeButtons()
        {
            btnClose.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            btnClose.Margin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            btnClose.Name = "btnClose";
            btnClose.Size = new System.Drawing.Size(96, 27);
            btnClose.TabIndex = 1;
            btnClose.Text = "关闭";
            btnClose.UseVisualStyleBackColor = true;
            btnClose.Click += new System.EventHandler(btnClose_Click);

            btnDownload.Margin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            btnDownload.Name = "btnDownload";
            btnDownload.Size = new System.Drawing.Size(96, 27);
            btnDownload.TabIndex = 0;
            btnDownload.Text = "去下载";
            btnDownload.UseVisualStyleBackColor = true;
            btnDownload.Click += new System.EventHandler(btnDownload_Click);

            buttonPanel.AutoSize = true;
            buttonPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            buttonPanel.Controls.Add(btnClose);
            buttonPanel.Controls.Add(btnDownload);
            buttonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            buttonPanel.Location = new System.Drawing.Point(3, 365);
            buttonPanel.Margin = new System.Windows.Forms.Padding(3, 12, 3, 3);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Size = new System.Drawing.Size(610, 27);
            buttonPanel.TabIndex = 4;
            buttonPanel.WrapContents = false;
        }

        private void InitializeInfoPanel()
        {
            lblTitle.AutoSize = true;
            lblTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Bold);
            lblTitle.Location = new System.Drawing.Point(3, 0);
            lblTitle.Margin = new System.Windows.Forms.Padding(3, 0, 3, 12);
            lblTitle.Name = "lblTitle";
            lblTitle.Size = new System.Drawing.Size(104, 18);
            lblTitle.TabIndex = 0;
            lblTitle.Text = "发现新版本";

            lblCurrentVersion.AutoSize = true;
            lblCurrentVersion.Location = new System.Drawing.Point(3, 0);
            lblCurrentVersion.Margin = new System.Windows.Forms.Padding(3, 6, 3, 6);
            lblCurrentVersion.Name = "lblCurrentVersion";
            lblCurrentVersion.Size = new System.Drawing.Size(53, 12);
            lblCurrentVersion.TabIndex = 0;
            lblCurrentVersion.Text = "当前版本";

            lblCurrentVersionValue.AutoEllipsis = true;
            lblCurrentVersionValue.Dock = System.Windows.Forms.DockStyle.Fill;
            lblCurrentVersionValue.Location = new System.Drawing.Point(111, 0);
            lblCurrentVersionValue.Margin = new System.Windows.Forms.Padding(3, 6, 3, 6);
            lblCurrentVersionValue.Name = "lblCurrentVersionValue";
            lblCurrentVersionValue.Size = new System.Drawing.Size(496, 24);
            lblCurrentVersionValue.TabIndex = 1;
            lblCurrentVersionValue.Text = "未提供";
            lblCurrentVersionValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            lblLatestVersion.AutoSize = true;
            lblLatestVersion.Location = new System.Drawing.Point(3, 24);
            lblLatestVersion.Margin = new System.Windows.Forms.Padding(3, 6, 3, 6);
            lblLatestVersion.Name = "lblLatestVersion";
            lblLatestVersion.Size = new System.Drawing.Size(53, 12);
            lblLatestVersion.TabIndex = 2;
            lblLatestVersion.Text = "最新版本";

            lblLatestVersionValue.AutoEllipsis = true;
            lblLatestVersionValue.Dock = System.Windows.Forms.DockStyle.Fill;
            lblLatestVersionValue.Location = new System.Drawing.Point(111, 24);
            lblLatestVersionValue.Margin = new System.Windows.Forms.Padding(3, 6, 3, 6);
            lblLatestVersionValue.Name = "lblLatestVersionValue";
            lblLatestVersionValue.Size = new System.Drawing.Size(496, 24);
            lblLatestVersionValue.TabIndex = 3;
            lblLatestVersionValue.Text = "未提供";
            lblLatestVersionValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            lblReleaseDate.AutoSize = true;
            lblReleaseDate.Location = new System.Drawing.Point(3, 48);
            lblReleaseDate.Margin = new System.Windows.Forms.Padding(3, 6, 3, 6);
            lblReleaseDate.Name = "lblReleaseDate";
            lblReleaseDate.Size = new System.Drawing.Size(53, 12);
            lblReleaseDate.TabIndex = 4;
            lblReleaseDate.Text = "发布日期";

            lblReleaseDateValue.AutoEllipsis = true;
            lblReleaseDateValue.Dock = System.Windows.Forms.DockStyle.Fill;
            lblReleaseDateValue.Location = new System.Drawing.Point(111, 48);
            lblReleaseDateValue.Margin = new System.Windows.Forms.Padding(3, 6, 3, 6);
            lblReleaseDateValue.Name = "lblReleaseDateValue";
            lblReleaseDateValue.Size = new System.Drawing.Size(496, 24);
            lblReleaseDateValue.TabIndex = 5;
            lblReleaseDateValue.Text = "未提供";
            lblReleaseDateValue.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            lblReleaseNotes.AutoSize = true;
            lblReleaseNotes.Location = new System.Drawing.Point(3, 96);
            lblReleaseNotes.Margin = new System.Windows.Forms.Padding(3, 0, 3, 6);
            lblReleaseNotes.Name = "lblReleaseNotes";
            lblReleaseNotes.Size = new System.Drawing.Size(53, 12);
            lblReleaseNotes.TabIndex = 2;
            lblReleaseNotes.Text = "更新说明";

            infoPanel.ColumnCount = 2;
            infoPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 108F));
            infoPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            infoPanel.Controls.Add(lblCurrentVersion, 0, 0);
            infoPanel.Controls.Add(lblCurrentVersionValue, 1, 0);
            infoPanel.Controls.Add(lblLatestVersion, 0, 1);
            infoPanel.Controls.Add(lblLatestVersionValue, 1, 1);
            infoPanel.Controls.Add(lblReleaseDate, 0, 2);
            infoPanel.Controls.Add(lblReleaseDateValue, 1, 2);
            infoPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            infoPanel.Location = new System.Drawing.Point(3, 30);
            infoPanel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 12);
            infoPanel.Name = "infoPanel";
            infoPanel.RowCount = 3;
            infoPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            infoPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            infoPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            infoPanel.Size = new System.Drawing.Size(610, 72);
            infoPanel.TabIndex = 1;

            txtReleaseNotes.BackColor = System.Drawing.SystemColors.Window;
            txtReleaseNotes.Dock = System.Windows.Forms.DockStyle.Fill;
            txtReleaseNotes.Location = new System.Drawing.Point(3, 117);
            txtReleaseNotes.Multiline = true;
            txtReleaseNotes.Name = "txtReleaseNotes";
            txtReleaseNotes.ReadOnly = true;
            txtReleaseNotes.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            txtReleaseNotes.Size = new System.Drawing.Size(610, 233);
            txtReleaseNotes.TabIndex = 3;
            txtReleaseNotes.TabStop = false;
        }

        private void InitializeRootPanel()
        {
            rootPanel.ColumnCount = 1;
            rootPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            rootPanel.Controls.Add(lblTitle, 0, 0);
            rootPanel.Controls.Add(infoPanel, 0, 1);
            rootPanel.Controls.Add(lblReleaseNotes, 0, 2);
            rootPanel.Controls.Add(txtReleaseNotes, 0, 3);
            rootPanel.Controls.Add(buttonPanel, 0, 4);
            rootPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            rootPanel.Location = new System.Drawing.Point(12, 12);
            rootPanel.Name = "rootPanel";
            rootPanel.RowCount = 5;
            rootPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            rootPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            rootPanel.Size = new System.Drawing.Size(616, 395);
            rootPanel.TabIndex = 0;
        }

        private void InitializeForm()
        {
            AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = btnClose;
            ClientSize = new System.Drawing.Size(640, 419);
            Controls.Add(rootPanel);
            MinimumSize = new System.Drawing.Size(560, 420);
            Name = "FormUpdate";
            Padding = new System.Windows.Forms.Padding(12);
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "发现新版本";
        }

        #endregion
    }
}
