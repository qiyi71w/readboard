namespace readboard
{
    partial class SettingsForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SettingsForm));
            this.lblBlackOffsets = new System.Windows.Forms.Label();
            this.lblWhiteOffsets = new System.Windows.Forms.Label();
            this.lblBlackPercents = new System.Windows.Forms.Label();
            this.lblWhitePercents = new System.Windows.Forms.Label();
            this.txtBlackOffsets = new System.Windows.Forms.TextBox();
            this.txtWhiteOffsets = new System.Windows.Forms.TextBox();
            this.txtWhitePercents = new System.Windows.Forms.TextBox();
            this.txtBlackPercents = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.lblTips2 = new System.Windows.Forms.Label();
            this.btnConfirm = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.chkMag = new System.Windows.Forms.CheckBox();
            this.lblTips1 = new System.Windows.Forms.Label();
            this.lblTips = new System.Windows.Forms.Label();
            this.chkVerifyMove = new System.Windows.Forms.CheckBox();
            this.chkAutoMin = new System.Windows.Forms.CheckBox();
            this.lblBackForeOnly = new System.Windows.Forms.Label();
            this.btnReset = new System.Windows.Forms.Button();
            this.lblSyncInterval = new System.Windows.Forms.Label();
            this.txtSyncInterval = new System.Windows.Forms.TextBox();
            this.lblGrayOffsets = new System.Windows.Forms.Label();
            this.txtGrayOffsets = new System.Windows.Forms.TextBox();
            this.chkEnhanceScreen = new System.Windows.Forms.CheckBox();
            this.chkPonder = new System.Windows.Forms.CheckBox();
            this.chkDisableShowInBoardShortcut = new System.Windows.Forms.CheckBox();
            this.lblColorMode = new System.Windows.Forms.Label();
            this.rdoColorSystem = new System.Windows.Forms.RadioButton();
            this.rdoColorDark = new System.Windows.Forms.RadioButton();
            this.rdoColorLight = new System.Windows.Forms.RadioButton();
            this.chkDebugDiagnostics = new System.Windows.Forms.CheckBox();
            this.btnOpenDebugDiagnostics = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // lblBlackOffsets
            // 
            this.lblBlackOffsets.AutoSize = true;
            this.lblBlackOffsets.Location = new System.Drawing.Point(10, 85);
            this.lblBlackOffsets.Name = "lblBlackOffsets";
            this.lblBlackOffsets.Size = new System.Drawing.Size(95, 12);
            this.lblBlackOffsets.TabIndex = 0;
            this.lblBlackOffsets.Text = "黑色偏差(0-255)";
            // 
            // lblWhiteOffsets
            // 
            this.lblWhiteOffsets.AutoSize = true;
            this.lblWhiteOffsets.Location = new System.Drawing.Point(10, 111);
            this.lblWhiteOffsets.Name = "lblWhiteOffsets";
            this.lblWhiteOffsets.Size = new System.Drawing.Size(95, 12);
            this.lblWhiteOffsets.TabIndex = 1;
            this.lblWhiteOffsets.Text = "白色偏差(0-255)";
            // 
            // lblBlackPercents
            // 
            this.lblBlackPercents.AutoSize = true;
            this.lblBlackPercents.Location = new System.Drawing.Point(217, 85);
            this.lblBlackPercents.Name = "lblBlackPercents";
            this.lblBlackPercents.Size = new System.Drawing.Size(95, 12);
            this.lblBlackPercents.TabIndex = 2;
            this.lblBlackPercents.Text = "黑色占比(0-100)";
            // 
            // lblWhitePercents
            // 
            this.lblWhitePercents.AutoSize = true;
            this.lblWhitePercents.Location = new System.Drawing.Point(217, 111);
            this.lblWhitePercents.Name = "lblWhitePercents";
            this.lblWhitePercents.Size = new System.Drawing.Size(95, 12);
            this.lblWhitePercents.TabIndex = 3;
            this.lblWhitePercents.Text = "白色占比(0-100)";
            // 
            // txtBlackOffsets
            // 
            this.txtBlackOffsets.Location = new System.Drawing.Point(119, 80);
            this.txtBlackOffsets.Name = "txtBlackOffsets";
            this.txtBlackOffsets.Size = new System.Drawing.Size(79, 21);
            this.txtBlackOffsets.TabIndex = 4;
            // 
            // txtWhiteOffsets
            // 
            this.txtWhiteOffsets.Location = new System.Drawing.Point(119, 105);
            this.txtWhiteOffsets.Name = "txtWhiteOffsets";
            this.txtWhiteOffsets.Size = new System.Drawing.Size(79, 21);
            this.txtWhiteOffsets.TabIndex = 5;
            // 
            // txtWhitePercents
            // 
            this.txtWhitePercents.Location = new System.Drawing.Point(327, 105);
            this.txtWhitePercents.Name = "txtWhitePercents";
            this.txtWhitePercents.Size = new System.Drawing.Size(79, 21);
            this.txtWhitePercents.TabIndex = 6;
            // 
            // txtBlackPercents
            // 
            this.txtBlackPercents.Location = new System.Drawing.Point(327, 80);
            this.txtBlackPercents.Name = "txtBlackPercents";
            this.txtBlackPercents.Size = new System.Drawing.Size(79, 21);
            this.txtBlackPercents.TabIndex = 7;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(17, 65);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(0, 12);
            this.label5.TabIndex = 8;
            // 
            // lblTips2
            // 
            this.lblTips2.AutoSize = true;
            this.lblTips2.Location = new System.Drawing.Point(10, 181);
            this.lblTips2.Name = "lblTips2";
            this.lblTips2.Size = new System.Drawing.Size(287, 12);
            this.lblTips2.TabIndex = 9;
            this.lblTips2.Text = "如某种颜色棋子识别丢失,可尝试增大偏差或降低占比";
            // 
            // btnConfirm
            // 
            this.btnConfirm.Location = new System.Drawing.Point(257, 203);
            this.btnConfirm.Name = "btnConfirm";
            this.btnConfirm.Size = new System.Drawing.Size(75, 23);
            this.btnConfirm.TabIndex = 10;
            this.btnConfirm.Text = "确定";
            this.btnConfirm.UseVisualStyleBackColor = true;
            this.btnConfirm.Click += new System.EventHandler(this.button1_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Cursor = System.Windows.Forms.Cursors.Default;
            this.btnCancel.Location = new System.Drawing.Point(338, 203);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 11;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.button2_Click);
            // 
            // chkMag
            // 
            this.chkMag.AutoSize = true;
            this.chkMag.Location = new System.Drawing.Point(12, 33);
            this.chkMag.Name = "chkMag";
            this.chkMag.Size = new System.Drawing.Size(84, 16);
            this.chkMag.TabIndex = 12;
            this.chkMag.Text = "使用放大镜";
            this.chkMag.UseVisualStyleBackColor = true;
            // 
            // lblTips1
            // 
            this.lblTips1.AutoSize = true;
            this.lblTips1.Location = new System.Drawing.Point(10, 161);
            this.lblTips1.Name = "lblTips1";
            this.lblTips1.Size = new System.Drawing.Size(287, 12);
            this.lblTips1.TabIndex = 13;
            this.lblTips1.Text = "如某种颜色棋子识别过多,可尝试降低偏差或增大占比";
            // 
            // lblTips
            // 
            this.lblTips.AutoSize = true;
            this.lblTips.Location = new System.Drawing.Point(10, 136);
            this.lblTips.Name = "lblTips";
            this.lblTips.Size = new System.Drawing.Size(143, 12);
            this.lblTips.TabIndex = 14;
            this.lblTips.Text = "注:所有参数都必须为整数";
            // 
            // chkVerifyMove
            // 
            this.chkVerifyMove.AutoSize = true;
            this.chkVerifyMove.Location = new System.Drawing.Point(218, 12);
            this.chkVerifyMove.Name = "chkVerifyMove";
            this.chkVerifyMove.Size = new System.Drawing.Size(132, 16);
            this.chkVerifyMove.TabIndex = 15;
            this.chkVerifyMove.Text = "验证落子以确保成功";
            this.chkVerifyMove.UseVisualStyleBackColor = true;
            // 
            // chkAutoMin
            // 
            this.chkAutoMin.AutoSize = true;
            this.chkAutoMin.Location = new System.Drawing.Point(12, 12);
            this.chkAutoMin.Name = "chkAutoMin";
            this.chkAutoMin.Size = new System.Drawing.Size(120, 16);
            this.chkAutoMin.TabIndex = 16;
            this.chkAutoMin.Text = "同步后自动最小化";
            this.chkAutoMin.UseVisualStyleBackColor = true;
            // 
            // lblBackForeOnly
            // 
            this.lblBackForeOnly.AutoSize = true;
            this.lblBackForeOnly.Location = new System.Drawing.Point(10, 59);
            this.lblBackForeOnly.Name = "lblBackForeOnly";
            this.lblBackForeOnly.Size = new System.Drawing.Size(305, 12);
            this.lblBackForeOnly.TabIndex = 17;
            this.lblBackForeOnly.Text = "以下选项只对 其他(前台),其他(后台) 类型的同步生效:";
            // 
            // btnReset
            // 
            this.btnReset.Location = new System.Drawing.Point(12, 202);
            this.btnReset.Margin = new System.Windows.Forms.Padding(2);
            this.btnReset.Name = "btnReset";
            this.btnReset.Size = new System.Drawing.Size(105, 23);
            this.btnReset.TabIndex = 22;
            this.btnReset.Text = "恢复默认设置";
            this.btnReset.UseVisualStyleBackColor = true;
            this.btnReset.Click += new System.EventHandler(this.button4_Click);
            // 
            // lblSyncInterval
            // 
            this.lblSyncInterval.AutoSize = true;
            this.lblSyncInterval.Location = new System.Drawing.Point(216, 34);
            this.lblSyncInterval.Name = "lblSyncInterval";
            this.lblSyncInterval.Size = new System.Drawing.Size(107, 12);
            this.lblSyncInterval.TabIndex = 23;
            this.lblSyncInterval.Text = "同步时间间隔(ms):";
            // 
            // txtSyncInterval
            // 
            this.txtSyncInterval.Location = new System.Drawing.Point(327, 29);
            this.txtSyncInterval.Name = "txtSyncInterval";
            this.txtSyncInterval.Size = new System.Drawing.Size(78, 21);
            this.txtSyncInterval.TabIndex = 24;
            // 
            // lblGrayOffsets
            // 
            this.lblGrayOffsets.AutoSize = true;
            this.lblGrayOffsets.Location = new System.Drawing.Point(217, 136);
            this.lblGrayOffsets.Name = "lblGrayOffsets";
            this.lblGrayOffsets.Size = new System.Drawing.Size(95, 12);
            this.lblGrayOffsets.TabIndex = 25;
            this.lblGrayOffsets.Text = "灰度偏色(0-255)";
            // 
            // txtGrayOffsets
            // 
            this.txtGrayOffsets.Location = new System.Drawing.Point(327, 131);
            this.txtGrayOffsets.Name = "txtGrayOffsets";
            this.txtGrayOffsets.Size = new System.Drawing.Size(79, 21);
            this.txtGrayOffsets.TabIndex = 26;
            // 
            // chkEnhanceScreen
            // 
            this.chkEnhanceScreen.AutoSize = true;
            this.chkEnhanceScreen.Location = new System.Drawing.Point(130, 33);
            this.chkEnhanceScreen.Name = "chkEnhanceScreen";
            this.chkEnhanceScreen.Size = new System.Drawing.Size(72, 16);
            this.chkEnhanceScreen.TabIndex = 27;
            this.chkEnhanceScreen.Text = "强化截图";
            this.chkEnhanceScreen.UseVisualStyleBackColor = true;
            // 
            // chkPonder
            // 
            this.chkPonder.AutoSize = true;
            this.chkPonder.Location = new System.Drawing.Point(130, 12);
            this.chkPonder.Name = "chkPonder";
            this.chkPonder.Size = new System.Drawing.Size(72, 16);
            this.chkPonder.TabIndex = 28;
            this.chkPonder.Text = "后台思考";
            this.chkPonder.UseVisualStyleBackColor = true;
            // 
            // chkDisableShowInBoardShortcut
            // 
            this.chkDisableShowInBoardShortcut.AutoSize = true;
            this.chkDisableShowInBoardShortcut.Location = new System.Drawing.Point(356, 12);
            this.chkDisableShowInBoardShortcut.Name = "chkDisableShowInBoardShortcut";
            this.chkDisableShowInBoardShortcut.Size = new System.Drawing.Size(120, 16);
            this.chkDisableShowInBoardShortcut.TabIndex = 29;
            this.chkDisableShowInBoardShortcut.Text = "关闭显示选点快捷键";
            this.chkDisableShowInBoardShortcut.UseVisualStyleBackColor = true;
            //
            // lblColorMode
            //
            this.lblColorMode.AutoSize = true;
            this.lblColorMode.Location = new System.Drawing.Point(10, 55);
            this.lblColorMode.Name = "lblColorMode";
            this.lblColorMode.Size = new System.Drawing.Size(59, 12);
            this.lblColorMode.TabIndex = 30;
            this.lblColorMode.Text = "颜色模式:";
            //
            // rdoColorSystem
            //
            this.rdoColorSystem.AutoSize = true;
            this.rdoColorSystem.Location = new System.Drawing.Point(80, 53);
            this.rdoColorSystem.Name = "rdoColorSystem";
            this.rdoColorSystem.Size = new System.Drawing.Size(71, 16);
            this.rdoColorSystem.TabIndex = 31;
            this.rdoColorSystem.Text = "跟随系统";
            this.rdoColorSystem.UseVisualStyleBackColor = true;
            //
            // rdoColorDark
            //
            this.rdoColorDark.AutoSize = true;
            this.rdoColorDark.Location = new System.Drawing.Point(160, 53);
            this.rdoColorDark.Name = "rdoColorDark";
            this.rdoColorDark.Size = new System.Drawing.Size(47, 16);
            this.rdoColorDark.TabIndex = 32;
            this.rdoColorDark.Text = "深色";
            this.rdoColorDark.UseVisualStyleBackColor = true;
            //
            // rdoColorLight
            //
            this.rdoColorLight.AutoSize = true;
            this.rdoColorLight.Location = new System.Drawing.Point(220, 53);
            this.rdoColorLight.Name = "rdoColorLight";
            this.rdoColorLight.Size = new System.Drawing.Size(47, 16);
            this.rdoColorLight.TabIndex = 33;
            this.rdoColorLight.Text = "浅色";
            this.rdoColorLight.UseVisualStyleBackColor = true;
            //
            // chkDebugDiagnostics
            //
            this.chkDebugDiagnostics.AutoSize = true;
            this.chkDebugDiagnostics.Location = new System.Drawing.Point(12, 75);
            this.chkDebugDiagnostics.Name = "chkDebugDiagnostics";
            this.chkDebugDiagnostics.Size = new System.Drawing.Size(120, 16);
            this.chkDebugDiagnostics.TabIndex = 34;
            this.chkDebugDiagnostics.Text = "保存调试诊断";
            this.chkDebugDiagnostics.UseVisualStyleBackColor = true;
            //
            // btnOpenDebugDiagnostics
            //
            this.btnOpenDebugDiagnostics.Location = new System.Drawing.Point(304, 12);
            this.btnOpenDebugDiagnostics.Name = "btnOpenDebugDiagnostics";
            this.btnOpenDebugDiagnostics.Size = new System.Drawing.Size(108, 23);
            this.btnOpenDebugDiagnostics.TabIndex = 35;
            this.btnOpenDebugDiagnostics.Text = "打开调试目录";
            this.btnOpenDebugDiagnostics.UseVisualStyleBackColor = true;
            this.btnOpenDebugDiagnostics.Click += new System.EventHandler(this.btnOpenDebugDiagnostics_Click);
            //
            // SettingsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(424, 231);
            this.Controls.Add(this.btnOpenDebugDiagnostics);
            this.Controls.Add(this.chkDebugDiagnostics);
            this.Controls.Add(this.rdoColorLight);
            this.Controls.Add(this.rdoColorDark);
            this.Controls.Add(this.rdoColorSystem);
            this.Controls.Add(this.lblColorMode);
            this.Controls.Add(this.chkDisableShowInBoardShortcut);
            this.Controls.Add(this.chkPonder);
            this.Controls.Add(this.chkEnhanceScreen);
            this.Controls.Add(this.txtGrayOffsets);
            this.Controls.Add(this.lblGrayOffsets);
            this.Controls.Add(this.txtSyncInterval);
            this.Controls.Add(this.lblSyncInterval);
            this.Controls.Add(this.btnReset);
            this.Controls.Add(this.lblBackForeOnly);
            this.Controls.Add(this.chkAutoMin);
            this.Controls.Add(this.chkVerifyMove);
            this.Controls.Add(this.lblTips);
            this.Controls.Add(this.lblTips1);
            this.Controls.Add(this.chkMag);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnConfirm);
            this.Controls.Add(this.lblTips2);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.txtBlackPercents);
            this.Controls.Add(this.txtWhitePercents);
            this.Controls.Add(this.txtWhiteOffsets);
            this.Controls.Add(this.txtBlackOffsets);
            this.Controls.Add(this.lblWhitePercents);
            this.Controls.Add(this.lblBlackPercents);
            this.Controls.Add(this.lblWhiteOffsets);
            this.Controls.Add(this.lblBlackOffsets);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "SettingsForm";
            this.Text = "参数设置";
            this.TopMost = true;
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblBlackOffsets;
        private System.Windows.Forms.Label lblWhiteOffsets;
        private System.Windows.Forms.Label lblBlackPercents;
        private System.Windows.Forms.Label lblWhitePercents;
        private System.Windows.Forms.TextBox txtBlackOffsets;
        private System.Windows.Forms.TextBox txtWhiteOffsets;
        private System.Windows.Forms.TextBox txtWhitePercents;
        private System.Windows.Forms.TextBox txtBlackPercents;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label lblTips2;
        private System.Windows.Forms.Button btnConfirm;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.CheckBox chkMag;
        private System.Windows.Forms.Label lblTips1;
        private System.Windows.Forms.Label lblTips;
        private System.Windows.Forms.CheckBox chkVerifyMove;
        private System.Windows.Forms.CheckBox chkAutoMin;
        private System.Windows.Forms.Label lblBackForeOnly;
        private System.Windows.Forms.Button btnReset;
        private System.Windows.Forms.Label lblSyncInterval;
        private System.Windows.Forms.TextBox txtSyncInterval;
        private System.Windows.Forms.Label lblGrayOffsets;
        private System.Windows.Forms.TextBox txtGrayOffsets;
        private System.Windows.Forms.CheckBox chkEnhanceScreen;
        private System.Windows.Forms.CheckBox chkPonder;
        private System.Windows.Forms.CheckBox chkDisableShowInBoardShortcut;
        private System.Windows.Forms.Label lblColorMode;
        private System.Windows.Forms.RadioButton rdoColorSystem;
        private System.Windows.Forms.RadioButton rdoColorDark;
        private System.Windows.Forms.RadioButton rdoColorLight;
        private System.Windows.Forms.CheckBox chkDebugDiagnostics;
        private System.Windows.Forms.Button btnOpenDebugDiagnostics;
    }
}
