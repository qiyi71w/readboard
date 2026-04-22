using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MouseKeyboardActivityMonitor;
using MouseKeyboardActivityMonitor.WinApi;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.ComponentModel;
using LwInterop = Interop.lw;

namespace readboard
{
    public partial class MainForm : Form, IProtocolCommandHost, ISyncCoordinatorHost
    {
        // Boolean showDebugImage = true;
        Boolean clicked = false;

        public static int ox1;
        private int selectionX1;
        int ox2;
        public static int oy1;
        private int selectionY1;
        int oy2;
        IntPtr hwnd = IntPtr.Zero;
        Form2 form2;

        private const int TYPE_FOX = 0;
        private const int TYPE_TYGEM = 1;
        private const int TYPE_SINA = 2;
        private const int TYPE_BACKGROUND = 3;
        private const int TYPE_FOX_BACKGROUND_PLACE = 4;
        private const int TYPE_FOREGROUND = 5;
        private const int ContinuousSyncPollIntervalMs = 100;
        public static int type = TYPE_FOX;
        private int currentSyncType = TYPE_FOX;
        // Boolean isQTYC = false;
        // int boardWidth=19;
        int boardH = 19;
        int boardW = 19;
        //Boolean noticeLast = true;
        Boolean canUseLW = false;
        //Boolean noLw = false;
        Boolean isMannulCircle = false;
        float factor = 1.0f;
        private KeyboardHookListener hookListener;
        private readonly LaunchOptions launchOptions;
        private readonly ISyncSessionCoordinator sessionCoordinator;
        private readonly ILegacySelectionCalibrationService selectionCalibrationService;
        private readonly UiThreadInvoker uiThreadInvoker;
        private readonly SerialBackgroundWorkQueue placeRequestQueue;
        private readonly object placeProtocolSyncRoot = new object();
        private readonly object protocolCommandSyncRoot = new object();
        private readonly GitHubUpdateChecker updateChecker = new GitHubUpdateChecker();
        private readonly ToolTip showInBoardShortcutToolTip = new ToolTip();
        private readonly Queue<Action> pendingProtocolCommands = new Queue<Action>();
        private readonly BackgroundSelectionWindowBindingCoordinator backgroundSelectionWindowBindingCoordinator =
            new BackgroundSelectionWindowBindingCoordinator();
        private const int MainFormMinimumLogicalWidth = 360;
        private const int MainFormScreenLogicalPadding = 40;
        private FoxWindowContext lastFoxWindowContext = FoxWindowContext.Unknown();
        private FoxWindowBinding foxWindowBinding = null;
        private bool hasRetainedFoxTitleSnapshot = false;
        private string lastAppliedMainWindowTitle = string.Empty;

        int posX = -1;
        int posY = -1;

        LwInterop.lwsoft lw;
        private Button btnTheme;
        private ContextMenuStrip themeMenu;
        private ToolStripMenuItem menuThemeOptimized;
        private ToolStripMenuItem menuThemeClassic;
        private bool isMainFormSizeInitialized = false;
        private bool isApplyingMainFormLayout = false;
        private bool isShuttingDown = false;
        private bool closeRequestedBeforeHandle = false;
        private bool isInitializingProtocolState = true;
        private static readonly System.Drawing.Size MainFormDefaultSize = new System.Drawing.Size(792, 374);

        private readonly struct MainHeaderLayoutMetrics
        {
            public MainHeaderLayoutMetrics(int platformBottom, int utilityBottom, int platformWidth, bool utilitiesInRightColumn)
            {
                PlatformBottom = platformBottom;
                UtilityBottom = utilityBottom;
                PlatformWidth = platformWidth;
                UtilitiesInRightColumn = utilitiesInRightColumn;
            }

            public int PlatformBottom { get; }

            public int UtilityBottom { get; }

            public int PlatformWidth { get; }

            public bool UtilitiesInRightColumn { get; }
        }

        private static Boolean IsFoxSyncType(int syncType)
        {
            return syncType == TYPE_FOX || syncType == TYPE_FOX_BACKGROUND_PLACE;
        }

        private static Boolean UsesManualSelectionType(int syncType)
        {
            return syncType == TYPE_BACKGROUND || syncType == TYPE_FOREGROUND;
        }

        private static Boolean SupportsFastSyncType(int syncType)
        {
            return IsFoxSyncType(syncType) || syncType == TYPE_TYGEM || syncType == TYPE_SINA;
        }

        private bool SupportsShowInBoard()
        {
            return CurrentSyncType != TYPE_FOREGROUND;
        }

        private int CurrentSyncType
        {
            get { return currentSyncType; }
        }

        private void SetCurrentSyncType(int syncType)
        {
            currentSyncType = syncType;
            type = syncType;
        }

        private void UpdateSelectionBounds(int x1, int y1, int x2, int y2)
        {
            selectionX1 = x1;
            selectionY1 = y1;
            ox1 = x1;
            oy1 = y1;
            ox2 = x2;
            oy2 = y2;
        }

        private static System.Drawing.Point ClampToScreenWorkingArea(System.Drawing.Point location, System.Drawing.Size windowSize)
        {
            Rectangle workingArea = Screen.FromPoint(location).WorkingArea;
            int maxX = Math.Max(workingArea.Left, workingArea.Right - windowSize.Width);
            int maxY = Math.Max(workingArea.Top, workingArea.Bottom - windowSize.Height);
            return new System.Drawing.Point(
                Math.Min(Math.Max(workingArea.Left, location.X), maxX),
                Math.Min(Math.Max(workingArea.Top, location.Y), maxY));
        }

        private void RestoreSavedWindowLocation()
        {
            if (posX == -1 || posY == -1)
                return;

            Location = ClampToScreenWorkingArea(new System.Drawing.Point(posX, posY), Size);
        }

        private void RestoreSavedWindowLocationIfNeeded()
        {
            if (isMainFormSizeInitialized)
                return;

            RestoreSavedWindowLocation();
        }

        private Point? TryGetStartupReferencePoint()
        {
            if (isMainFormSizeInitialized || posX == -1 || posY == -1)
                return null;

            return new Point(posX, posY);
        }

        private Point ResolveLayoutReferencePoint()
        {
            return DisplayScaling.ResolveReferencePoint(
                IsHandleCreated,
                Bounds,
                Location,
                TryGetStartupReferencePoint());
        }

        internal void RefreshShowInBoardShortcutToolTip()
        {
            showInBoardShortcutToolTip.SetToolTip(this.chkShowInBoard, Program.disableShowInBoardShortcut ? string.Empty : "Ctrl+X");
        }

        private bool IsOptimizedTheme()
        {
            return Program.uiThemeMode == Program.UiThemeOptimized;
        }

        private IEnumerable<GroupBox> MainThemeGroups()
        {
            return new[] { groupBox1, groupBox2, groupBox4 };
        }

        private IEnumerable<Control> MainThemeSurfaces()
        {
            return new Control[] { flowLayoutPanel1, flowLayoutPanel2, panel1, panel2, panel3, panel4 };
        }

        private IEnumerable<ButtonBase> MainThemeOptions()
        {
            return new ButtonBase[] { rdoFox, rdoFoxBack, rdoTygem, rdoSina, rdoBack, rdoFore, rdo19x19, rdo13x13, rdo9x9, rdoOtherBoard, chkBothSync, chkAutoPlay, chkShowInBoard, radioBlack, radioWhite };
        }

        private IEnumerable<TextBox> MainThemeInputs()
        {
            return new[] { textBox1, textBox2, textBox3, txtBoardWidth, txtBoardHeight };
        }

        private IEnumerable<Label> MainThemeLabels()
        {
            return new[] { lblBoardSize, lblPlayCondition, lblTime, lblTotalVisits, lblBestMoveVisits, label6 };
        }

        private IEnumerable<Button> MainPrimaryButtons()
        {
            return new[] { btnFastSync, btnKeepSync };
        }

        private IEnumerable<Button> MainSecondaryButtons()
        {
            return new[] { btnClickBoard, btnCircleBoard, btnCircleRow1, btnOneTimeSync, btnTogglePonder, btnExchange, btnForceRebuild, btnSettings, btnHelp, btnKomi65, btnCheckUpdate, btnTheme };
        }

        private IEnumerable<Button> MainTypographyButtons()
        {
            return new[] { btnFastSync, btnKeepSync, btnClickBoard, btnCircleBoard, btnCircleRow1, btnOneTimeSync, btnTogglePonder, btnExchange, btnForceRebuild, btnSettings, btnHelp, btnKomi65, btnCheckUpdate, btnClearBoard, btnTheme };
        }

        private void EnsureThemeControls()
        {
            if (btnTheme != null)
                return;

            btnTheme = new Button();
            btnTheme.Name = "btnTheme";
            btnTheme.Size = new System.Drawing.Size(68, 32);
            btnTheme.TabIndex = 39;
            btnTheme.UseVisualStyleBackColor = true;
            btnTheme.Click += btnTheme_Click;

            themeMenu = new ContextMenuStrip();
            themeMenu.ShowImageMargin = false;
            menuThemeOptimized = new ToolStripMenuItem();
            menuThemeClassic = new ToolStripMenuItem();
            menuThemeOptimized.Click += menuThemeOptimized_Click;
            menuThemeClassic.Click += menuThemeClassic_Click;
            themeMenu.Items.Add(menuThemeOptimized);
            themeMenu.Items.Add(menuThemeClassic);
            Controls.Add(btnTheme);
            btnTheme.BringToFront();
        }

        private void ApplyThemeControlTexts()
        {
            EnsureThemeControls();
            btnTheme.Text = getLangStr("MainForm_btnTheme");
            menuThemeOptimized.Text = getLangStr("MainForm_themeOptimized");
            menuThemeClassic.Text = getLangStr("MainForm_themeClassic");
            menuThemeOptimized.Checked = IsOptimizedTheme();
            menuThemeClassic.Checked = !IsOptimizedTheme();
        }

        private void btnTheme_Click(object sender, EventArgs e)
        {
            if (themeMenu != null)
                themeMenu.Show(btnTheme, new System.Drawing.Point(0, btnTheme.Height));
        }

        private void menuThemeOptimized_Click(object sender, EventArgs e)
        {
            SwitchTheme(Program.UiThemeOptimized);
        }

        private void menuThemeClassic_Click(object sender, EventArgs e)
        {
            SwitchTheme(Program.UiThemeClassic);
        }

        private void SwitchTheme(int themeMode)
        {
            if (Program.uiThemeMode == themeMode)
                return;

            Program.uiThemeMode = themeMode;
            ApplyMainFormUi();
            saveOtherConfig();
        }

        private void ApplyMainFormUi()
        {
            if (isApplyingMainFormLayout)
                return;

            isApplyingMainFormLayout = true;
            SuspendLayout();
            try
            {
                DoubleBuffered = true;
                AutoScroll = true;
                EnsureThemeControls();
                factor = GetCurrentDpiScale();
                ConstrainMainFormWidth();
                groupBox1.Text = getLangStr("MainForm_groupPlatform");
                groupBox2.Text = getLangStr("MainForm_groupBoard");
                groupBox4.Text = getLangStr("MainForm_groupSync");
                rdoOtherBoard.Text = getLangStr("MainForm_rdoCustomBoard");
                label6.Text = "x";
                ApplyMainFormTypography();
                ApplyThemeControlTexts();
                ApplyMainFormTheme();
                MainHeaderLayoutMetrics headerLayout = ArrangeMainHeader();
                int boardTop = headerLayout.UtilitiesInRightColumn
                    ? headerLayout.PlatformBottom + ScaleValue(12)
                    : headerLayout.UtilityBottom + ScaleValue(12);
                int boardBottom = ArrangeMainBoardSection(boardTop, headerLayout);
                int syncBottom = ArrangeMainSyncSection(Math.Max(boardBottom, headerLayout.UtilityBottom) + ScaleValue(12));
                ArrangeMainActions(syncBottom + ScaleValue(12));
            }
            finally
            {
                ResumeLayout(false);
                PerformLayout();
                RestoreSavedWindowLocationIfNeeded();
                if (IsHandleCreated && !isMainFormSizeInitialized)
                    isMainFormSizeInitialized = true;
                factor = GetCurrentDpiScale();
                isApplyingMainFormLayout = false;
            }
        }

        private void ApplyMainFormTypography()
        {
            Font = UiTheme.BodyFont;

            foreach (GroupBox group in MainThemeGroups())
                group.Font = UiTheme.SectionFont;

            foreach (Control surface in MainThemeSurfaces())
                surface.Font = UiTheme.BodyFont;

            foreach (ButtonBase option in MainThemeOptions())
                option.Font = UiTheme.BodyFont;

            foreach (TextBox textBox in MainThemeInputs())
                textBox.Font = UiTheme.BodyFont;

            foreach (Label label in MainThemeLabels())
                label.Font = UiTheme.BodyFont;

            foreach (Button button in MainTypographyButtons())
                button.Font = UiTheme.BodyFont;
        }

        private void ApplyMainFormTheme()
        {
            if (IsOptimizedTheme())
            {
                UiTheme.ApplyWindow(this);
                ApplyOptimizedMainFormTheme();
                return;
            }

            ApplyClassicMainFormTheme();
        }

        private void ApplyOptimizedMainFormTheme()
        {
            foreach (GroupBox group in MainThemeGroups())
                UiTheme.StyleGroupBox(group);

            foreach (Control surface in MainThemeSurfaces())
                UiTheme.StylePanelSurface(surface);

            foreach (ButtonBase option in MainThemeOptions())
                UiTheme.StyleOption(option);

            foreach (TextBox textBox in MainThemeInputs())
                UiTheme.StyleInput(textBox);

            foreach (Label label in MainThemeLabels())
                UiTheme.StyleSubtleLabel(label);

            foreach (Button button in MainPrimaryButtons())
                UiTheme.StylePrimaryButton(button);

            foreach (Button button in MainSecondaryButtons())
                UiTheme.StyleSecondaryButton(button);

            UiTheme.StyleDangerButton(btnClearBoard);
        }

        private void ApplyClassicMainFormTheme()
        {
            BackColor = SystemColors.Control;
            ForeColor = SystemColors.ControlText;
            Font = Control.DefaultFont;

            foreach (GroupBox group in MainThemeGroups())
            {
                group.BackColor = SystemColors.Control;
                group.ForeColor = SystemColors.ControlText;
                group.Font = Control.DefaultFont;
                group.Padding = new Padding(3);
            }

            foreach (Control surface in MainThemeSurfaces())
            {
                surface.BackColor = SystemColors.Control;
                surface.ForeColor = SystemColors.ControlText;
                surface.Font = Control.DefaultFont;
            }

            foreach (ButtonBase option in MainThemeOptions())
            {
                option.BackColor = SystemColors.Control;
                option.ForeColor = SystemColors.ControlText;
                option.Font = Control.DefaultFont;
                option.Cursor = Cursors.Default;
                option.FlatStyle = FlatStyle.Standard;
                option.UseVisualStyleBackColor = true;
            }

            foreach (TextBox textBox in MainThemeInputs())
            {
                textBox.BackColor = SystemColors.Window;
                textBox.ForeColor = SystemColors.WindowText;
                textBox.Font = Control.DefaultFont;
                textBox.BorderStyle = BorderStyle.Fixed3D;
            }

            foreach (Label label in MainThemeLabels())
            {
                label.BackColor = Color.Transparent;
                label.ForeColor = SystemColors.ControlText;
                label.Font = Control.DefaultFont;
                label.BorderStyle = BorderStyle.None;
                label.Padding = Padding.Empty;
            }

            foreach (Button button in MainPrimaryButtons())
            {
                button.FlatStyle = FlatStyle.System;
                button.UseVisualStyleBackColor = true;
                button.Font = Control.DefaultFont;
                button.Cursor = Cursors.Default;
            }

            foreach (Button button in MainSecondaryButtons())
            {
                button.FlatStyle = FlatStyle.System;
                button.UseVisualStyleBackColor = true;
                button.Font = Control.DefaultFont;
                button.Cursor = Cursors.Default;
            }

            btnClearBoard.FlatStyle = FlatStyle.System;
            btnClearBoard.UseVisualStyleBackColor = true;
            btnClearBoard.Font = Control.DefaultFont;
            btnClearBoard.Cursor = Cursors.Default;
        }

        private MainHeaderLayoutMetrics ArrangeMainHeader()
        {
            if (CanUseLegacyMainDesktopLayout())
                return ArrangeLegacyMainHeader();

            return ArrangeAdaptiveMainHeader();
        }

        private MainHeaderLayoutMetrics ArrangeLegacyMainHeader()
        {
            int left = ScaleValue(12);
            int top = ScaleValue(12);
            int buttonHeight = ScaleValue(32);
            int optionLeft = ScaleValue(14);
            int optionTop = ScaleValue(31);
            int optionGap = ScaleValue(10);
            int utilityGap = ScaleValue(8);
            int settingsWidth = MeasureButtonWidth(btnSettings, 72);
            int helpWidth = MeasureButtonWidth(btnHelp, 68);
            int themeWidth = MeasureButtonWidth(btnTheme, 68);
            int utilityRight = ClientSize.Width - left;
            int themeLeft = utilityRight - themeWidth;
            int helpLeft = themeLeft - utilityGap - helpWidth;
            int settingsLeft = helpLeft - utilityGap - settingsWidth;

            groupBox1.SetBounds(left, top, settingsLeft - left - utilityGap, ScaleValue(72));
            rdoFox.Location = new Point(optionLeft, optionTop);
            rdoFoxBack.Location = new Point(rdoFox.Right + optionGap, optionTop);
            rdoTygem.Location = new Point(rdoFoxBack.Right + optionGap, optionTop);
            rdoSina.Location = new Point(rdoTygem.Right + optionGap, optionTop);
            rdoBack.Location = new Point(rdoSina.Right + optionGap, optionTop);
            rdoFore.Location = new Point(rdoBack.Right + optionGap, optionTop);
            btnSettings.SetBounds(settingsLeft, top, settingsWidth, buttonHeight);
            btnHelp.SetBounds(helpLeft, top, helpWidth, buttonHeight);
            btnTheme.SetBounds(themeLeft, top, themeWidth, buttonHeight);
            btnKomi65.SetBounds(settingsLeft, top + buttonHeight + utilityGap, utilityRight - settingsLeft, buttonHeight);
            btnCheckUpdate.SetBounds(settingsLeft, btnKomi65.Bottom + utilityGap, utilityRight - settingsLeft, buttonHeight);
            return new MainHeaderLayoutMetrics(groupBox1.Bottom, btnCheckUpdate.Bottom, groupBox1.Width, true);
        }

        private MainHeaderLayoutMetrics ArrangeAdaptiveMainHeader()
        {
            int left = ScaleValue(12);
            int top = ScaleValue(12);
            int contentWidth = ClientSize.Width - left * 2;
            int buttonHeight = ScaleValue(32);
            int optionGap = ScaleValue(10);
            int rowGap = ScaleValue(8);
            int buttonGap = ScaleValue(8);
            int optionLeft = ScaleValue(14);
            int optionTop = ScaleValue(31);
            int groupPaddingBottom = ScaleValue(16);
            int settingsWidth = MeasureButtonWidth(btnSettings, 72);
            int helpWidth = MeasureButtonWidth(btnHelp, 68);
            int themeWidth = MeasureButtonWidth(btnTheme, 68);
            int komiWidth = MeasureButtonWidth(btnKomi65, 170);
            int updateWidth = MeasureButtonWidth(btnCheckUpdate, 170);
            int utilityRowWidth = settingsWidth + helpWidth + themeWidth + buttonGap * 2;
            int utilityColumnWidth = Math.Max(utilityRowWidth, Math.Max(komiWidth, updateWidth));
            int minimumPlatformWidth = Math.Min(contentWidth, MeasureOptionsWidth(new ButtonBase[] { rdoFox, rdoFoxBack, rdoTygem, rdoSina, rdoBack, rdoFore }, optionGap) + ScaleValue(28));
            bool canUseSideBySide = contentWidth >= minimumPlatformWidth + buttonGap + utilityColumnWidth + ScaleValue(24);

            int groupWidth = canUseSideBySide ? contentWidth - utilityColumnWidth - buttonGap : contentWidth;
            groupBox1.SetBounds(left, top, groupWidth, 0);
            int groupBottom = LayoutOptionsRow(new ButtonBase[] { rdoFox, rdoFoxBack, rdoTygem, rdoSina, rdoBack, rdoFore }, groupBox1, optionLeft, optionTop, optionGap, rowGap);
            groupBox1.Height = groupBottom + groupPaddingBottom;

            if (canUseSideBySide)
            {
                int utilityLeft = groupBox1.Right + buttonGap;
                btnSettings.SetBounds(utilityLeft, top, settingsWidth, buttonHeight);
                btnHelp.SetBounds(btnSettings.Right + buttonGap, top, helpWidth, buttonHeight);
                btnTheme.SetBounds(btnHelp.Right + buttonGap, top, themeWidth, buttonHeight);
                btnKomi65.SetBounds(utilityLeft, btnSettings.Bottom + rowGap, utilityColumnWidth, buttonHeight);
                btnCheckUpdate.SetBounds(utilityLeft, btnKomi65.Bottom + rowGap, utilityColumnWidth, buttonHeight);
                return new MainHeaderLayoutMetrics(groupBox1.Bottom, btnCheckUpdate.Bottom, groupBox1.Width, true);
            }

            int utilityTop = groupBox1.Bottom + rowGap;
            btnSettings.SetBounds(left, utilityTop, settingsWidth, buttonHeight);
            btnHelp.SetBounds(btnSettings.Right + buttonGap, utilityTop, helpWidth, buttonHeight);
            btnTheme.SetBounds(btnHelp.Right + buttonGap, utilityTop, themeWidth, buttonHeight);
            btnKomi65.SetBounds(left, btnSettings.Bottom + rowGap, contentWidth, buttonHeight);
            btnCheckUpdate.SetBounds(left, btnKomi65.Bottom + rowGap, contentWidth, buttonHeight);
            return new MainHeaderLayoutMetrics(groupBox1.Bottom, btnCheckUpdate.Bottom, contentWidth, false);
        }

        private int ArrangeMainBoardSection(int top, MainHeaderLayoutMetrics headerLayout)
        {
            if (CanUseLegacyMainDesktopLayout())
                return ArrangeLegacyMainBoardSection(top);

            return ArrangeAdaptiveMainBoardSection(top, headerLayout);
        }

        private int ArrangeLegacyMainBoardSection(int top)
        {
            int left = ScaleValue(12);
            int optionTop = ScaleValue(29);
            int optionGap = ScaleValue(8);
            int textBoxWidth = ScaleValue(34);
            int inputTop = ScaleValue(27);
            int inputHeight = ScaleValue(24);
            int customInputGap = ScaleValue(12);
            int separatorGap = ScaleValue(4);
            int sectionPadding = ScaleValue(16);

            lblBoardSize.SetBounds(sectionPadding, ScaleValue(30), Math.Max(lblBoardSize.PreferredSize.Width, ScaleValue(52)), ScaleValue(20));
            lblBoardSize.TextAlign = ContentAlignment.MiddleLeft;
            rdo19x19.Location = new Point(lblBoardSize.Right + ScaleValue(6), optionTop);
            rdo13x13.Location = new Point(rdo19x19.Right + optionGap, optionTop);
            rdo9x9.Location = new Point(rdo13x13.Right + optionGap, optionTop);
            rdoOtherBoard.Location = new Point(rdo9x9.Right + optionGap + ScaleValue(4), optionTop);
            txtBoardWidth.AutoSize = false;
            txtBoardHeight.AutoSize = false;
            int customInputLeft = rdoOtherBoard.Right + customInputGap;
            txtBoardWidth.SetBounds(customInputLeft, inputTop, textBoxWidth, inputHeight);
            txtBoardWidth.TextAlign = HorizontalAlignment.Center;
            label6.TextAlign = ContentAlignment.MiddleCenter;
            label6.SetBounds(txtBoardWidth.Right + separatorGap, ScaleValue(30), ScaleValue(10), ScaleValue(18));
            txtBoardHeight.SetBounds(label6.Right + separatorGap, inputTop, textBoxWidth, inputHeight);
            txtBoardHeight.TextAlign = HorizontalAlignment.Center;
            groupBox2.SetBounds(left, top, txtBoardHeight.Right + sectionPadding, ScaleValue(72));
            return groupBox2.Bottom;
        }

        private int ArrangeAdaptiveMainBoardSection(int top, MainHeaderLayoutMetrics headerLayout)
        {
            int left = ScaleValue(12);
            int optionTop = ScaleValue(29);
            int optionGap = ScaleValue(8);
            int textBoxWidth = ScaleValue(42);
            int inputTop = ScaleValue(27);
            int inputHeight = ScaleValue(26);
            int customInputGap = ScaleValue(12);
            int separatorGap = ScaleValue(4);
            int contentWidth = ClientSize.Width - left * 2;
            int groupWidth = headerLayout.UtilitiesInRightColumn ? headerLayout.PlatformWidth : contentWidth;
            int sectionPadding = ScaleValue(16);
            int rowGap = ScaleValue(12);

            groupBox2.SetBounds(left, top, groupWidth, 0);
            lblBoardSize.SetBounds(sectionPadding, optionTop, Math.Max(lblBoardSize.PreferredSize.Width, ScaleValue(52)), ScaleValue(20));
            lblBoardSize.TextAlign = ContentAlignment.MiddleLeft;
            rdo19x19.Location = new System.Drawing.Point(lblBoardSize.Right + ScaleValue(6), optionTop);
            rdo13x13.Location = new System.Drawing.Point(rdo19x19.Right + optionGap, optionTop);
            rdo9x9.Location = new System.Drawing.Point(rdo13x13.Right + optionGap, optionTop);
            rdoOtherBoard.Location = new System.Drawing.Point(rdo9x9.Right + optionGap + ScaleValue(4), optionTop);
            txtBoardWidth.AutoSize = false;
            txtBoardHeight.AutoSize = false;
            int customInputLeft = rdoOtherBoard.Right + customInputGap;
            txtBoardWidth.SetBounds(customInputLeft, inputTop, textBoxWidth, inputHeight);
            txtBoardWidth.TextAlign = HorizontalAlignment.Center;
            label6.TextAlign = ContentAlignment.MiddleCenter;
            label6.SetBounds(txtBoardWidth.Right + separatorGap, inputTop + ScaleValue(4), ScaleValue(10), ScaleValue(18));
            txtBoardHeight.SetBounds(label6.Right + separatorGap, inputTop, textBoxWidth, inputHeight);
            txtBoardHeight.TextAlign = HorizontalAlignment.Center;
            if (txtBoardHeight.Right + sectionPadding > groupWidth)
            {
                int wrappedTop = rdoOtherBoard.Bottom + rowGap;
                txtBoardWidth.SetBounds(sectionPadding, wrappedTop, textBoxWidth, inputHeight);
                label6.SetBounds(txtBoardWidth.Right + separatorGap, wrappedTop + ScaleValue(2), ScaleValue(10), ScaleValue(18));
                txtBoardHeight.SetBounds(label6.Right + separatorGap, wrappedTop, textBoxWidth, inputHeight);
            }

            int bottom = Math.Max(Math.Max(txtBoardHeight.Bottom, rdoOtherBoard.Bottom), lblBoardSize.Bottom);
            groupBox2.Height = bottom + ScaleValue(18);
            return groupBox2.Bottom;
        }

        private int ArrangeMainSyncSection(int top)
        {
            if (CanUseLegacyMainDesktopLayout())
                return ArrangeLegacyMainSyncSection(top);

            return ArrangeAdaptiveMainSyncSection(top);
        }

        private int ArrangeLegacyMainSyncSection(int top)
        {
            int left = ScaleValue(12);
            int rowHeight = ScaleValue(24);
            int timeFieldGap = ScaleValue(8);
            int groupWidth = ClientSize.Width - ScaleValue(42);
            int rowWidth = groupWidth - ScaleValue(34);
            int sharedVisitsLabelWidth = GetSharedMainSyncVisitsLabelWidth();
            int sharedLegacyVisitsPanelWidth = GetLegacyMainSyncVisitsPanelWidth();

            groupBox4.SetBounds(left, top, groupWidth, ScaleValue(100));
            flowLayoutPanel1.SetBounds(ScaleValue(16), ScaleValue(28), rowWidth, ScaleValue(30));
            flowLayoutPanel2.SetBounds(ScaleValue(16), ScaleValue(62), rowWidth, ScaleValue(30));
            flowLayoutPanel1.WrapContents = false;
            flowLayoutPanel2.WrapContents = false;
            chkBothSync.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioBlack.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            chkAutoPlay.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioWhite.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            panel1.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel2.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel3.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel4.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel1.AutoSize = false;
            panel2.AutoSize = false;
            panel3.AutoSize = false;
            panel4.AutoSize = false;
            panel1.Size = new Size(
                Math.Max(ScaleValue(129) + timeFieldGap, lblPlayCondition.PreferredSize.Width + ScaleValue(22) + timeFieldGap),
                rowHeight);
            panel2.Size = new Size(sharedLegacyVisitsPanelWidth, rowHeight);
            panel3.Size = new Size(
                Math.Max(ScaleValue(61), lblTime.PreferredSize.Width + ScaleValue(8)),
                rowHeight);
            panel4.Size = new Size(sharedLegacyVisitsPanelWidth, rowHeight);
            lblPlayCondition.AutoSize = false;
            lblTotalVisits.AutoSize = false;
            lblTime.AutoSize = false;
            lblBestMoveVisits.AutoSize = false;
            lblPlayCondition.SetBounds(0, ScaleValue(3), lblPlayCondition.PreferredSize.Width, ScaleValue(18));
            lblTotalVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(18));
            lblTime.SetBounds(0, ScaleValue(3), lblTime.PreferredSize.Width, ScaleValue(18));
            lblBestMoveVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(18));
            lblPlayCondition.TextAlign = ContentAlignment.MiddleLeft;
            lblTotalVisits.TextAlign = ContentAlignment.MiddleLeft;
            lblTime.TextAlign = ContentAlignment.MiddleLeft;
            lblBestMoveVisits.TextAlign = ContentAlignment.MiddleLeft;
            textBox1.AutoSize = false;
            textBox2.AutoSize = false;
            textBox3.AutoSize = false;
            textBox1.Margin = new Padding(timeFieldGap, 1, 0, 0);
            textBox2.Margin = new Padding(ScaleValue(8), 1, 0, 0);
            textBox3.Margin = new Padding(ScaleValue(8), 1, 0, 0);
            textBox1.Size = new Size(ScaleValue(68), rowHeight);
            textBox2.Size = new Size(ScaleValue(92), rowHeight);
            textBox3.Size = new Size(ScaleValue(92), rowHeight);
            flowLayoutPanel1.Height = ScaleValue(30);
            flowLayoutPanel2.Height = ScaleValue(30);
            return groupBox4.Bottom;
        }

        private int ArrangeAdaptiveMainSyncSection(int top)
        {
            int left = ScaleValue(12);
            int rowHeight = ScaleValue(26);
            int timeFieldGap = ScaleValue(8);
            int groupWidth = ClientSize.Width - left * 2;
            int rowWidth = groupWidth - ScaleValue(34);
            int sharedVisitsLabelWidth = GetSharedMainSyncVisitsLabelWidth();
            int sharedAdaptiveVisitsPanelWidth = GetAdaptiveMainSyncVisitsPanelWidth();

            groupBox4.SetBounds(left, top, groupWidth, 0);
            flowLayoutPanel1.SetBounds(ScaleValue(16), ScaleValue(28), rowWidth, rowHeight);
            flowLayoutPanel2.SetBounds(ScaleValue(16), flowLayoutPanel1.Bottom + ScaleValue(8), rowWidth, rowHeight);
            flowLayoutPanel1.WrapContents = true;
            flowLayoutPanel2.WrapContents = true;
            chkBothSync.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioBlack.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            chkAutoPlay.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioWhite.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            panel1.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel2.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel3.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel4.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel1.AutoSize = false;
            panel2.AutoSize = false;
            panel3.AutoSize = false;
            panel4.AutoSize = false;
            panel1.Size = new System.Drawing.Size(lblPlayCondition.PreferredSize.Width + ScaleValue(18) + ScaleValue(68) + timeFieldGap, rowHeight);
            panel2.Size = new System.Drawing.Size(sharedAdaptiveVisitsPanelWidth, rowHeight);
            panel3.Size = new System.Drawing.Size(lblTime.PreferredSize.Width + ScaleValue(18) + ScaleValue(92), rowHeight);
            panel4.Size = new System.Drawing.Size(sharedAdaptiveVisitsPanelWidth, rowHeight);
            lblPlayCondition.AutoSize = false;
            lblTotalVisits.AutoSize = false;
            lblTime.AutoSize = false;
            lblBestMoveVisits.AutoSize = false;
            lblPlayCondition.SetBounds(0, ScaleValue(3), lblPlayCondition.PreferredSize.Width, ScaleValue(20));
            lblTotalVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(20));
            lblTime.SetBounds(0, ScaleValue(3), lblTime.PreferredSize.Width, ScaleValue(20));
            lblBestMoveVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(20));
            lblPlayCondition.TextAlign = ContentAlignment.MiddleLeft;
            lblTotalVisits.TextAlign = ContentAlignment.MiddleLeft;
            lblTime.TextAlign = ContentAlignment.MiddleLeft;
            lblBestMoveVisits.TextAlign = ContentAlignment.MiddleLeft;
            textBox1.AutoSize = false;
            textBox2.AutoSize = false;
            textBox3.AutoSize = false;
            textBox1.Margin = new Padding(timeFieldGap, 1, 0, 0);
            textBox2.Margin = new Padding(ScaleValue(8), 1, 0, 0);
            textBox3.Margin = new Padding(ScaleValue(8), 1, 0, 0);
            textBox1.Size = new System.Drawing.Size(ScaleValue(68), rowHeight);
            textBox2.Size = new System.Drawing.Size(ScaleValue(92), rowHeight);
            textBox3.Size = new System.Drawing.Size(ScaleValue(92), rowHeight);
            flowLayoutPanel1.Height = flowLayoutPanel1.GetPreferredSize(new Size(rowWidth, 0)).Height;
            flowLayoutPanel2.Top = flowLayoutPanel1.Bottom + ScaleValue(8);
            flowLayoutPanel2.Height = flowLayoutPanel2.GetPreferredSize(new Size(rowWidth, 0)).Height;
            groupBox4.Height = flowLayoutPanel2.Bottom + ScaleValue(10);
            return groupBox4.Bottom;
        }

        private void ArrangeMainActions(int top)
        {
            if (CanUseLegacyMainDesktopLayout())
            {
                ArrangeLegacyMainActions(top);
                return;
            }

            ArrangeAdaptiveMainActions(top);
        }

        private void ArrangeLegacyMainActions(int top)
        {
            int left = ScaleValue(12);
            int firstRowTop = top;
            int secondRowTop = top + ScaleValue(38);
            int buttonHeight = ScaleValue(32);
            int buttonGap = ScaleValue(12);

            btnFastSync.SetBounds(left, firstRowTop, MeasureButtonWidth(btnFastSync, 118), buttonHeight);
            btnClickBoard.SetBounds(btnFastSync.Right + buttonGap, firstRowTop, MeasureButtonWidth(btnClickBoard, 186), buttonHeight);
            btnCircleBoard.SetBounds(btnClickBoard.Right + buttonGap, firstRowTop, MeasureButtonWidth(btnCircleBoard, 104), buttonHeight);
            btnCircleRow1.SetBounds(btnCircleBoard.Right + buttonGap, firstRowTop, MeasureButtonWidth(btnCircleRow1, 104), buttonHeight);
            chkShowInBoard.AutoSize = true;
            chkShowInBoard.Location = new Point(btnCircleRow1.Right + ScaleValue(16), firstRowTop + ScaleValue(8));
            btnKeepSync.SetBounds(left, secondRowTop, MeasureButtonWidth(btnKeepSync, 128), buttonHeight);
            btnOneTimeSync.SetBounds(btnKeepSync.Right + buttonGap, secondRowTop, MeasureButtonWidth(btnOneTimeSync, 112), buttonHeight);
            btnTogglePonder.SetBounds(btnOneTimeSync.Right + buttonGap, secondRowTop, MeasureButtonWidth(btnTogglePonder, 112), buttonHeight);
            btnExchange.SetBounds(btnTogglePonder.Right + buttonGap, secondRowTop, MeasureButtonWidth(btnExchange, 104), buttonHeight);
            btnForceRebuild.SetBounds(btnExchange.Right + buttonGap, secondRowTop, MeasureButtonWidth(btnForceRebuild, 118), buttonHeight);
            btnClearBoard.SetBounds(btnForceRebuild.Right + buttonGap, secondRowTop, MeasureButtonWidth(btnClearBoard, 110), buttonHeight);
            ApplyMainFormClientHeight(Math.Max(chkShowInBoard.Bottom, btnClearBoard.Bottom) + ScaleValue(12));
        }

        private void ArrangeAdaptiveMainActions(int top)
        {
            int left = ScaleValue(12);
            int buttonHeight = ScaleValue(32);
            int buttonGap = ScaleValue(12);
            int rowGap = ScaleValue(8);
            int maxRight = ClientSize.Width - left;
            int currentX = left;
            int currentY = top;
            int rowHeight = buttonHeight;

            Button[] actionButtons = new[]
            {
                btnFastSync,
                btnClickBoard,
                btnCircleBoard,
                btnCircleRow1,
                btnKeepSync,
                btnOneTimeSync,
                btnTogglePonder,
                btnExchange,
                btnForceRebuild,
                btnClearBoard
            };
            int[] minWidths = new[] { 118, 186, 104, 104, 128, 112, 112, 104, 118, 110 };
            for (int index = 0; index < actionButtons.Length; index++)
            {
                Button button = actionButtons[index];
                int width = MeasureButtonWidth(button, minWidths[index]);
                if (currentX > left && currentX + width > maxRight)
                {
                    currentX = left;
                    currentY += rowHeight + rowGap;
                }

                button.SetBounds(currentX, currentY, width, buttonHeight);
                currentX = button.Right + buttonGap;
            }

            chkShowInBoard.AutoSize = true;
            int showInBoardWidth = GetLayoutOptionPreferredSize(chkShowInBoard).Width;
            if (currentX + showInBoardWidth > maxRight)
            {
                currentX = left;
                currentY += rowHeight + rowGap;
            }
            chkShowInBoard.Location = new Point(currentX, currentY + ScaleValue(8));
            ApplyMainFormClientHeight(chkShowInBoard.Bottom + ScaleValue(12));
        }

        private int LayoutOptionsRow(ButtonBase[] options, GroupBox groupBox, int startX, int startY, int itemGap, int rowGap)
        {
            int currentX = startX;
            int currentY = startY;
            int availableRight = groupBox.Width - startX;
            int rowHeight = 0;
            foreach (ButtonBase option in options)
            {
                Size preferredSize = GetLayoutOptionPreferredSize(option);
                if (currentX > startX && currentX + preferredSize.Width > availableRight)
                {
                    currentX = startX;
                    currentY += rowHeight + rowGap;
                    rowHeight = 0;
                }

                option.Location = new Point(currentX, currentY);
                currentX += preferredSize.Width + itemGap;
                rowHeight = Math.Max(rowHeight, preferredSize.Height);
            }

            return currentY + rowHeight;
        }

        private int MeasureOptionsWidth(ButtonBase[] options, int itemGap)
        {
            int width = 0;
            foreach (ButtonBase option in options)
                width += GetLayoutOptionPreferredSize(option).Width;
            return width + itemGap * Math.Max(0, options.Length - 1);
        }

        private Size GetLayoutOptionPreferredSize(ButtonBase option)
        {
            Size standardSize = MeasureLayoutOptionPreferredSize(option, FlatStyle.Standard);
            Size flatSize = MeasureLayoutOptionPreferredSize(option, FlatStyle.Flat);
            return new Size(
                Math.Max(standardSize.Width, flatSize.Width),
                Math.Max(standardSize.Height, flatSize.Height));
        }

        private static Size MeasureLayoutOptionPreferredSize(ButtonBase option, FlatStyle flatStyle)
        {
            if (option is RadioButton radioButton)
            {
                using (RadioButton probe = new RadioButton())
                {
                    probe.AutoSize = true;
                    probe.Text = radioButton.Text;
                    probe.Font = radioButton.Font;
                    probe.FlatStyle = flatStyle;
                    return probe.PreferredSize;
                }
            }

            if (option is CheckBox checkBox)
            {
                using (CheckBox probe = new CheckBox())
                {
                    probe.AutoSize = true;
                    probe.Text = checkBox.Text;
                    probe.Font = checkBox.Font;
                    probe.FlatStyle = flatStyle;
                    return probe.PreferredSize;
                }
            }

            throw new NotSupportedException($"Unsupported layout option type: {option.GetType().FullName}");
        }

        private int MeasureButtonWidth(Button button, int minimumLogicalWidth)
        {
            int minimumWidth = ScaleValue(minimumLogicalWidth);
            return Math.Max(minimumWidth, TextRenderer.MeasureText(button.Text, button.Font).Width + ScaleValue(28));
        }

        private int GetSharedMainSyncVisitsLabelWidth()
        {
            return Math.Max(lblTotalVisits.PreferredSize.Width, lblBestMoveVisits.PreferredSize.Width);
        }

        private int GetLegacyMainSyncVisitsPanelWidth()
        {
            return Math.Max(ScaleValue(112), GetSharedMainSyncVisitsLabelWidth() + ScaleValue(11));
        }

        private int GetAdaptiveMainSyncVisitsPanelWidth()
        {
            return GetSharedMainSyncVisitsLabelWidth() + ScaleValue(18) + ScaleValue(92);
        }

        private bool CanUseLegacyMainDesktopLayout()
        {
            return ClientSize.Width >= Math.Max(
                Math.Max(GetLegacyMainHeaderRequiredWidth(), GetLegacyMainSyncRequiredWidth()),
                Math.Max(GetLegacyMainBoardRequiredWidth(), GetLegacyMainActionsRequiredWidth()));
        }

        private int GetLegacyMainHeaderRequiredWidth()
        {
            int left = ScaleValue(12);
            int optionLeft = ScaleValue(14);
            int optionGap = ScaleValue(10);
            int utilityGap = ScaleValue(8);
            int buttonGap = ScaleValue(8);
            int settingsWidth = MeasureButtonWidth(btnSettings, 72);
            int helpWidth = MeasureButtonWidth(btnHelp, 68);
            int themeWidth = MeasureButtonWidth(btnTheme, 68);
            int utilityColumnWidth = Math.Max(
                settingsWidth + helpWidth + themeWidth + buttonGap * 2,
                Math.Max(MeasureButtonWidth(btnKomi65, 170), MeasureButtonWidth(btnCheckUpdate, 170)));
            int platformWidth = optionLeft + MeasureOptionsWidth(
                new ButtonBase[] { rdoFox, rdoFoxBack, rdoTygem, rdoSina, rdoBack, rdoFore },
                optionGap) + ScaleValue(20);
            return left * 2 + platformWidth + utilityGap + utilityColumnWidth;
        }

        private int GetLegacyMainBoardRequiredWidth()
        {
            int left = ScaleValue(12);
            int sectionPadding = ScaleValue(16);
            int optionGap = ScaleValue(8);
            int textBoxWidth = ScaleValue(34);
            int customInputGap = ScaleValue(12);
            int separatorGap = ScaleValue(4);
            int labelWidth = Math.Max(lblBoardSize.PreferredSize.Width, ScaleValue(52));
            int contentWidth =
                sectionPadding
                + labelWidth
                + ScaleValue(6)
                + GetLayoutOptionPreferredSize(rdo19x19).Width
                + optionGap
                + GetLayoutOptionPreferredSize(rdo13x13).Width
                + optionGap
                + GetLayoutOptionPreferredSize(rdo9x9).Width
                + optionGap
                + ScaleValue(4)
                + GetLayoutOptionPreferredSize(rdoOtherBoard).Width
                + customInputGap
                + textBoxWidth
                + separatorGap
                + ScaleValue(10)
                + separatorGap
                + textBoxWidth
                + sectionPadding;
            return left * 2 + contentWidth;
        }

        private int GetLegacyMainSyncRequiredWidth()
        {
            int left = ScaleValue(12);
            int timeFieldGap = ScaleValue(8);
            int buttonGap = ScaleValue(12);
            int sharedVisitsPanelWidth = GetLegacyMainSyncVisitsPanelWidth();
            int row1Width =
                GetLayoutOptionPreferredSize(chkBothSync).Width
                + buttonGap
                + GetLayoutOptionPreferredSize(radioBlack).Width
                + buttonGap
                + Math.Max(ScaleValue(129) + timeFieldGap, lblPlayCondition.PreferredSize.Width + ScaleValue(22) + timeFieldGap)
                + buttonGap
                + sharedVisitsPanelWidth
                + ScaleValue(8)
                + ScaleValue(92);
            int row2Width =
                GetLayoutOptionPreferredSize(chkAutoPlay).Width
                + buttonGap
                + GetLayoutOptionPreferredSize(radioWhite).Width
                + buttonGap
                + Math.Max(ScaleValue(61), lblTime.PreferredSize.Width + ScaleValue(8))
                + timeFieldGap
                + ScaleValue(68)
                + buttonGap
                + sharedVisitsPanelWidth
                + ScaleValue(8)
                + ScaleValue(92);
            return left * 2 + ScaleValue(34) + Math.Max(row1Width, row2Width);
        }

        private int GetLegacyMainActionsRequiredWidth()
        {
            int left = ScaleValue(12);
            int buttonGap = ScaleValue(12);
            int firstRowWidth =
                MeasureButtonWidth(btnFastSync, 118)
                + buttonGap
                + MeasureButtonWidth(btnClickBoard, 186)
                + buttonGap
                + MeasureButtonWidth(btnCircleBoard, 104)
                + buttonGap
                + MeasureButtonWidth(btnCircleRow1, 104)
                + ScaleValue(16)
                + GetLayoutOptionPreferredSize(chkShowInBoard).Width;
            int secondRowWidth =
                MeasureButtonWidth(btnKeepSync, 128)
                + buttonGap
                + MeasureButtonWidth(btnOneTimeSync, 112)
                + buttonGap
                + MeasureButtonWidth(btnTogglePonder, 112)
                + buttonGap
                + MeasureButtonWidth(btnExchange, 104)
                + buttonGap
                + MeasureButtonWidth(btnForceRebuild, 118)
                + buttonGap
                + MeasureButtonWidth(btnClearBoard, 110);
            return left * 2 + Math.Max(firstRowWidth, secondRowWidth);
        }

        private void ConstrainMainFormWidth()
        {
            Rectangle workingArea = GetCurrentWorkingArea();
            int maxWidth = Math.Max(ScaleValue(300), workingArea.Width - ScaleValue(MainFormScreenLogicalPadding));
            int minimumWidth = Math.Min(ScaleValue(MainFormMinimumLogicalWidth), maxWidth);
            int maxHeight = GetMaxMainFormClientHeight();
            int targetWidth = isMainFormSizeInitialized
                ? Math.Min(Math.Max(ClientSize.Width, minimumWidth), maxWidth)
                : Math.Min(ScaleSize(MainFormDefaultSize).Width, maxWidth);
            int targetHeight = isMainFormSizeInitialized
                ? Math.Min(ClientSize.Height, maxHeight)
                : Math.Min(ScaleSize(MainFormDefaultSize).Height, maxHeight);

            ClientSize = new Size(targetWidth, targetHeight);
        }

        private int GetMaxMainFormClientHeight()
        {
            Rectangle workingArea = GetCurrentWorkingArea();
            return Math.Max(ScaleValue(280), workingArea.Height - ScaleValue(MainFormScreenLogicalPadding));
        }

        private void ApplyMainFormClientHeight(int desiredHeight)
        {
            int maxHeight = GetMaxMainFormClientHeight();
            int constrainedHeight = Math.Min(desiredHeight, maxHeight);
            AutoScrollMinSize = desiredHeight > constrainedHeight
                ? new Size(0, desiredHeight)
                : Size.Empty;
            ClientSize = new Size(ClientSize.Width, constrainedHeight);
        }

        private Rectangle GetCurrentWorkingArea()
        {
            return DisplayScaling.GetScreenWorkingAreaFromPoint(ResolveLayoutReferencePoint());
        }

        private int ScaleValue(int logicalValue)
        {
            return (int)Math.Round(logicalValue * GetCurrentDpiScale());
        }

        private Size ScaleSize(Size logicalSize)
        {
            return new Size(ScaleValue(logicalSize.Width), ScaleValue(logicalSize.Height));
        }

        private float GetCurrentDpiScale()
        {
            try
            {
                Point? startupReferencePoint = TryGetStartupReferencePoint();
                if (startupReferencePoint.HasValue)
                    return (float)DisplayScaling.NormalizeScale(DisplayScaling.GetScaleForPoint(startupReferencePoint.Value));
                if (IsHandleCreated)
                    return (float)DisplayScaling.NormalizeScale(DisplayScaling.GetScaleForWindow(Handle));
                if (factor > 0f)
                    return factor;
                return DeviceDpi > 0 ? DeviceDpi / 96f : 1f;
            }
            catch
            {
                return factor > 0f ? factor : 1f;
            }
        }

        private void setNativeBoardMode(int syncType)
        {
            SetCurrentSyncType(syncType);
            ApplySyncModeControlState();
        }

        private void setManualSelectionMode(int syncType)
        {
            SetCurrentSyncType(syncType);
            ApplySyncModeControlState();
        }

        private void ApplySyncModeControlState()
        {
            bool manualSelectionMode = UsesManualSelectionType(CurrentSyncType);
            btnCircleBoard.Enabled = manualSelectionMode;
            btnCircleRow1.Enabled = manualSelectionMode;
            btnClickBoard.Enabled = !manualSelectionMode;
            btnFastSync.Enabled = SupportsFastSyncType(CurrentSyncType);
            if (!manualSelectionMode && rdoOtherBoard.Checked)
                rdo19x19.Checked = true;
            rdoOtherBoard.Enabled = manualSelectionMode;
            ApplyShowInBoardControlState();
            ResetMainWindowTitle();
        }

        private void ApplyShowInBoardControlState()
        {
            bool supportsShowInBoard = SupportsShowInBoard();
            chkShowInBoard.Enabled = supportsShowInBoard;
            if (!supportsShowInBoard && chkShowInBoard.Checked)
                chkShowInBoard.Checked = false;
        }

        private void SetSyncConfigurationControlsEnabled(bool enabled)
        {
            rdoFox.Enabled = enabled;
            rdoFoxBack.Enabled = enabled;
            rdoTygem.Enabled = enabled;
            rdoBack.Enabled = enabled;
            rdoSina.Enabled = enabled;
            rdo19x19.Enabled = enabled;
            rdo13x13.Enabled = enabled;
            rdo9x9.Enabled = enabled;
            rdoOtherBoard.Enabled = enabled;
            rdoFore.Enabled = enabled;
        }

        private void DisableBoardSelectionControls()
        {
            btnCircleRow1.Enabled = false;
            btnCircleBoard.Enabled = false;
            btnClickBoard.Enabled = false;
            btnOneTimeSync.Enabled = false;
        }

        private void RestoreBoardSelectionControls()
        {
            ApplySyncModeControlState();
            btnOneTimeSync.Enabled = true;
        }

        private void SetSyncBoth(bool enabled)
        {
            sessionCoordinator.SetSyncBoth(enabled);
        }


        public void SendError(String strMsg)
        {
            sessionCoordinator.SendError(strMsg);
        }

        private string GetProtocolNumericValue(TextBox textBox)
        {
            return string.IsNullOrWhiteSpace(textBox.Text) ? "0" : textBox.Text;
        }

        private void SendPlayCommandIfSelected()
        {
            if (!sessionCoordinator.SyncBoth)
                return;
            if (radioBlack.Checked)
            {
                sessionCoordinator.SendPlay(
                    "black",
                    GetProtocolNumericValue(textBox1),
                    GetProtocolNumericValue(textBox2),
                    GetProtocolNumericValue(textBox3));
                return;
            }
            if (radioWhite.Checked)
            {
                sessionCoordinator.SendPlay(
                    "white",
                    GetProtocolNumericValue(textBox1),
                    GetProtocolNumericValue(textBox2),
                    GetProtocolNumericValue(textBox3));
            }
        }

        private void SendPonderStatusCommand()
        {
            sessionCoordinator.SendPonderStatus(Program.playPonder);
        }

        private void SendVersionCommand()
        {
            sessionCoordinator.SendVersion(Program.version);
        }

        private void SendSyncCommand()
        {
            sessionCoordinator.SendSync();
        }

        private void SendStopSyncCommand()
        {
            sessionCoordinator.SendStopSync();
        }

        private void SendBothSyncCommand(bool enabled)
        {
            sessionCoordinator.SendBothSync(enabled);
        }

        private bool CanUseForegroundFoxInBoardProtocol()
        {
            return CurrentSyncType == TYPE_FOX && !canUseLW;
        }

        private void SendForegroundFoxInBoardCommand(bool enabled)
        {
            sessionCoordinator.SendForegroundFoxInBoard(enabled);
        }

        private void SendBothSyncStateChange()
        {
            SendBothSyncCommand(sessionCoordinator.SyncBoth);
            if (Program.showInBoard && CanUseForegroundFoxInBoardProtocol())
                SendForegroundFoxInBoardCommand(sessionCoordinator.SyncBoth);
        }

        private void ResendSyncSessionState()
        {
            if (!sessionCoordinator.KeepSync)
                return;
            SendSyncCommand();
            SendPlayCommandIfSelected();
        }

        private void SendClearCommand()
        {
            sessionCoordinator.SendClear();
        }

        private void SendNoInBoardCommand()
        {
            sessionCoordinator.SendNoInBoard();
        }

        private void SendNotInBoardCommand()
        {
            sessionCoordinator.SendNotInBoard();
        }

        private void SendPlacementResultCommand(bool success)
        {
            sessionCoordinator.SendPlacementResult(success);
        }

        private void SendTimeChangedCommand()
        {
            sessionCoordinator.SendTimeChanged(GetProtocolNumericValue(textBox1));
        }

        private void SendPlayoutsChangedCommand()
        {
            sessionCoordinator.SendPlayoutsChanged(GetProtocolNumericValue(textBox2));
        }

        private void SendFirstPolicyChangedCommand()
        {
            sessionCoordinator.SendFirstPolicyChanged(GetProtocolNumericValue(textBox3));
        }

        private void SendNoPonderCommand()
        {
            sessionCoordinator.SendNoPonder();
        }

        private void SendStopAutoPlayCommand()
        {
            sessionCoordinator.SendStopAutoPlay();
        }

        private void SendPassCommand()
        {
            sessionCoordinator.SendPass();
        }

        private void SendShutdownProtocol()
        {
            sessionCoordinator.SendShutdownProtocol();
        }

        private void ReleasePlacementBinding(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;
            LwInterop.lwsoft lwh = new LwInterop.lwsoft();
            lwh.ForceUnBindWindow((int)handle);
        }

        private void NormalizeNumericTextBox(TextBox textBox)
        {
            var reg = new Regex("^[0-9]*$");
            string str = textBox.Text.Trim();
            var sb = new StringBuilder();
            if (reg.IsMatch(str))
                return;
            for (int i = 0; i < str.Length; i++)
            {
                if (reg.IsMatch(str[i].ToString()))
                    sb.Append(str[i].ToString());
            }
            textBox.Text = sb.ToString();
            textBox.SelectionStart = textBox.Text.Length;
        }

        private SyncMode GetCurrentSyncMode()
        {
            switch (CurrentSyncType)
            {
                case TYPE_TYGEM:
                    return SyncMode.Tygem;
                case TYPE_SINA:
                    return SyncMode.Sina;
                case TYPE_BACKGROUND:
                    return SyncMode.Background;
                case TYPE_FOX_BACKGROUND_PLACE:
                    return SyncMode.FoxBackgroundPlace;
                case TYPE_FOREGROUND:
                    return SyncMode.Foreground;
                default:
                    return SyncMode.Fox;
            }
        }

        private BoardDimensions CreateCurrentBoardSize()
        {
            return new BoardDimensions(boardW, boardH);
        }

        private bool HasManualSelection()
        {
            return ox2 > selectionX1 && oy2 > selectionY1;
        }

        private PixelRect BuildCaptureSelectionBounds()
        {
            if (!HasManualSelection())
                return null;

            return new PixelRect(selectionX1, selectionY1, ox2 - selectionX1, oy2 - selectionY1);
        }

        private string GetSelectedPlayColor()
        {
            if (radioBlack.Checked)
                return "black";
            if (radioWhite.Checked)
                return "white";
            return null;
        }

        private void InvokeHostAction(Action action)
        {
            if (action == null)
                throw new ArgumentNullException("action");
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }
            action();
        }

        private bool TryDispatchProtocolCommand(Action command)
        {
            if (command == null)
                throw new ArgumentNullException("command");
            if (isShuttingDown || IsDisposed || Disposing)
                return true;
            if (!IsHandleCreated)
                return false;
            if (InvokeRequired)
            {
                BeginInvoke(command);
                return true;
            }
            command();
            return true;
        }

        private void EnqueuePendingProtocolCommand(Action command)
        {
            bool shouldFlush = false;

            lock (protocolCommandSyncRoot)
            {
                if (isShuttingDown || IsDisposed || Disposing)
                    return;

                pendingProtocolCommands.Enqueue(command);
                shouldFlush = IsHandleCreated;
            }

            if (shouldFlush)
                FlushPendingProtocolCommands();
        }

        private void FlushPendingProtocolCommands()
        {
            Action[] pendingCommands;

            if (!TryTakePendingProtocolCommands(out pendingCommands))
                return;

            for (int i = 0; i < pendingCommands.Length; i++)
                TryDispatchProtocolCommand(pendingCommands[i]);
        }

        internal void DrainStartupProtocolCommands()
        {
            Action[] pendingCommands;

            while (TryTakePendingProtocolCommands(out pendingCommands))
            {
                for (int i = 0; i < pendingCommands.Length; i++)
                {
                    if (isShuttingDown)
                        return;
                    pendingCommands[i]();
                }
            }
        }

        private bool TryTakePendingProtocolCommands(out Action[] pendingCommands)
        {
            lock (protocolCommandSyncRoot)
            {
                if (isShuttingDown || pendingProtocolCommands.Count == 0)
                {
                    pendingCommands = null;
                    return false;
                }

                pendingCommands = pendingProtocolCommands.ToArray();
                pendingProtocolCommands.Clear();
                return true;
            }
        }

        private void ClearPendingProtocolCommands()
        {
            lock (protocolCommandSyncRoot)
                pendingProtocolCommands.Clear();
        }

        private void InvokeUiHostAction(Action action)
        {
            if (action == null)
                throw new ArgumentNullException("action");
            if (isShuttingDown || IsDisposed || Disposing || !IsHandleCreated)
                return;
            if (InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }
            action();
        }

        SyncCoordinatorHostSnapshot ISyncCoordinatorHost.CaptureSnapshot()
        {
            return uiThreadInvoker.ExecuteOrCancel(
                CaptureSnapshotCore,
                IsSnapshotCaptureCancelled);
        }

        private SyncCoordinatorHostSnapshot CaptureSnapshotCore()
        {
            string syncPlatform = ResolveSyncPlatform();
            FoxWindowContext foxWindowContext = ResolveFoxWindowContext();
            int? foxMoveNumber = foxWindowContext.ResolveDisplayedMoveNumber();
            UpdateMainWindowTitle(foxWindowContext);

            SyncCoordinatorHostSnapshot snapshot = new SyncCoordinatorHostSnapshot
            {
                SyncMode = GetCurrentSyncMode(),
                BoardWidth = boardW,
                BoardHeight = boardH,
                SelectionBounds = BuildCaptureSelectionBounds(),
                SelectedWindowHandle = hwnd,
                DpiScale = factor,
                LegacyTypeToken = CurrentSyncType.ToString(),
                ShowInBoard = Program.showInBoard,
                SupportsForegroundFoxInBoardProtocol = CanUseForegroundFoxInBoardProtocol(),
                CanUseLightweightInterop = CurrentSyncType == TYPE_FOX && canUseLW,
                AutoMinimize = Program.autoMin,
                SampleIntervalMs = Program.timeinterval,
                UseEnhancedCapture = Program.useEnhanceScreen,
                FoxMoveNumber = foxMoveNumber,
                PlayColor = GetSelectedPlayColor(),
                AiTimeValue = GetProtocolNumericValue(textBox1),
                PlayoutsValue = GetProtocolNumericValue(textBox2),
                FirstPolicyValue = GetProtocolNumericValue(textBox3)
            };

            sessionCoordinator.SetSyncPlatform(syncPlatform);
            sessionCoordinator.SetFoxWindowContext(foxWindowContext);
            UpdateCapturedFoxMoveNumber(snapshot.FoxMoveNumber);
            return snapshot;
        }

        private string ResolveSyncPlatform()
        {
            return IsFoxSyncType(CurrentSyncType) ? "fox" : "generic";
        }

        private FoxWindowContext ResolveFoxWindowContext()
        {
            if (!IsFoxSyncType(CurrentSyncType) || hwnd == IntPtr.Zero)
            {
                InvalidateFoxWindowBinding();
                return FoxWindowContext.Unknown();
            }

            FoxWindowContext foxWindowContext;
            if (TryRefreshFoxWindowContextFromBinding(out foxWindowContext))
                return foxWindowContext;
            if (TryResolveFoxWindowBinding(out foxWindowContext))
                return foxWindowContext;
            return FoxWindowContext.Unknown();
        }

        private bool TryRefreshFoxWindowContextFromBinding(out FoxWindowContext foxWindowContext)
        {
            if (FoxWindowTitleReader.TryRead(foxWindowBinding, hwnd, GetParent, out foxWindowContext))
                return true;

            InvalidateFoxWindowBinding();
            foxWindowContext = FoxWindowContext.Unknown();
            return false;
        }

        private bool TryResolveFoxWindowBinding(out FoxWindowContext foxWindowContext)
        {
            FoxWindowBinding binding;
            if (!FoxWindowBindingResolver.TryResolve(
                hwnd,
                FoxWindowTitleReader.ReadWindowTitle,
                GetParent,
                out binding,
                out foxWindowContext))
            {
                InvalidateFoxWindowBinding();
                foxWindowContext = FoxWindowContext.Unknown();
                return false;
            }

            foxWindowBinding = binding;
            return true;
        }

        private void InvalidateFoxWindowBinding()
        {
            foxWindowBinding = null;
        }

        private void UpdateMainWindowTitle(FoxWindowContext foxWindowContext)
        {
            lastFoxWindowContext = FoxWindowContext.CopyOf(foxWindowContext);
            ApplyMainWindowTitle();
        }

        private void RefreshMainWindowTitleFromCurrentWindow()
        {
            UpdateMainWindowTitle(ResolveFoxWindowContext());
        }

        private void ResetMainWindowTitle()
        {
            hasRetainedFoxTitleSnapshot = false;
            lastFoxWindowContext = FoxWindowContext.Unknown();
            InvalidateFoxWindowBinding();
            ApplyMainWindowTitle();
        }

        private MainWindowTitleDisplayMode ResolveMainWindowTitleDisplayMode()
        {
            if (isShuttingDown || !IsFoxSyncType(CurrentSyncType))
                return MainWindowTitleDisplayMode.Hidden;
            if (HasActiveSyncOperation())
                return MainWindowTitleDisplayMode.Syncing;
            if (hasRetainedFoxTitleSnapshot)
                return MainWindowTitleDisplayMode.RetainedSnapshot;
            return MainWindowTitleDisplayMode.Hidden;
        }

        private void ApplyMainWindowTitle()
        {
            string title = MainWindowTitleFormatter.Format(
                getLangStr("MainForm_title"),
                ResolveMainWindowTitleDisplayMode(),
                hwnd != IntPtr.Zero,
                lastFoxWindowContext,
                getLangStr("MainForm_titleTagFox"),
                getLangStr("MainForm_titleTagRoom"),
                getLangStr("MainForm_titleTagRecord"),
                getLangStr("MainForm_titleTagSyncing"),
                getLangStr("MainForm_titleTagTitleMissing"),
                getLangStr("MainForm_titleTagRecordEnd"),
                getLangStr("MainForm_titleMoveFormatSingle"),
                getLangStr("MainForm_titleMoveFormatRecord"));
            if (string.Equals(lastAppliedMainWindowTitle, title, StringComparison.Ordinal))
                return;
            this.Text = title;
            lastAppliedMainWindowTitle = title;
        }

        private void UpdateCapturedFoxMoveNumber(int? foxMoveNumber)
        {
            sessionCoordinator.SetCapturedFoxMoveNumber(foxMoveNumber);
        }

        private bool IsSnapshotCaptureCancelled()
        {
            return isShuttingDown || !HasActiveSyncOperation();
        }

        private bool HasActiveSyncOperation()
        {
            return sessionCoordinator.StartedSync || sessionCoordinator.IsContinuousSyncing;
        }

        void ISyncCoordinatorHost.UpdateSelectedWindowHandle(IntPtr handle)
        {
            InvokeHostAction(delegate
            {
                hwnd = handle;
                hasRetainedFoxTitleSnapshot = false;
                lastFoxWindowContext = FoxWindowContext.Unknown();
                InvalidateFoxWindowBinding();
                if (HasActiveSyncOperation())
                {
                    RefreshMainWindowTitleFromCurrentWindow();
                    return;
                }
                ApplyMainWindowTitle();
            });
        }

        void ISyncCoordinatorHost.OnKeepSyncStarted()
        {
            InvokeUiHostAction(ApplyKeepSyncStartedUi);
        }

        void ISyncCoordinatorHost.OnKeepSyncStopped(bool continuousSyncActive)
        {
            InvokeUiHostAction(delegate
            {
                ApplyKeepSyncStoppedUi(continuousSyncActive);
            });
        }

        void ISyncCoordinatorHost.OnContinuousSyncStarted()
        {
            InvokeUiHostAction(ApplyContinuousSyncStartedUi);
        }

        void ISyncCoordinatorHost.OnContinuousSyncStopped()
        {
            InvokeUiHostAction(ApplyContinuousSyncStoppedUi);
        }

        void ISyncCoordinatorHost.ShowMissingSyncSourceMessage()
        {
            InvokeUiHostAction(delegate
            {
                MessageBox.Show(getLangStr("noSelectedBoardAndFailed"));
            });
        }

        void ISyncCoordinatorHost.ShowRecognitionFailureMessage()
        {
            InvokeUiHostAction(delegate
            {
                MessageBox.Show(getLangStr("recgnizeFaild"));
            });
        }

        void ISyncCoordinatorHost.MinimizeWindow()
        {
            InvokeUiHostAction(delegate
            {
                if (WindowState != FormWindowState.Minimized)
                    WindowState = FormWindowState.Minimized;
            });
        }

        void ISyncCoordinatorHost.ReleasePlacementBinding(IntPtr handle)
        {
            InvokeHostAction(delegate
            {
                ReleasePlacementBinding(handle);
            });
        }

        bool ISyncCoordinatorHost.TrySendPlaceProtocolError(string message)
        {
            return TrySendPlaceProtocolError(message);
        }

        private void ApplyKeepSyncStartedUi()
        {
            btnKeepSync.Text = getLangStr("stopSync");
            btnFastSync.Text = getLangStr("stopSync");
            SetSyncConfigurationControlsEnabled(false);
            DisableBoardSelectionControls();
            hasRetainedFoxTitleSnapshot = false;
            RefreshMainWindowTitleFromCurrentWindow();
        }

        private void ApplyKeepSyncStoppedUi(bool continuousSyncActive)
        {
            btnKeepSync.Text = getLangStr("keepSync") + "(" + Program.timename + "ms)";
            if (!SyncToolbarTextResolver.ShouldRestoreIdleUiAfterKeepSyncStop(continuousSyncActive))
            {
                ApplyMainWindowTitle();
                return;
            }
            btnFastSync.Text = getLangStr("fastSync");
            btnKeepSync.Enabled = true;
            SetSyncConfigurationControlsEnabled(true);
            RestoreBoardSelectionControls();
            ResetMainWindowTitle();
        }

        private void ApplyContinuousSyncStartedUi()
        {
            btnFastSync.Text = getLangStr("stopSync");
            btnKeepSync.Enabled = false;
            SetSyncConfigurationControlsEnabled(false);
            DisableBoardSelectionControls();
            hasRetainedFoxTitleSnapshot = false;
            RefreshMainWindowTitleFromCurrentWindow();
        }

        private void ApplyContinuousSyncStoppedUi()
        {
            bool keepSyncActive = sessionCoordinator.StartedSync;
            btnFastSync.Text = SyncToolbarTextResolver.ResolveFastSyncTextAfterContinuousStop(
                keepSyncActive,
                getLangStr("stopSync"),
                getLangStr("fastSync"));

            if (keepSyncActive)
            {
                ApplyMainWindowTitle();
                return;
            }
            ApplyKeepSyncStoppedUi(false);
        }

        private Boolean isCtrlDown = false;

        private void HookListener_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            // Console.Out.WriteLine(e.KeyValue);
            if (e.KeyValue == 162 || e.KeyValue == 163)
                isCtrlDown = true;
            if (isCtrlDown && e.KeyValue == 88 && SupportsShowInBoard() && !Program.disableShowInBoardShortcut)
                chkShowInBoard.Checked = !chkShowInBoard.Checked;
        }

        private void HookListener_KeyUp(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyValue == 162 || e.KeyValue == 163)
                isCtrlDown = false;
        }

        internal bool IsShutdownRequested
        {
            get { return isShuttingDown; }
        }

        internal MainForm(
            LaunchOptions launchOptions,
            ISyncSessionCoordinator sessionCoordinator,
            ILegacySelectionCalibrationService selectionCalibrationService)
        {
            if (launchOptions == null)
                throw new ArgumentNullException("launchOptions");
            if (sessionCoordinator == null)
                throw new ArgumentNullException("sessionCoordinator");
            if (selectionCalibrationService == null)
                throw new ArgumentNullException("selectionCalibrationService");

            this.launchOptions = launchOptions;
            this.sessionCoordinator = sessionCoordinator;
            this.selectionCalibrationService = selectionCalibrationService;
            this.uiThreadInvoker = new UiThreadInvoker(this);
            this.placeRequestQueue = new SerialBackgroundWorkQueue("ReadboardPlaceRequestQueue");
            InitializeComponent();
            GlobalHooker hooker = new GlobalHooker();
            hookListener = new KeyboardHookListener(hooker);
            hookListener.KeyDown += HookListener_KeyDown;
            hookListener.KeyUp += HookListener_KeyUp;
            hookListener.Start();
            using (System.Drawing.Bitmap bitmap = new Bitmap(1, 1))
            using (System.Drawing.Graphics graphics2 = Graphics.FromImage(bitmap))
            {
                factor = graphics2.DpiX / 96;
            }
            if (factor > 1.0f)
            {
                Program.isScaled = true;
                Program.factor = factor;
            }
            ApplyLoadedConfiguration();
            this.MaximizeBox = false;
            if (!launchOptions.AiTime.Equals(" "))
                textBox1.Text = launchOptions.AiTime;
            if (!launchOptions.Playouts.Equals(" "))
                textBox2.Text = launchOptions.Playouts;
            if (!launchOptions.FirstPolicy.Equals(" "))
                textBox3.Text = launchOptions.FirstPolicy;
            try
            {
                //  int s = DllRegisterServer();
                //  if (s >= 0)
                //  {
                //注册成功!             
                try
                {
                    lw = new LwInterop.lwsoft();
                    canUseLW = false;// true;
                }
                catch (Exception)
                {
                    canUseLW = false;
                }
                //  }
                //  else
                //  {
                //注册失败}
                //      canUseLW = false;
                //  }
            }
            catch (Exception)
            {
                canUseLW = false;
            }
            radioWhite.Enabled = false;
            radioBlack.Enabled = false;
            textBox1.Enabled = false;
            textBox2.Enabled = false;
            textBox3.Enabled = false;
            if (sessionCoordinator.SyncBoth)
            {
                chkBothSync.Checked = true;
                chkAutoPlay.Enabled = true;
            }
            else
            {
                chkBothSync.Checked = false;
                chkAutoPlay.Enabled = false;
            }
            this.rdoFox.Text = getLangStr("MainForm_rdoFox");
            this.rdoFoxBack.Text = getLangStr("MainForm_rdoFoxBack");
            this.rdoTygem.Text = getLangStr("MainForm_rdoTygem");
            this.rdoSina.Text = getLangStr("MainForm_rdoSina");
            this.rdoBack.Text = getLangStr("MainForm_rdoBack");
            this.rdoFore.Text = getLangStr("MainForm_rdoFore");
            this.btnSettings.Text = getLangStr("MainForm_btnSettings");
            this.btnHelp.Text = getLangStr("MainForm_btnHelp");
            this.btnCheckUpdate.Text = getLangStr("MainForm_btnCheckUpdate");
            this.btnFastSync.Text = getLangStr("MainForm_btnFastSync");
            this.lblBoardSize.Text = getLangStr("MainForm_lblBoardSize");
            this.btnKomi65.Text = getLangStr("MainForm_btnKomi65");
            this.chkBothSync.Text = getLangStr("MainForm_chkBothSync");
            this.chkAutoPlay.Text = getLangStr("MainForm_chkAutoPlay");
            this.radioBlack.Text = getLangStr("MainForm_radioBlack");
            this.radioWhite.Text = getLangStr("MainForm_radioWhite");
            this.lblPlayCondition.Text = getLangStr("MainForm_lblPlayCondition");
            this.lblTime.Text = getLangStr("MainForm_lblTime");
            this.lblTotalVisits.Text = getLangStr("MainForm_lblTotalVisits");
            this.lblBestMoveVisits.Text=getLangStr("MainForm_lblBestMoveVisits");
            this.btnClickBoard.Text = getLangStr("MainForm_btnClickBoard");
            this.btnCircleBoard.Text = getLangStr("MainForm_btnCircleBoard");
            this.btnCircleRow1.Text = getLangStr("MainForm_btnCircleRow1");
            this.btnTogglePonder.Text = getLangStr("MainForm_btnTogglePonder");
            this.chkShowInBoard.Text = getLangStr("MainForm_chkShowInBoard");
            this.btnKeepSync.Text = getLangStr("MainForm_btnKeepSync");
            this.btnOneTimeSync.Text = getLangStr("MainForm_btnOneTimeSync");
            this.btnExchange.Text = getLangStr("MainForm_btnExchange");
            this.btnForceRebuild.Text = getLangStr("MainForm_btnForceRebuild");
            this.btnClearBoard.Text = getLangStr("MainForm_btnClearBoard");
            ResetMainWindowTitle();
            ApplyMainFormUi();
            RefreshShowInBoardShortcutToolTip();
            isInitializingProtocolState = false;
        }

        private String getLangStr(String itemName)
        {
            String result  = "";
            try {
                result = Program.langItems[itemName].ToString();
            }
            catch (Exception e)
            {
                SendError(e.ToString());              
            }
            return result;
        }

        private void SetCheckUpdateButtonBusy(Boolean isChecking)
        {
            this.btnCheckUpdate.Enabled = !isChecking;
            this.btnCheckUpdate.Text = isChecking
                ? getLangStr("MainForm_btnCheckUpdate_Checking")
                : getLangStr("MainForm_btnCheckUpdate");
        }

        private void ApplyUpdateDialogLanguage(FormUpdate formUpdate)
        {
            formUpdate.Text = getLangStr("Update_dialogTitle");
            SetControlText(formUpdate, "lblTitle", getLangStr("Update_dialogTitle"));
            SetControlText(formUpdate, "lblCurrentVersion", getLangStr("Update_currentVersion"));
            SetControlText(formUpdate, "lblLatestVersion", getLangStr("Update_latestVersion"));
            SetControlText(formUpdate, "lblReleaseDate", getLangStr("Update_releaseDate"));
            SetControlText(formUpdate, "lblReleaseNotes", getLangStr("Update_releaseNotes"));
            SetControlText(formUpdate, "btnDownload", getLangStr("Update_download"));
            SetControlText(formUpdate, "btnClose", getLangStr("Update_close"));
        }

        private void SetControlText(Control root, string controlName, string text)
        {
            Control control = FindControl(root, controlName);
            if (control == null)
                return;
            control.Text = text;
        }

        private static Control FindControl(Control root, string controlName)
        {
            if (root == null || string.IsNullOrEmpty(controlName))
                return null;

            if (root.Name == controlName)
                return root;
            foreach (Control child in root.Controls)
            {
                Control result = FindControl(child, controlName);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void ShowUpdateAvailable(UpdateCheckResult result)
        {
            UpdateDialogModel model = new UpdateDialogModel
            {
                CurrentVersion = result.CurrentVersion,
                LatestVersion = result.LatestVersion,
                PublishedAt = result.PublishedAt,
                ReleaseNotes = result.ReleaseNotes,
                DownloadUrl = result.ReleaseUrl,
                UnavailableText = getLangStr("Update_notProvided"),
                EmptyReleaseNotesText = getLangStr("Update_releaseNotesUnavailable"),
                MissingDownloadUrlMessage = getLangStr("Update_missingDownloadUrl"),
                InvalidDownloadUrlFormatMessage = getLangStr("Update_invalidDownloadUrlFormat"),
                UnsupportedDownloadUrlSchemeMessage = getLangStr("Update_unsupportedDownloadUrlScheme"),
                OpenDownloadUrlFailedMessage = getLangStr("Update_openDownloadFailed")
            };
            using (FormUpdate formUpdate = new FormUpdate(model))
            {
                ApplyUpdateDialogLanguage(formUpdate);
                formUpdate.ShowDialog(this);
            }
        }

        private void ShowUpdateUpToDate()
        {
            MessageBox.Show(this, getLangStr("Update_upToDate"), getLangStr("MainForm_btnCheckUpdate"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowUpdateCheckFailed(string errorMessage)
        {
            string reason = string.IsNullOrWhiteSpace(errorMessage)
                ? getLangStr("Update_unknownError")
                : errorMessage;
            string message = getLangStr("Update_checkFailed") + Environment.NewLine + reason;
            MessageBox.Show(this, message, getLangStr("MainForm_btnCheckUpdate"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void HandleUpdateCheckResult(UpdateCheckResult result)
        {
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    ShowUpdateAvailable(result);
                    return;
                case UpdateCheckStatus.UpToDate:
                    ShowUpdateUpToDate();
                    return;
                case UpdateCheckStatus.Failed:
                    ShowUpdateCheckFailed(result.ErrorMessage);
                    return;
                default:
                    throw new InvalidEnumArgumentException("result.Status", (int)result.Status, typeof(UpdateCheckStatus));
            }
        }

        private async void btnCheckUpdate_Click(object sender, EventArgs e)
        {
            try
            {
                SetCheckUpdateButtonBusy(true);
                UpdateCheckResult result = await updateChecker.CheckAsync();
                if (result == null)
                    throw new InvalidOperationException("Update check returned no result.");
                HandleUpdateCheckResult(result);
            }
            catch (Exception ex)
            {
                ShowUpdateCheckFailed(ex.Message);
            }
            finally
            {
                SetCheckUpdateButtonBusy(false);
            }
        }

        public void sendPonderStatus()
        {
            SendPonderStatusCommand();
        }

        MouseHookListener mh;
        /// <summary>
        /// 注册
        /// </summary>
        /// <param name="strCmd"></param>
        /// <returns></returns>
        static void AutoRegCom(string strCmd)
        {
            // string rInfo;
            try
            {
                Process proc = new Process();
                proc.StartInfo.CreateNoWindow = true;
                proc.StartInfo.FileName = "cmd.exe";
                proc.StartInfo.Arguments = "C:\\Windows\\System32\\cmd.exe";
                proc.StartInfo.UseShellExecute = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.StartInfo.RedirectStandardInput = true;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.Verb = "RunAs";
                proc.StartInfo.UseShellExecute = false;
                proc.Start();
                proc.StandardInput.WriteLine(strCmd);
                proc.Close();
            }
            catch (Exception)
            {
                return;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            string startup = Application.ExecutablePath;
            int pp = startup.LastIndexOf("\\");
            startup = startup.Substring(0, pp);

            mh = new MouseHookListener(new GlobalHooker());

            mh.MouseMove += mh_MouseMoveEvent;
            mh.MouseClick += mh_MouseMoveEvent2;
            mh.Enabled = false;
            this.btnKeepSync.Text = getLangStr("keepSync") + "(" + Program.timename + "ms)";
        }

        //[DllImport("user32.dll")]
        //static extern void BlockInput(bool Block);
        public void Snap(int x1, int y1, int x2, int y2)
        {
            UpdateSelectionBounds(
                Math.Min(x1, x2),
                Math.Min(y1, y2),
                Math.Max(x1, x2),
                Math.Max(y1, y2));
            if (!TryFinalizeSelectionBounds())
            {
                MessageBox.Show(getLangStr("recgnizeFaild"));// Program.isChn ? "不能识别棋盘,请调整被同步棋盘大小后重新选择或尝试[框选1路线]" : "Can not detect board,Please zoom the board and try again or use [CircleRow1]");
                RestoreMainWindowAfterSelection();
            }
            else if (CurrentSyncType == TYPE_BACKGROUND)
                BeginResolveBackgroundSelectionWindowAsync();
            else
                RestoreMainWindowAfterSelection();
            //mh.Enabled = false;
        }

        private bool TryFinalizeSelectionBounds()
        {
            if (!isMannulCircle)
                return TryCalibrateSelectionBounds();

            ExpandManualSelectionBounds();
            return true;
        }

        private void ExpandManualSelectionBounds()
        {
            int gapX = (int)Math.Round((ox2 - selectionX1) / ((boardW - 1) * 2f));
            int gapY = (int)Math.Round((oy2 - selectionY1) / ((boardH - 1) * 2f));
            UpdateSelectionBounds(selectionX1 - gapX, selectionY1 - gapY, ox2 + gapX, oy2 + gapY);
        }

        private bool TryCalibrateSelectionBounds()
        {
            Rectangle selectedBounds = Rectangle.FromLTRB(selectionX1, selectionY1, ox2, oy2);
            LegacySelectionCalibrationResult calibrationResult = selectionCalibrationService.Calibrate(selectedBounds, CreateCurrentBoardSize());
            if (calibrationResult.CapturedBitmap != null)
                Program.ReplaceBitmap(calibrationResult.CapturedBitmap);
            if (!calibrationResult.Success)
            {
                if (!string.IsNullOrWhiteSpace(calibrationResult.FailureReason))
                    SendError(calibrationResult.FailureReason);
                return false;
            }

            Rectangle adjustedBounds = calibrationResult.SelectionBounds;
            UpdateSelectionBounds(adjustedBounds.Left, adjustedBounds.Top, adjustedBounds.Right, adjustedBounds.Bottom);
            return true;
        }

        private void BeginResolveBackgroundSelectionWindowAsync()
        {
            System.Drawing.Point selectionCenter = new System.Drawing.Point((selectionX1 + ox2) / 2, (selectionY1 + oy2) / 2);
            backgroundSelectionWindowBindingCoordinator.Start(
                selectionCenter,
                WindowFromPoint,
                delegate(IntPtr handle)
                {
                    hwnd = handle;
                    ResetMainWindowTitle();
                },
                delegate
                {
                    RestoreMainWindowAfterSelection();
                },
                delegate(Exception ex)
                {
                    SendError(ex.ToString());
                });
        }

        private void RestoreMainWindowAfterSelection()
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        void mh_MouseMoveEvent(object sender, MouseEventArgs e)
        {
            if (CurrentSyncType == TYPE_BACKGROUND)
                return;
        }

        void mh_MouseMoveEvent2(object sender, MouseEventArgs e)
        {
            if (CurrentSyncType == TYPE_BACKGROUND)
                return;
            if (clicked)
            {
                //if (!isKuangxuan)
                //     mh.Enabled = false;
                clicked = false;
                hwnd = getMousePointHwnd();
                ResetMainWindowTitle();
            }


        }

        private void Form1_MouseClick(object sender, MouseEventArgs e)
        {

        }

        private void button3_Click(object sender, EventArgs e)
        {
            mh.Enabled = true;
            clicked = true;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            oneTimeSync();
        }

        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(Point Point);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out Point lpPoint);

        private IntPtr getMousePointHwnd()
        {
            Point p;
            GetCursorPos(out p);
            return WindowFromPoint(p);
        }

        private void oneTimeSync()
        {
            hasRetainedFoxTitleSnapshot = false;
            bool oneTimeSyncSucceeded = sessionCoordinator.TryRunOneTimeSync();
            if (!oneTimeSyncSucceeded)
            {
                ResetMainWindowTitle();
                return;
            }
            if (IsFoxSyncType(CurrentSyncType))
            {
                hasRetainedFoxTitleSnapshot = true;
                ApplyMainWindowTitle();
            }
        }

        public void resetBtnKeepSyncName()
        {
            if (!sessionCoordinator.StartedSync)
                this.btnKeepSync.Text = getLangStr("keepSync") + "("+ Program.timename + "ms)";
        }

        [DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        private void button5_Click(object sender, EventArgs e)
        {
            if (sessionCoordinator.IsContinuousSyncing)
            {
                sessionCoordinator.EndContinuousSync();
            }
            if (!sessionCoordinator.StartedSync)
            {
                sessionCoordinator.TryStartKeepSync();
            }
            else
            {
                stopSync();
            }
        }

        private void stopSync()
        {
            sessionCoordinator.StopSyncSession();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            SendClearCommand();
        }

        private void btnForceRebuild_Click(object sender, EventArgs e)
        {
            sessionCoordinator.ArmForceRebuild();
            if (HasActiveSyncOperation())
            {
                InvalidateFoxWindowBinding();
                RefreshMainWindowTitleFromCurrentWindow();
            }
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoFox.Checked)
                setNativeBoardMode(TYPE_FOX);
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoTygem.Checked)
                setNativeBoardMode(TYPE_TYGEM);
        }

        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoBack.Checked)
                setManualSelectionMode(TYPE_BACKGROUND);
        }

        private void radioButton4_CheckedChanged(object sender, EventArgs e)
        {

            if (this.rdoSina.Checked)
                setNativeBoardMode(TYPE_SINA);

        }

        private void radioButtonFoxBack_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoFoxBack.Checked)
                setNativeBoardMode(TYPE_FOX_BACKGROUND_PLACE);
        }

        public void saveOtherConfig()
        {
            PersistConfiguration();
        }

        public void shutdown()
        {
            shutdown(true);
        }

        public void shutdown(bool persistConfiguration)
        {
            List<Exception> shutdownExceptions = new List<Exception>();

            lock (placeProtocolSyncRoot)
            {
                if (isShuttingDown)
                    return;

                isShuttingDown = true;
                RunShutdownStep(shutdownExceptions, delegate { placeRequestQueue.Stop(); });
                RunShutdownStep(shutdownExceptions, delegate { ClearPendingProtocolCommands(); });
            }
            ResetMainWindowTitle();
            if (persistConfiguration)
                RunShutdownStep(shutdownExceptions, delegate { PersistConfiguration(); });
            RunShutdownStep(shutdownExceptions, delegate { DisposeInputHooks(); });
            RunShutdownStep(shutdownExceptions, delegate { SendShutdownProtocol(); });
            RunShutdownStep(shutdownExceptions, delegate { Program.DisposeBitmap(); });
            RunShutdownStep(shutdownExceptions, delegate { sessionCoordinator.Stop(); });
            RunShutdownStep(shutdownExceptions, delegate
            {
                if (!IsHandleCreated)
                {
                    closeRequestedBeforeHandle = true;
                    return;
                }
                if (IsDisposed || Disposing)
                    return;
                BeginInvoke((Action)Close);
            });
            ThrowShutdownExceptions(shutdownExceptions);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!isShuttingDown && !IsDisposed && !Disposing)
                ApplyMainFormUi();
            FlushPendingProtocolCommands();
            if (!closeRequestedBeforeHandle || IsDisposed)
                return;
            BeginInvoke((Action)Close);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (isShuttingDown || IsDisposed || Disposing)
                return;
            factor = GetCurrentDpiScale();
            ApplyMainFormUi();
        }

        private void DisposeInputHooks()
        {
            if (hookListener != null)
            {
                hookListener.KeyDown -= HookListener_KeyDown;
                hookListener.KeyUp -= HookListener_KeyUp;
                hookListener.Stop();
                hookListener.Dispose();
                hookListener = null;
            }
            if (mh == null)
                return;
            mh.MouseMove -= mh_MouseMoveEvent;
            mh.MouseClick -= mh_MouseMoveEvent2;
            mh.Enabled = false;
            mh.Stop();
            mh.Dispose();
            mh = null;
        }

        private static void RunShutdownStep(List<Exception> shutdownExceptions, Action shutdownStep)
        {
            try
            {
                shutdownStep();
            }
            catch (Exception ex)
            {
                shutdownExceptions.Add(ex);
            }
        }

        private static void ThrowShutdownExceptions(List<Exception> shutdownExceptions)
        {
            if (shutdownExceptions.Count == 0)
                return;
            if (shutdownExceptions.Count == 1)
                ExceptionDispatchInfo.Capture(shutdownExceptions[0]).Throw();
            throw new AggregateException("MainForm shutdown failed.", shutdownExceptions);
        }

        private void form_closing(object sender, FormClosingEventArgs e)
        {
            if (isShuttingDown)
                return;
            e.Cancel = true;
            shutdown();
        }

        private void radioButton5_CheckedChanged(object sender, EventArgs e)
        {
            if (isInitializingProtocolState)
                return;
            if (this.rdo19x19.Checked)
            {
                boardW = 19;
                boardH = 19;
                this.txtBoardHeight.BackColor = System.Drawing.SystemColors.Menu;
                this.txtBoardWidth.BackColor = System.Drawing.SystemColors.Menu;
            }
            saveOtherConfig();
        }

        private void radioButton6_CheckedChanged(object sender, EventArgs e)
        {
            if (isInitializingProtocolState)
                return;
            if (this.rdo13x13.Checked)
            {
                boardW = 13;
                boardH = 13;
                this.txtBoardHeight.BackColor = System.Drawing.SystemColors.Menu;
                this.txtBoardWidth.BackColor = System.Drawing.SystemColors.Menu;
            }
            saveOtherConfig();
        }

        private void radioButton7_CheckedChanged(object sender, EventArgs e)
        {
            if (isInitializingProtocolState)
                return;
            if (this.rdo9x9.Checked)
            {
                boardW = 9;
                boardH = 9;
                this.txtBoardHeight.BackColor = System.Drawing.SystemColors.Menu;
                this.txtBoardWidth.BackColor = System.Drawing.SystemColors.Menu;
            }
            saveOtherConfig();
        }

        public void sendVersion()
        {
            SendVersionCommand();
        }

        public void stopInBoard()
        {
            this.chkShowInBoard.Checked = false;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetForegroundWindow", CharSet = System.Runtime.InteropServices.CharSet.Auto, ExactSpelling = true)]
        public static extern IntPtr GetF();

        public void lossFocus()
        {
            if (GetF() != FindWindow("SunAwtDialog", "FloatBoard"))//dm.FindWindow("SunAwtDialog", "FloatBoard"))              
            {
                mouse_event((int)(MouseEventFlags.MiddleDown | MouseEventFlags.Absolute), 0, 0, 0, IntPtr.Zero);
                mouse_event((int)(MouseEventFlags.MiddleUp | MouseEventFlags.Absolute), 0, 0, 0, IntPtr.Zero);
            }
        }

        //class MoveInfo
        //{
        //    public int x;
        //    public int y;
        //}

        [DllImport("USER32.DLL")]
        public static extern void SwitchToThisWindow(IntPtr hwnd, Boolean fAltTab);

        [DllImport("USER32.DLL")]
        public static extern IntPtr GetParent(IntPtr hwnd);

        public void placeMove(int x, int y)
        {
            EnqueuePlaceRequest(new MoveRequest
            {
                X = x,
                Y = y,
                VerifyMove = Program.verifyMove
            });
        }

        private const int MK_LBUTTON = 0x0001;
        uint WM_MOUSEMOVE = 0x200;
        uint WM_LBUTTONDOWN = 0x201;
        uint WM_LBUTTONUP = 0x202;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        private static int buildMouseLParam(int x, int y)
        {
            return (x & 0xFFFF) | ((y & 0xFFFF) << 16);
        }

        // Keep legacy background modes non-blocking to preserve their historical behavior.
        private void postBackgroundMouseClick(int x, int y, IntPtr hwnd)
        {
            int lParam = buildMouseLParam(x, y);
            PostMessage(hwnd, WM_LBUTTONDOWN, 0, lParam);
            PostMessage(hwnd, WM_LBUTTONUP, 0, lParam);
        }

        // Fox background placement needs a blocking move/click sequence in client coordinates.
        private void sendBackgroundMouseClickWithMove(int x, int y, IntPtr hwnd)
        {
            int lParam = buildMouseLParam(x, y);
            SendMessage(hwnd, WM_MOUSEMOVE, 0, lParam);
            SendMessage(hwnd, WM_LBUTTONDOWN, MK_LBUTTON, lParam);
            SendMessage(hwnd, WM_LBUTTONUP, 0, lParam);
        }

        public enum MouseEventFlags
        {
            Move = 0x0001,
            LeftDown = 0x0002,
            LeftUp = 0x0004,
            RightDown = 0x0008,
            RightUp = 0x0010,
            MiddleDown = 0x0020,
            MiddleUp = 0x0040,
            Wheel = 0x0800,
            Absolute = 0x8000
        }
        [DllImport("User32")]
        public extern static void mouse_event(int dwFlags, int dx, int dy, int dwData, IntPtr dwExtraInfo);

        private void textbox1_TextChanged(object sender, EventArgs e)
        {
            NormalizeNumericTextBox(textBox1);
            if (isInitializingProtocolState)
                return;
            SendTimeChangedCommand();
        }

        private void button7_Click_1(object sender, EventArgs e)
        {
            SendNoPonderCommand();
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            NormalizeNumericTextBox(textBox2);
            if (isInitializingProtocolState)
                return;
            SendPlayoutsChangedCommand();
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
            NormalizeNumericTextBox(textBox3);
            if (isInitializingProtocolState)
                return;
            SendFirstPolicyChangedCommand();
        }

        private void checkBox1_CheckedChanged_1(object sender, EventArgs e)
        {
            SetSyncBoth(chkBothSync.Checked);
            chkAutoPlay.Enabled = sessionCoordinator.SyncBoth;
            if (isInitializingProtocolState)
                return;
            SendBothSyncStateChange();
            ResendSyncSessionState();
            this.saveOtherConfig();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                Process process1 = new Process();
                process1.StartInfo.FileName = getLangStr("helpFile");
                process1.StartInfo.Arguments = "";
                process1.StartInfo.WindowStyle = ProcessWindowStyle.Maximized;
                process1.Start();
            }
            catch (Exception)
            {
                MessageBox.Show(getLangStr("noHelpFile")); //(Program.isChn ? "找不到说明文档,请检查Lizzie目录下[readboard]文件夹内的[readme.rtf]文件是否存在" : "Can not find file,Please check [readme.rtf] file is in the folder [readboard]");
            }
        }

        private void rdoqiantai_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoFore.Checked)
                setManualSelectionMode(TYPE_FOREGROUND);
        }

        private void chkAutoPlay_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutoPlay.Checked)
            {
                radioWhite.Enabled = true;
                radioBlack.Enabled = true;
                textBox1.Enabled = true;
                textBox2.Enabled = true;
                textBox3.Enabled = true;
            }
            else
            {
                radioWhite.Checked = false;
                radioBlack.Checked = false;
                radioWhite.Enabled = false;
                radioBlack.Enabled = false;
                textBox1.Enabled = false;
                textBox2.Enabled = false;
                textBox3.Enabled = false;
                SendStopAutoPlayCommand();
            }
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {
            SettingsForm form4 = new SettingsForm(this);
            form4.Show(this);
        }

        private void radioButton8_CheckedChanged(object sender, EventArgs e)
        {
            if (isInitializingProtocolState)
                return;
            if (this.rdoOtherBoard.Checked)
            {
                try
                {
                    this.boardW = int.Parse(txtBoardWidth.Text);
                    this.boardH = int.Parse(txtBoardHeight.Text);
                }
                catch (Exception)
                {
                    // MessageBox.Show(Program.isChn?"错误的棋盘大小":"Wrong goban size!");
                }
            }
            saveOtherConfig();
        }



        private void parseWidth(object sender, EventArgs e)
        {
            try
            {
                if (this.rdoOtherBoard.Checked)
                {
                    this.boardW = int.Parse(txtBoardWidth.Text);
                    saveOtherConfig();
                }
                else
                {
                    int w = int.Parse(txtBoardWidth.Text);
                }
            }
            catch (Exception)
            {
            }
        }

        private void parseHeight(object sender, EventArgs e)
        {
            try
            {
                if (this.rdoOtherBoard.Checked)
                {
                    this.boardH = int.Parse(txtBoardHeight.Text);
                    saveOtherConfig();
                }
                else
                {
                    int h = int.Parse(txtBoardHeight.Text);
                }
            }
            catch (Exception)
            {
            }
        }

        private void tb_KeyPressWidth(object sender, KeyPressEventArgs e)
        {
            if (!(e.KeyChar == '\b' || (e.KeyChar >= '0' && e.KeyChar <= '9')))
            {
                e.Handled = true;
            }
            txtBoardWidth.BackColor = System.Drawing.SystemColors.Menu;
        }

        private void tb_KeyPressHeight(object sender, KeyPressEventArgs e)
        {
            if (!(e.KeyChar == '\b' || (e.KeyChar >= '0' && e.KeyChar <= '9')))
            {
                e.Handled = true;
            }
            txtBoardHeight.BackColor = System.Drawing.SystemColors.Menu;
        }

        private void radioBlack_CheckedChanged(object sender, EventArgs e)
        {
            if (radioBlack.Checked)
                radioWhite.Checked = false;
            if (sessionCoordinator.KeepSync)
                SendPlayCommandIfSelected();
        }

        private void radioWhite_CheckedChanged(object sender, EventArgs e)
        {
            if (radioWhite.Checked)
                radioBlack.Checked = false;
            if (sessionCoordinator.KeepSync)
                SendPlayCommandIfSelected();
        }


        private void button8_Click(object sender, EventArgs e)
        {
            SendPassCommand();
        }

        private void chkShowInBoard_CheckedChanged(object sender, EventArgs e)
        {
            if (chkShowInBoard.Checked && CurrentSyncType == TYPE_FOREGROUND)
            {
                chkShowInBoard.Checked = false;
                return;
            }
            Program.showInBoard = chkShowInBoard.Checked;
            if (isInitializingProtocolState)
                return;
            PersistConfiguration();
            if (CanUseForegroundFoxInBoardProtocol())
                SendForegroundFoxInBoardCommand(chkShowInBoard.Checked && sessionCoordinator.SyncBoth);
            if (chkShowInBoard.Checked)
            {
                if (Program.showInBoardHint)
                {
                    TipsForm form7 = new TipsForm(this);
                    form7.ShowDialog(this);
                }
            }
            else
            {
                SendNotInBoardCommand();
            }
        }

        private void button9_Click(object sender, EventArgs e)
        {
            MessageBox.Show(getLangStr("komi65Describe")); 
            //if (!Program.isChn)
            //    MessageBox.Show("Because of lack of move history,captured stone number will be incorrectly,So only area scoring can be used.You can set rules [area scoring + 7.0 komi + hasbutton] to simulate Japanese rule.");
            //else
            //MessageBox.Show("由于同步时无法获取提子数,日本规则(数目)将变得不准确,需要同步日本规则贴6.5目的棋局时可在Katago中使用[数子+贴目7.0+收后方贴还0.5目]规则模拟");

            //else {
            //    try
            //    {
            //        Process process1 = new Process();
            //        process1.StartInfo.FileName = "readboard\\65komi.rtf";
            //        process1.StartInfo.Arguments = "";
            //        process1.StartInfo.WindowStyle = ProcessWindowStyle.Maximized;
            //        process1.Start();
            //    }
            //    catch (Exception)
            //    {
            //        MessageBox.Show(Program.isChn ? "找不到说明文档,请检查Lizzie目录下[readboard]文件夹内的[65komi.rtf]文件是否存在" : "Can not find file,Please check [65komi.rtf] file is in the folder [readboard]");
            //    }
            //}            
        }

        private void button10_Click(object sender, EventArgs e)
        {
            if (!sessionCoordinator.IsContinuousSyncing && !sessionCoordinator.StartedSync)
            {
                sessionCoordinator.TryStartContinuousSync();
            }
            else
            {
                stopSync();
            }
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            isMannulCircle = false;
            selectBoard();
        }

        private void button11_Click(object sender, EventArgs e)
        {
            isMannulCircle = true;
            selectBoard();
        }

        private void selectBoard()
        {
            mh.Enabled = true;
            this.WindowState = FormWindowState.Minimized;
            form2 = new Form2(this, isMannulCircle);
            form2.ShowDialog(this);
        }

    }
}
