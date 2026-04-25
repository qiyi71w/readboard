namespace readboard
{
    partial class MainForm
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.btnCircleBoard = new System.Windows.Forms.Button();
            this.btnClickBoard = new System.Windows.Forms.Button();
            this.btnOneTimeSync = new System.Windows.Forms.Button();
            this.btnKeepSync = new System.Windows.Forms.Button();
            this.btnForceRebuild = new System.Windows.Forms.Button();
            this.btnClearBoard = new System.Windows.Forms.Button();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.rdoFoxBack = new System.Windows.Forms.RadioButton();
            this.rdoYike = new System.Windows.Forms.RadioButton();
            this.rdoFore = new System.Windows.Forms.RadioButton();
            this.rdoSina = new System.Windows.Forms.RadioButton();
            this.rdoBack = new System.Windows.Forms.RadioButton();
            this.rdoTygem = new System.Windows.Forms.RadioButton();
            this.rdoFox = new System.Windows.Forms.RadioButton();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.label6 = new System.Windows.Forms.Label();
            this.txtBoardHeight = new System.Windows.Forms.TextBox();
            this.txtBoardWidth = new System.Windows.Forms.TextBox();
            this.rdoOtherBoard = new System.Windows.Forms.RadioButton();
            this.lblBoardSize = new System.Windows.Forms.Label();
            this.rdo9x9 = new System.Windows.Forms.RadioButton();
            this.rdo13x13 = new System.Windows.Forms.RadioButton();
            this.rdo19x19 = new System.Windows.Forms.RadioButton();
            this.radioWhite = new System.Windows.Forms.RadioButton();
            this.radioBlack = new System.Windows.Forms.RadioButton();
            this.chkBothSync = new System.Windows.Forms.CheckBox();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.lblTime = new System.Windows.Forms.Label();
            this.btnTogglePonder = new System.Windows.Forms.Button();
            this.lblTotalVisits = new System.Windows.Forms.Label();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.lblBestMoveVisits = new System.Windows.Forms.Label();
            this.textBox3 = new System.Windows.Forms.TextBox();
            this.btnHelp = new System.Windows.Forms.Button();
            this.btnCheckUpdate = new System.Windows.Forms.Button();
            this.lblPlayCondition = new System.Windows.Forms.Label();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.flowLayoutPanel2 = new System.Windows.Forms.FlowLayoutPanel();
            this.chkAutoPlay = new System.Windows.Forms.CheckBox();
            this.panel3 = new System.Windows.Forms.Panel();
            this.panel4 = new System.Windows.Forms.Panel();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.panel1 = new System.Windows.Forms.Panel();
            this.panel2 = new System.Windows.Forms.Panel();
            this.btnSettings = new System.Windows.Forms.Button();
            this.btnExchange = new System.Windows.Forms.Button();
            this.chkShowInBoard = new System.Windows.Forms.CheckBox();
            this.btnKomi65 = new System.Windows.Forms.Button();
            this.btnFastSync = new System.Windows.Forms.Button();
            this.btnCircleRow1 = new System.Windows.Forms.Button();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.flowLayoutPanel2.SuspendLayout();
            this.panel3.SuspendLayout();
            this.panel4.SuspendLayout();
            this.flowLayoutPanel1.SuspendLayout();
            this.panel1.SuspendLayout();
            this.panel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnCircleBoard
            // 
            this.btnCircleBoard.Location = new System.Drawing.Point(155, 126);
            this.btnCircleBoard.Name = "btnCircleBoard";
            this.btnCircleBoard.Size = new System.Drawing.Size(83, 23);
            this.btnCircleBoard.TabIndex = 1;
            this.btnCircleBoard.Text = "框选棋盘";
            this.btnCircleBoard.UseVisualStyleBackColor = true;
            this.btnCircleBoard.Click += new System.EventHandler(this.Button2_Click);
            // 
            // btnClickBoard
            // 
            this.btnClickBoard.Font = new System.Drawing.Font("宋体", 9F);
            this.btnClickBoard.Location = new System.Drawing.Point(7, 126);
            this.btnClickBoard.Name = "btnClickBoard";
            this.btnClickBoard.Size = new System.Drawing.Size(145, 23);
            this.btnClickBoard.TabIndex = 2;
            this.btnClickBoard.Text = "选择棋盘(点击棋盘内部)";
            this.btnClickBoard.UseVisualStyleBackColor = true;
            this.btnClickBoard.Click += new System.EventHandler(this.button3_Click);
            // 
            // btnOneTimeSync
            // 
            this.btnOneTimeSync.Location = new System.Drawing.Point(117, 153);
            this.btnOneTimeSync.Name = "btnOneTimeSync";
            this.btnOneTimeSync.Size = new System.Drawing.Size(82, 23);
            this.btnOneTimeSync.TabIndex = 3;
            this.btnOneTimeSync.Text = "单次同步";
            this.btnOneTimeSync.UseVisualStyleBackColor = true;
            this.btnOneTimeSync.Click += new System.EventHandler(this.button4_Click);
            // 
            // btnKeepSync
            // 
            this.btnKeepSync.Location = new System.Drawing.Point(7, 153);
            this.btnKeepSync.Name = "btnKeepSync";
            this.btnKeepSync.Size = new System.Drawing.Size(107, 23);
            this.btnKeepSync.TabIndex = 4;
            this.btnKeepSync.Text = "持续同步(200ms)";
            this.btnKeepSync.UseVisualStyleBackColor = true;
            this.btnKeepSync.Click += new System.EventHandler(this.button5_Click);
            // 
            // btnForceRebuild
            // 
            this.btnForceRebuild.Location = new System.Drawing.Point(284, 153);
            this.btnForceRebuild.Name = "btnForceRebuild";
            this.btnForceRebuild.Size = new System.Drawing.Size(82, 23);
            this.btnForceRebuild.TabIndex = 40;
            this.btnForceRebuild.Text = "强制重建";
            this.btnForceRebuild.UseVisualStyleBackColor = true;
            this.btnForceRebuild.Click += new System.EventHandler(this.btnForceRebuild_Click);
            // 
            // btnClearBoard
            // 
            this.btnClearBoard.Location = new System.Drawing.Point(372, 153);
            this.btnClearBoard.Name = "btnClearBoard";
            this.btnClearBoard.Size = new System.Drawing.Size(80, 23);
            this.btnClearBoard.TabIndex = 5;
            this.btnClearBoard.Text = "清空棋盘";
            this.btnClearBoard.UseVisualStyleBackColor = true;
            this.btnClearBoard.Click += new System.EventHandler(this.button6_Click);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.rdoFoxBack);
            this.groupBox1.Controls.Add(this.rdoYike);
            this.groupBox1.Controls.Add(this.rdoFore);
            this.groupBox1.Controls.Add(this.rdoSina);
            this.groupBox1.Controls.Add(this.rdoBack);
            this.groupBox1.Controls.Add(this.rdoTygem);
            this.groupBox1.Controls.Add(this.rdoFox);
            this.groupBox1.Location = new System.Drawing.Point(7, -2);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(560, 32);
            this.groupBox1.TabIndex = 14;
            this.groupBox1.TabStop = false;
            // 
            // rdoFoxBack
            // 
            this.rdoFoxBack.AutoSize = true;
            this.rdoFoxBack.Location = new System.Drawing.Point(56, 11);
            this.rdoFoxBack.Name = "rdoFoxBack";
            this.rdoFoxBack.Size = new System.Drawing.Size(107, 16);
            this.rdoFoxBack.TabIndex = 15;
            this.rdoFoxBack.TabStop = true;
            this.rdoFoxBack.Text = "野狐(后台落子)";
            this.rdoFoxBack.UseVisualStyleBackColor = true;
            this.rdoFoxBack.CheckedChanged += new System.EventHandler(this.radioButtonFoxBack_CheckedChanged);
            // 
            // rdoYike
            // 
            this.rdoYike.AutoSize = true;
            this.rdoYike.Location = new System.Drawing.Point(174, 11);
            this.rdoYike.Name = "rdoYike";
            this.rdoYike.Size = new System.Drawing.Size(47, 16);
            this.rdoYike.TabIndex = 16;
            this.rdoYike.TabStop = true;
            this.rdoYike.Text = "弈客";
            this.rdoYike.UseVisualStyleBackColor = true;
            this.rdoYike.CheckedChanged += new System.EventHandler(this.radioButtonYike_CheckedChanged);
            // 
            // rdoFore
            // 
            this.rdoFore.AutoSize = true;
            this.rdoFore.Location = new System.Drawing.Point(459, 11);
            this.rdoFore.Name = "rdoFore";
            this.rdoFore.Size = new System.Drawing.Size(83, 16);
            this.rdoFore.TabIndex = 14;
            this.rdoFore.TabStop = true;
            this.rdoFore.Text = "其他(前台)";
            this.rdoFore.UseVisualStyleBackColor = true;
            this.rdoFore.CheckedChanged += new System.EventHandler(this.rdoqiantai_CheckedChanged);
            // 
            // rdoSina
            // 
            this.rdoSina.AutoSize = true;
            this.rdoSina.Location = new System.Drawing.Point(285, 11);
            this.rdoSina.Name = "rdoSina";
            this.rdoSina.Size = new System.Drawing.Size(47, 16);
            this.rdoSina.TabIndex = 13;
            this.rdoSina.TabStop = true;
            this.rdoSina.Text = "新浪";
            this.rdoSina.UseVisualStyleBackColor = true;
            this.rdoSina.CheckedChanged += new System.EventHandler(this.radioButton4_CheckedChanged);
            // 
            // rdoBack
            // 
            this.rdoBack.AutoSize = true;
            this.rdoBack.Location = new System.Drawing.Point(344, 11);
            this.rdoBack.Name = "rdoBack";
            this.rdoBack.Size = new System.Drawing.Size(83, 16);
            this.rdoBack.TabIndex = 12;
            this.rdoBack.TabStop = true;
            this.rdoBack.Text = "其他(后台)";
            this.rdoBack.UseVisualStyleBackColor = true;
            this.rdoBack.CheckedChanged += new System.EventHandler(this.radioButton3_CheckedChanged);
            // 
            // rdoTygem
            // 
            this.rdoTygem.AutoSize = true;
            this.rdoTygem.Location = new System.Drawing.Point(231, 11);
            this.rdoTygem.Name = "rdoTygem";
            this.rdoTygem.Size = new System.Drawing.Size(47, 16);
            this.rdoTygem.TabIndex = 11;
            this.rdoTygem.TabStop = true;
            this.rdoTygem.Text = "弈城";
            this.rdoTygem.UseVisualStyleBackColor = true;
            this.rdoTygem.CheckedChanged += new System.EventHandler(this.radioButton2_CheckedChanged);
            // 
            // rdoFox
            // 
            this.rdoFox.AutoSize = true;
            this.rdoFox.Location = new System.Drawing.Point(5, 11);
            this.rdoFox.Name = "rdoFox";
            this.rdoFox.Size = new System.Drawing.Size(47, 16);
            this.rdoFox.TabIndex = 10;
            this.rdoFox.TabStop = true;
            this.rdoFox.Text = "野狐";
            this.rdoFox.UseVisualStyleBackColor = true;
            this.rdoFox.CheckedChanged += new System.EventHandler(this.radioButton1_CheckedChanged);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.label6);
            this.groupBox2.Controls.Add(this.txtBoardHeight);
            this.groupBox2.Controls.Add(this.txtBoardWidth);
            this.groupBox2.Controls.Add(this.rdoOtherBoard);
            this.groupBox2.Controls.Add(this.lblBoardSize);
            this.groupBox2.Controls.Add(this.rdo9x9);
            this.groupBox2.Controls.Add(this.rdo13x13);
            this.groupBox2.Controls.Add(this.rdo19x19);
            this.groupBox2.Location = new System.Drawing.Point(87, 29);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(246, 31);
            this.groupBox2.TabIndex = 15;
            this.groupBox2.TabStop = false;
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(212, 12);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(11, 12);
            this.label6.TabIndex = 36;
            this.label6.Text = "*";
            // 
            // txtBoardHeight
            // 
            this.txtBoardHeight.BackColor = System.Drawing.SystemColors.Menu;
            this.txtBoardHeight.Location = new System.Drawing.Point(224, 8);
            this.txtBoardHeight.Name = "txtBoardHeight";
            this.txtBoardHeight.Size = new System.Drawing.Size(21, 21);
            this.txtBoardHeight.TabIndex = 35;
            this.txtBoardHeight.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.tb_KeyPressHeight);
            this.txtBoardHeight.Leave += new System.EventHandler(this.parseHeight);
            // 
            // txtBoardWidth
            // 
            this.txtBoardWidth.BackColor = System.Drawing.SystemColors.Menu;
            this.txtBoardWidth.Location = new System.Drawing.Point(191, 8);
            this.txtBoardWidth.Name = "txtBoardWidth";
            this.txtBoardWidth.Size = new System.Drawing.Size(21, 21);
            this.txtBoardWidth.TabIndex = 34;
            this.txtBoardWidth.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.tb_KeyPressWidth);
            this.txtBoardWidth.Leave += new System.EventHandler(this.parseWidth);
            // 
            // rdoOtherBoard
            // 
            this.rdoOtherBoard.AutoSize = true;
            this.rdoOtherBoard.Location = new System.Drawing.Point(176, 12);
            this.rdoOtherBoard.Name = "rdoOtherBoard";
            this.rdoOtherBoard.Size = new System.Drawing.Size(14, 13);
            this.rdoOtherBoard.TabIndex = 33;
            this.rdoOtherBoard.TabStop = true;
            this.rdoOtherBoard.UseVisualStyleBackColor = true;
            this.rdoOtherBoard.CheckedChanged += new System.EventHandler(this.radioButton8_CheckedChanged);
            // 
            // lblBoardSize
            // 
            this.lblBoardSize.AutoSize = true;
            this.lblBoardSize.Location = new System.Drawing.Point(3, 12);
            this.lblBoardSize.Name = "lblBoardSize";
            this.lblBoardSize.Size = new System.Drawing.Size(35, 12);
            this.lblBoardSize.TabIndex = 17;
            this.lblBoardSize.Text = "棋盘:";
            // 
            // rdo9x9
            // 
            this.rdo9x9.AutoSize = true;
            this.rdo9x9.Location = new System.Drawing.Point(138, 11);
            this.rdo9x9.Name = "rdo9x9";
            this.rdo9x9.Size = new System.Drawing.Size(41, 16);
            this.rdo9x9.TabIndex = 16;
            this.rdo9x9.TabStop = true;
            this.rdo9x9.Text = "9*9";
            this.rdo9x9.UseVisualStyleBackColor = true;
            this.rdo9x9.CheckedChanged += new System.EventHandler(this.radioButton7_CheckedChanged);
            // 
            // rdo13x13
            // 
            this.rdo13x13.AutoSize = true;
            this.rdo13x13.Location = new System.Drawing.Point(88, 11);
            this.rdo13x13.Name = "rdo13x13";
            this.rdo13x13.Size = new System.Drawing.Size(53, 16);
            this.rdo13x13.TabIndex = 15;
            this.rdo13x13.TabStop = true;
            this.rdo13x13.Text = "13*13";
            this.rdo13x13.UseVisualStyleBackColor = true;
            this.rdo13x13.CheckedChanged += new System.EventHandler(this.radioButton6_CheckedChanged);
            // 
            // rdo19x19
            // 
            this.rdo19x19.AutoSize = true;
            this.rdo19x19.Location = new System.Drawing.Point(38, 11);
            this.rdo19x19.Name = "rdo19x19";
            this.rdo19x19.Size = new System.Drawing.Size(53, 16);
            this.rdo19x19.TabIndex = 14;
            this.rdo19x19.TabStop = true;
            this.rdo19x19.Text = "19*19";
            this.rdo19x19.UseVisualStyleBackColor = true;
            this.rdo19x19.CheckedChanged += new System.EventHandler(this.radioButton5_CheckedChanged);
            // 
            // radioWhite
            // 
            this.radioWhite.AutoSize = true;
            this.radioWhite.Location = new System.Drawing.Point(81, 3);
            this.radioWhite.Name = "radioWhite";
            this.radioWhite.Size = new System.Drawing.Size(47, 16);
            this.radioWhite.TabIndex = 23;
            this.radioWhite.TabStop = true;
            this.radioWhite.Text = "执白";
            this.radioWhite.UseVisualStyleBackColor = true;
            this.radioWhite.CheckedChanged += new System.EventHandler(this.radioWhite_CheckedChanged);
            // 
            // radioBlack
            // 
            this.radioBlack.AutoSize = true;
            this.radioBlack.Location = new System.Drawing.Point(81, 3);
            this.radioBlack.Name = "radioBlack";
            this.radioBlack.Size = new System.Drawing.Size(47, 16);
            this.radioBlack.TabIndex = 22;
            this.radioBlack.TabStop = true;
            this.radioBlack.Text = "执黑";
            this.radioBlack.UseVisualStyleBackColor = true;
            this.radioBlack.CheckedChanged += new System.EventHandler(this.radioBlack_CheckedChanged);
            // 
            // chkBothSync
            // 
            this.chkBothSync.AutoSize = true;
            this.chkBothSync.Location = new System.Drawing.Point(3, 3);
            this.chkBothSync.Name = "chkBothSync";
            this.chkBothSync.Size = new System.Drawing.Size(72, 16);
            this.chkBothSync.TabIndex = 20;
            this.chkBothSync.Text = "双向同步";
            this.chkBothSync.UseVisualStyleBackColor = true;
            this.chkBothSync.CheckedChanged += new System.EventHandler(this.checkBox1_CheckedChanged_1);
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(187, 3);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(51, 21);
            this.textBox1.TabIndex = 25;
            this.textBox1.TextChanged += new System.EventHandler(this.textbox1_TextChanged);
            // 
            // lblTime
            // 
            this.lblTime.AutoSize = true;
            this.lblTime.Location = new System.Drawing.Point(0, 5);
            this.lblTime.Margin = new System.Windows.Forms.Padding(0);
            this.lblTime.Name = "lblTime";
            this.lblTime.Size = new System.Drawing.Size(53, 12);
            this.lblTime.TabIndex = 24;
            this.lblTime.Text = "每手用时";
            // 
            // btnTogglePonder
            // 
            this.btnTogglePonder.Location = new System.Drawing.Point(202, 153);
            this.btnTogglePonder.Name = "btnTogglePonder";
            this.btnTogglePonder.Size = new System.Drawing.Size(82, 23);
            this.btnTogglePonder.TabIndex = 21;
            this.btnTogglePonder.Text = "停止/分析";
            this.btnTogglePonder.UseVisualStyleBackColor = true;
            this.btnTogglePonder.Click += new System.EventHandler(this.button7_Click_1);
            // 
            // lblTotalVisits
            // 
            this.lblTotalVisits.AutoSize = true;
            this.lblTotalVisits.Location = new System.Drawing.Point(2, 5);
            this.lblTotalVisits.Name = "lblTotalVisits";
            this.lblTotalVisits.Size = new System.Drawing.Size(101, 12);
            this.lblTotalVisits.TabIndex = 26;
            this.lblTotalVisits.Text = "最大计算量(选填)";
            // 
            // textBox2
            // 
            this.textBox2.Location = new System.Drawing.Point(350, 3);
            this.textBox2.Name = "textBox2";
            this.textBox2.Size = new System.Drawing.Size(84, 21);
            this.textBox2.TabIndex = 27;
            this.textBox2.TextChanged += new System.EventHandler(this.textBox2_TextChanged);
            // 
            // lblBestMoveVisits
            // 
            this.lblBestMoveVisits.AutoSize = true;
            this.lblBestMoveVisits.Location = new System.Drawing.Point(2, 5);
            this.lblBestMoveVisits.Name = "lblBestMoveVisits";
            this.lblBestMoveVisits.Size = new System.Drawing.Size(101, 12);
            this.lblBestMoveVisits.TabIndex = 28;
            this.lblBestMoveVisits.Text = "首位计算量(选填)";
            // 
            // textBox3
            // 
            this.textBox3.Location = new System.Drawing.Point(350, 3);
            this.textBox3.Name = "textBox3";
            this.textBox3.Size = new System.Drawing.Size(84, 21);
            this.textBox3.TabIndex = 29;
            this.textBox3.TextChanged += new System.EventHandler(this.textBox3_TextChanged);
            // 
            // btnHelp
            // 
            this.btnHelp.Location = new System.Drawing.Point(512, 4);
            this.btnHelp.Name = "btnHelp";
            this.btnHelp.Size = new System.Drawing.Size(44, 26);
            this.btnHelp.TabIndex = 30;
            this.btnHelp.Text = "帮助";
            this.btnHelp.UseVisualStyleBackColor = true;
            this.btnHelp.Click += new System.EventHandler(this.button1_Click);
            // 
            // btnCheckUpdate
            // 
            this.btnCheckUpdate.Location = new System.Drawing.Point(538, 126);
            this.btnCheckUpdate.Name = "btnCheckUpdate";
            this.btnCheckUpdate.Size = new System.Drawing.Size(92, 23);
            this.btnCheckUpdate.TabIndex = 31;
            this.btnCheckUpdate.Text = "检查更新";
            this.btnCheckUpdate.UseVisualStyleBackColor = true;
            this.btnCheckUpdate.Click += new System.EventHandler(this.btnCheckUpdate_Click);
            // 
            // lblPlayCondition
            // 
            this.lblPlayCondition.AutoSize = true;
            this.lblPlayCondition.Location = new System.Drawing.Point(0, 5);
            this.lblPlayCondition.Name = "lblPlayCondition";
            this.lblPlayCondition.Size = new System.Drawing.Size(107, 12);
            this.lblPlayCondition.TabIndex = 31;
            this.lblPlayCondition.Text = "引擎自动落子条件:";
            // 
            // groupBox4
            // 
            this.groupBox4.Controls.Add(this.flowLayoutPanel2);
            this.groupBox4.Controls.Add(this.flowLayoutPanel1);
            this.groupBox4.Location = new System.Drawing.Point(7, 59);
            this.groupBox4.Margin = new System.Windows.Forms.Padding(0);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Padding = new System.Windows.Forms.Padding(0);
            this.groupBox4.Size = new System.Drawing.Size(541, 63);
            this.groupBox4.TabIndex = 32;
            this.groupBox4.TabStop = false;
            // 
            // flowLayoutPanel2
            // 
            this.flowLayoutPanel2.Controls.Add(this.chkAutoPlay);
            this.flowLayoutPanel2.Controls.Add(this.radioWhite);
            this.flowLayoutPanel2.Controls.Add(this.panel3);
            this.flowLayoutPanel2.Controls.Add(this.textBox1);
            this.flowLayoutPanel2.Controls.Add(this.panel4);
            this.flowLayoutPanel2.Controls.Add(this.textBox3);
            this.flowLayoutPanel2.Location = new System.Drawing.Point(1, 34);
            this.flowLayoutPanel2.Margin = new System.Windows.Forms.Padding(4);
            this.flowLayoutPanel2.Name = "flowLayoutPanel2";
            this.flowLayoutPanel2.Size = new System.Drawing.Size(588, 24);
            this.flowLayoutPanel2.TabIndex = 39;
            // 
            // chkAutoPlay
            // 
            this.chkAutoPlay.AutoSize = true;
            this.chkAutoPlay.Location = new System.Drawing.Point(3, 3);
            this.chkAutoPlay.Name = "chkAutoPlay";
            this.chkAutoPlay.Size = new System.Drawing.Size(72, 16);
            this.chkAutoPlay.TabIndex = 32;
            this.chkAutoPlay.Text = "自动落子";
            this.chkAutoPlay.UseVisualStyleBackColor = true;
            this.chkAutoPlay.CheckedChanged += new System.EventHandler(this.chkAutoPlay_CheckedChanged);
            // 
            // panel3
            // 
            this.panel3.AutoSize = true;
            this.panel3.Controls.Add(this.lblTime);
            this.panel3.Location = new System.Drawing.Point(131, 0);
            this.panel3.Margin = new System.Windows.Forms.Padding(0);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(53, 17);
            this.panel3.TabIndex = 33;
            // 
            // panel4
            // 
            this.panel4.AutoSize = true;
            this.panel4.Controls.Add(this.lblBestMoveVisits);
            this.panel4.Location = new System.Drawing.Point(241, 0);
            this.panel4.Margin = new System.Windows.Forms.Padding(0);
            this.panel4.Name = "panel4";
            this.panel4.Size = new System.Drawing.Size(106, 17);
            this.panel4.TabIndex = 34;
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Controls.Add(this.chkBothSync);
            this.flowLayoutPanel1.Controls.Add(this.radioBlack);
            this.flowLayoutPanel1.Controls.Add(this.panel1);
            this.flowLayoutPanel1.Controls.Add(this.panel2);
            this.flowLayoutPanel1.Controls.Add(this.textBox2);
            this.flowLayoutPanel1.Location = new System.Drawing.Point(1, 9);
            this.flowLayoutPanel1.Margin = new System.Windows.Forms.Padding(4);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(569, 32);
            this.flowLayoutPanel1.TabIndex = 39;
            // 
            // panel1
            // 
            this.panel1.AutoSize = true;
            this.panel1.Controls.Add(this.lblPlayCondition);
            this.panel1.Location = new System.Drawing.Point(131, 0);
            this.panel1.Margin = new System.Windows.Forms.Padding(0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(110, 17);
            this.panel1.TabIndex = 23;
            // 
            // panel2
            // 
            this.panel2.AutoSize = true;
            this.panel2.Controls.Add(this.lblTotalVisits);
            this.panel2.Location = new System.Drawing.Point(241, 0);
            this.panel2.Margin = new System.Windows.Forms.Padding(0);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(106, 17);
            this.panel2.TabIndex = 24;
            // 
            // btnSettings
            // 
            this.btnSettings.Location = new System.Drawing.Point(560, 4);
            this.btnSettings.Name = "btnSettings";
            this.btnSettings.Size = new System.Drawing.Size(71, 26);
            this.btnSettings.TabIndex = 32;
            this.btnSettings.Text = "参数设置";
            this.btnSettings.UseVisualStyleBackColor = true;
            this.btnSettings.Click += new System.EventHandler(this.btnSettings_Click);
            // 
            // btnExchange
            // 
            this.btnExchange.Location = new System.Drawing.Point(287, 153);
            this.btnExchange.Name = "btnExchange";
            this.btnExchange.Size = new System.Drawing.Size(82, 23);
            this.btnExchange.TabIndex = 34;
            this.btnExchange.Text = "交换顺序";
            this.btnExchange.UseVisualStyleBackColor = true;
            this.btnExchange.Click += new System.EventHandler(this.button8_Click);
            // 
            // chkShowInBoard
            // 
            this.chkShowInBoard.AutoSize = true;
            this.chkShowInBoard.Location = new System.Drawing.Point(338, 130);
            this.chkShowInBoard.Name = "chkShowInBoard";
            this.chkShowInBoard.Size = new System.Drawing.Size(120, 16);
            this.chkShowInBoard.TabIndex = 35;
            this.chkShowInBoard.Text = "原棋盘上显示选点";
            this.chkShowInBoard.UseVisualStyleBackColor = true;
            this.chkShowInBoard.CheckedChanged += new System.EventHandler(this.chkShowInBoard_CheckedChanged);
            // 
            // btnKomi65
            // 
            this.btnKomi65.Location = new System.Drawing.Point(512, 35);
            this.btnKomi65.Name = "btnKomi65";
            this.btnKomi65.Size = new System.Drawing.Size(118, 25);
            this.btnKomi65.TabIndex = 36;
            this.btnKomi65.Text = "6.5目规则设置方法";
            this.btnKomi65.UseVisualStyleBackColor = true;
            this.btnKomi65.Click += new System.EventHandler(this.button9_Click);
            // 
            // btnFastSync
            // 
            this.btnFastSync.Location = new System.Drawing.Point(7, 35);
            this.btnFastSync.Name = "btnFastSync";
            this.btnFastSync.Size = new System.Drawing.Size(78, 25);
            this.btnFastSync.TabIndex = 37;
            this.btnFastSync.Text = "一键同步";
            this.btnFastSync.UseVisualStyleBackColor = true;
            this.btnFastSync.Click += new System.EventHandler(this.button10_Click);
            // 
            // btnCircleRow1
            // 
            this.btnCircleRow1.Location = new System.Drawing.Point(241, 126);
            this.btnCircleRow1.Name = "btnCircleRow1";
            this.btnCircleRow1.Size = new System.Drawing.Size(92, 23);
            this.btnCircleRow1.TabIndex = 38;
            this.btnCircleRow1.Text = "框选1路线";
            this.btnCircleRow1.UseVisualStyleBackColor = true;
            this.btnCircleRow1.Click += new System.EventHandler(this.button11_Click);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(640, 182);
            this.Controls.Add(this.btnCircleRow1);
            this.Controls.Add(this.btnFastSync);
            this.Controls.Add(this.btnKomi65);
            this.Controls.Add(this.chkShowInBoard);
            this.Controls.Add(this.btnExchange);
            this.Controls.Add(this.btnSettings);
            this.Controls.Add(this.btnCheckUpdate);
            this.Controls.Add(this.groupBox4);
            this.Controls.Add(this.btnHelp);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.btnForceRebuild);
            this.Controls.Add(this.btnClearBoard);
            this.Controls.Add(this.btnTogglePonder);
            this.Controls.Add(this.btnKeepSync);
            this.Controls.Add(this.btnOneTimeSync);
            this.Controls.Add(this.btnClickBoard);
            this.Controls.Add(this.btnCircleBoard);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "棋盘同步工具";
            this.TopMost = true;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.form_closing);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox4.ResumeLayout(false);
            this.flowLayoutPanel2.ResumeLayout(false);
            this.flowLayoutPanel2.PerformLayout();
            this.panel3.ResumeLayout(false);
            this.panel3.PerformLayout();
            this.panel4.ResumeLayout(false);
            this.panel4.PerformLayout();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.panel2.ResumeLayout(false);
            this.panel2.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button btnCircleBoard;
        private System.Windows.Forms.Button btnClickBoard;
        private System.Windows.Forms.Button btnOneTimeSync;
        private System.Windows.Forms.Button btnKeepSync;
        private System.Windows.Forms.Button btnForceRebuild;
        private System.Windows.Forms.Button btnClearBoard;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.RadioButton rdoFoxBack;
        private System.Windows.Forms.RadioButton rdoYike;
        private System.Windows.Forms.RadioButton rdoSina;
        private System.Windows.Forms.RadioButton rdoBack;
        private System.Windows.Forms.RadioButton rdoTygem;
        private System.Windows.Forms.RadioButton rdoFox;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Label lblBoardSize;
        private System.Windows.Forms.RadioButton rdo9x9;
        private System.Windows.Forms.RadioButton rdo13x13;
        private System.Windows.Forms.RadioButton rdo19x19;
        private System.Windows.Forms.RadioButton radioWhite;
        private System.Windows.Forms.RadioButton radioBlack;
        private System.Windows.Forms.Button btnTogglePonder;
        private System.Windows.Forms.CheckBox chkBothSync;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Label lblTime;
        private System.Windows.Forms.Label lblBestMoveVisits;
        private System.Windows.Forms.TextBox textBox2;
        private System.Windows.Forms.Label lblTotalVisits;
        private System.Windows.Forms.TextBox textBox3;
        private System.Windows.Forms.Button btnHelp;
        private System.Windows.Forms.Button btnCheckUpdate;
        private System.Windows.Forms.Label lblPlayCondition;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.RadioButton rdoFore;
        private System.Windows.Forms.CheckBox chkAutoPlay;
        private System.Windows.Forms.Button btnSettings;
        private System.Windows.Forms.RadioButton rdoOtherBoard;
        private System.Windows.Forms.Button btnExchange;
        private System.Windows.Forms.CheckBox chkShowInBoard;
        private System.Windows.Forms.Button btnKomi65;
        private System.Windows.Forms.Button btnFastSync;
        private System.Windows.Forms.Button btnCircleRow1;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.TextBox txtBoardHeight;
        private System.Windows.Forms.TextBox txtBoardWidth;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel2;
        private System.Windows.Forms.Panel panel3;
        private System.Windows.Forms.Panel panel4;
    }
}

