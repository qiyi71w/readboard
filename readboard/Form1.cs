using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

namespace readboard
{
    public partial class MainForm : Form, IProtocolCommandHost, IAnalysisStateProtocolHost, ISyncCoordinatorHost, IWebViewSyncCoordinatorHost
    {
        // Boolean showDebugImage = true;
        Boolean clicked = false;

        private int selectionX1;
        int ox2;
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
        private const int TYPE_YIKE = 6;
        private const int ContinuousSyncPollIntervalMs = 100;
        // Boolean isQTYC = false;
        // int boardWidth=19;
        //Boolean noticeLast = true;
        //Boolean noLw = false;
        Boolean isMannulCircle = false;
        float factor = 1.0f;
        private readonly LaunchOptions launchOptions;
        private readonly ISyncSessionCoordinator sessionCoordinator;
        private readonly ILegacySelectionCalibrationService selectionCalibrationService;
        private readonly ControlCenterRuntime controlCenterRuntime;
        private readonly UiThreadInvoker uiThreadInvoker;
        private readonly SerialBackgroundWorkQueue placeRequestQueue;
        private HostedUpdateJourney hostedUpdateJourney;
        private readonly object placeProtocolSyncRoot = new object();
        private readonly object protocolCommandSyncRoot = new object();
        private readonly GitHubUpdateChecker updateChecker = new GitHubUpdateChecker();
        private readonly Queue<Action> pendingProtocolCommands = new Queue<Action>();
        private readonly BackgroundSelectionWindowBindingCoordinator backgroundSelectionWindowBindingCoordinator =
            new BackgroundSelectionWindowBindingCoordinator();
        private const int MainFormMinimumLogicalWidth = 360;
        private const int MainFormScreenLogicalPadding = 40;
        private FoxWindowContext lastFoxWindowContext = FoxWindowContext.Unknown();
        private YikeWindowContext lastYikeWindowContext = YikeWindowContext.Unknown();
        private IntPtr lastYikeContextWindowHandle = IntPtr.Zero;
        private FoxWindowBinding foxWindowBinding = null;
        private bool hasRetainedFoxTitleSnapshot = false;
        private MainWindowTitleTurn lastMainWindowTitleTurn = MainWindowTitleTurn.None;
        private string lastAppliedMainWindowTitle = string.Empty;
        private readonly IBoardCapturePlatform foxAutoPlayCapturePlatform = new Win32BoardCapturePlatform();
        private AutoPlayColorResolution lastFoxAutoPlayColorDetection = null;
        private IntPtr lastFoxAutoPlayColorDetectionWindowHandle = IntPtr.Zero;
        private string lastFoxAutoPlayColorDetectionContextSignature = string.Empty;
        private string lastFoxAutoPlayColorDetectionNicknameSignature = string.Empty;
        private string currentFoxAutoPlayNicknameSignature = string.Empty;
        private DateTime lastFoxAutoPlayColorDetectionTimestampUtc = DateTime.MinValue;
        private const int FoxAutoPlayColorDetectionCacheMs = 1000;

        int posX = -1;
        int posY = -1;

        private Button btnTheme;
        private Panel pnlAutoPlayColorStatus;
        private Panel pnlFoxAutoPlayIdentity;
        private ContextMenuStrip themeMenu;
        private ToolStripMenuItem menuThemeOptimized;
        private ToolStripMenuItem menuThemeClassic;
        private bool isMainFormSizeInitialized = false;
        private bool isApplyingMainFormLayout = false;
        private bool isShuttingDown = false;
        private bool closeRequestedBeforeHandle = false;
        private bool isInitializingProtocolState = true;
        private bool hostedUpdateSupported = false;
        private bool hostedUpdatePackageV2Supported = false;
        private bool suppressAutoPlayColorModeEvents = false;
        private bool suppressAutoPlayMoveModeEvents = false;
        private bool suppressControlCenterProjectionEvents = false;
        private bool suppressWebViewStatePublication = false;
        private int suppressedWebViewStatePublicationScopeDepth;
        private bool suppressedWebViewStatePublicationPending;
        private AutoPlayColorMode lastManualAutoPlayColorMode = AutoPlayColorMode.ManualBlack;
        private static readonly System.Drawing.Size MainFormDefaultSize = new System.Drawing.Size(852, 374);

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
            return IsFoxSyncType(syncType) || syncType == TYPE_TYGEM || syncType == TYPE_SINA || syncType == TYPE_YIKE;
        }

        private int CurrentSyncType
        {
            get
            {
                return controlCenterRuntime == null
                    ? TYPE_FOX
                    : (int)controlCenterRuntime.CurrentPreferences.Platform;
            }
        }

        private void UpdateSelectionBounds(int x1, int y1, int x2, int y2)
        {
            selectionX1 = x1;
            selectionY1 = y1;
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

        private bool IsOptimizedTheme()
        {
            return Program.uiThemeMode == Program.UiThemeOptimized;
        }

        private static bool IsDarkMode()
        {
            return UiTheme.IsDarkMode;
        }

        private IEnumerable<GroupBox> MainThemeGroups()
        {
            return new[] { groupBox1, groupBox2, groupBox4 };
        }

        private IEnumerable<Control> MainThemeSurfaces()
        {
            return new Control[] { flowLayoutPanel1, flowLayoutPanel2, flowLayoutPanelAutoPlayMoveMode, panel1, panel2, panel3, panel4, pnlAutoPlayColorStatus, pnlFoxAutoPlayIdentity };
        }

        private IEnumerable<ButtonBase> MainThemeOptions()
        {
            return new ButtonBase[] { rdoFox, rdoFoxBack, rdoYike, rdoTygem, rdoSina, rdoBack, rdoFore, rdo19x19, rdo13x13, rdo9x9, rdoOtherBoard, chkBothSync, chkAutoPlay, chkShowInBoard, radioBlack, radioWhite, radioAutoPlayColor, radioAutoPlayMoveFirst, radioAutoPlayMoveGma, btnFoxAutoPlayIdentity };
        }

        private IEnumerable<TextBox> MainThemeInputs()
        {
            return new[] { textBox1, textBox2, textBox3, txtBoardWidth, txtBoardHeight };
        }

        private IEnumerable<Label> MainThemeLabels()
        {
            return new[] { lblBoardSize, lblPlayCondition, lblTime, lblTotalVisits, lblBestMoveVisits, lblAutoPlayColorStatus, lblAutoPlayMoveMode, label6 };
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

            pnlAutoPlayColorStatus = new Panel();
            pnlAutoPlayColorStatus.Name = "pnlAutoPlayColorStatus";
            pnlAutoPlayColorStatus.Margin = Padding.Empty;
            pnlAutoPlayColorStatus.TabStop = false;

            pnlFoxAutoPlayIdentity = new Panel();
            pnlFoxAutoPlayIdentity.Name = "pnlFoxAutoPlayIdentity";
            pnlFoxAutoPlayIdentity.Margin = Padding.Empty;
            pnlFoxAutoPlayIdentity.TabStop = false;
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
                UiTheme.ResetOption(option);
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
            rdoYike.Location = new Point(rdoFoxBack.Right + optionGap, optionTop);
            rdoTygem.Location = new Point(rdoYike.Right + optionGap, optionTop);
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
            int minimumPlatformWidth = Math.Min(contentWidth, MeasureOptionsWidth(new ButtonBase[] { rdoFox, rdoFoxBack, rdoYike, rdoTygem, rdoSina, rdoBack, rdoFore }, optionGap) + ScaleValue(28));
            bool canUseSideBySide = contentWidth >= minimumPlatformWidth + buttonGap + utilityColumnWidth + ScaleValue(24);

            int groupWidth = canUseSideBySide ? contentWidth - utilityColumnWidth - buttonGap : contentWidth;
            groupBox1.SetBounds(left, top, groupWidth, 0);
            int groupBottom = LayoutOptionsRow(new ButtonBase[] { rdoFox, rdoFoxBack, rdoYike, rdoTygem, rdoSina, rdoBack, rdoFore }, groupBox1, optionLeft, optionTop, optionGap, rowGap);
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
            int conditionLabelWidth = GetMainSyncConditionTimeLabelWidth();

            ArrangeMainSyncFlowOrder();
            groupBox4.SetBounds(left, top, groupWidth, ScaleValue(132));
            flowLayoutPanel1.SetBounds(ScaleValue(16), ScaleValue(28), rowWidth, ScaleValue(30));
            flowLayoutPanel2.SetBounds(ScaleValue(16), ScaleValue(62), rowWidth, ScaleValue(30));
            flowLayoutPanelAutoPlayMoveMode.SetBounds(ScaleValue(16), ScaleValue(96), rowWidth, ScaleValue(30));
            flowLayoutPanel1.WrapContents = false;
            flowLayoutPanel2.WrapContents = false;
            flowLayoutPanelAutoPlayMoveMode.WrapContents = false;
            chkBothSync.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioBlack.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            chkAutoPlay.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioWhite.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            lblAutoPlayMoveMode.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioAutoPlayMoveFirst.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioAutoPlayMoveGma.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            pnlAutoPlayColorStatus.Margin = new Padding(0, 0, 0, 0);
            pnlFoxAutoPlayIdentity.Margin = new Padding(0, 0, 0, 0);
            panel1.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel2.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel3.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel4.Margin = new Padding(GetMainSyncTimeRowVisitsLeftMargin(), ScaleValue(2), 0, 0);
            panel1.AutoSize = false;
            panel2.AutoSize = false;
            panel3.AutoSize = false;
            panel4.AutoSize = false;
            panel1.Size = new Size(GetMainSyncConditionTimeSlotWidth(), rowHeight);
            panel2.Size = new Size(sharedLegacyVisitsPanelWidth, rowHeight);
            panel3.Size = new Size(GetMainSyncTimeLabelPanelWidth(), rowHeight);
            panel4.Size = new Size(sharedLegacyVisitsPanelWidth, rowHeight);
            lblPlayCondition.AutoSize = false;
            lblTotalVisits.AutoSize = false;
            lblTime.AutoSize = false;
            lblBestMoveVisits.AutoSize = false;
            lblPlayCondition.SetBounds(0, ScaleValue(3), conditionLabelWidth, ScaleValue(18));
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
            ArrangeMainSyncAutoStatusColumn(rowHeight);
            flowLayoutPanel1.Height = ScaleValue(30);
            flowLayoutPanel2.Height = ScaleValue(30);
            flowLayoutPanelAutoPlayMoveMode.Height = ScaleValue(30);
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
            int conditionLabelWidth = GetMainSyncConditionTimeLabelWidth();

            ArrangeMainSyncFlowOrder();
            groupBox4.SetBounds(left, top, groupWidth, 0);
            flowLayoutPanel1.SetBounds(ScaleValue(16), ScaleValue(28), rowWidth, rowHeight);
            flowLayoutPanel2.SetBounds(ScaleValue(16), flowLayoutPanel1.Bottom + ScaleValue(8), rowWidth, rowHeight);
            flowLayoutPanelAutoPlayMoveMode.SetBounds(ScaleValue(16), flowLayoutPanel2.Bottom + ScaleValue(8), rowWidth, rowHeight);
            flowLayoutPanel1.WrapContents = true;
            flowLayoutPanel2.WrapContents = true;
            flowLayoutPanelAutoPlayMoveMode.WrapContents = true;
            chkBothSync.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioBlack.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            chkAutoPlay.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioWhite.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            lblAutoPlayMoveMode.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioAutoPlayMoveFirst.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            radioAutoPlayMoveGma.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);
            pnlAutoPlayColorStatus.Margin = new Padding(0, 0, 0, 0);
            pnlFoxAutoPlayIdentity.Margin = new Padding(0, 0, 0, 0);
            panel1.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel2.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel3.Margin = new Padding(ScaleValue(12), ScaleValue(2), 0, 0);
            panel4.Margin = new Padding(GetMainSyncTimeRowVisitsLeftMargin(), ScaleValue(2), 0, 0);
            panel1.AutoSize = false;
            panel2.AutoSize = false;
            panel3.AutoSize = false;
            panel4.AutoSize = false;
            panel1.Size = new System.Drawing.Size(GetMainSyncConditionTimeSlotWidth(), rowHeight);
            panel2.Size = new System.Drawing.Size(sharedAdaptiveVisitsPanelWidth, rowHeight);
            panel3.Size = new System.Drawing.Size(GetMainSyncTimeLabelPanelWidth(), rowHeight);
            panel4.Size = new System.Drawing.Size(sharedAdaptiveVisitsPanelWidth, rowHeight);
            lblPlayCondition.AutoSize = false;
            lblTotalVisits.AutoSize = false;
            lblTime.AutoSize = false;
            lblBestMoveVisits.AutoSize = false;
            lblPlayCondition.SetBounds(0, ScaleValue(3), conditionLabelWidth, ScaleValue(20));
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
            ArrangeMainSyncAutoStatusColumn(rowHeight);
            flowLayoutPanel1.Height = flowLayoutPanel1.GetPreferredSize(new Size(rowWidth, 0)).Height;
            flowLayoutPanel2.Top = flowLayoutPanel1.Bottom + ScaleValue(8);
            flowLayoutPanel2.Height = flowLayoutPanel2.GetPreferredSize(new Size(rowWidth, 0)).Height;
            flowLayoutPanelAutoPlayMoveMode.Top = flowLayoutPanel2.Bottom + ScaleValue(8);
            flowLayoutPanelAutoPlayMoveMode.Height = flowLayoutPanelAutoPlayMoveMode.GetPreferredSize(new Size(rowWidth, 0)).Height;
            groupBox4.Height = flowLayoutPanelAutoPlayMoveMode.Bottom + ScaleValue(10);
            return groupBox4.Bottom;
        }

        private void ArrangeMainSyncFlowOrder()
        {
            if (radioAutoPlayColor.Parent != pnlAutoPlayColorStatus)
                pnlAutoPlayColorStatus.Controls.Add(radioAutoPlayColor);
            if (lblAutoPlayColorStatus.Parent != pnlAutoPlayColorStatus)
                pnlAutoPlayColorStatus.Controls.Add(lblAutoPlayColorStatus);
            if (btnFoxAutoPlayIdentity.Parent != pnlFoxAutoPlayIdentity)
                pnlFoxAutoPlayIdentity.Controls.Add(btnFoxAutoPlayIdentity);
            if (pnlAutoPlayColorStatus.Parent != flowLayoutPanel1)
                flowLayoutPanel1.Controls.Add(pnlAutoPlayColorStatus);
            if (pnlFoxAutoPlayIdentity.Parent != flowLayoutPanel2)
                flowLayoutPanel2.Controls.Add(pnlFoxAutoPlayIdentity);
            if (lblAutoPlayMoveMode.Parent != flowLayoutPanelAutoPlayMoveMode)
                flowLayoutPanelAutoPlayMoveMode.Controls.Add(lblAutoPlayMoveMode);
            if (radioAutoPlayMoveFirst.Parent != flowLayoutPanelAutoPlayMoveMode)
                flowLayoutPanelAutoPlayMoveMode.Controls.Add(radioAutoPlayMoveFirst);
            if (radioAutoPlayMoveGma.Parent != flowLayoutPanelAutoPlayMoveMode)
                flowLayoutPanelAutoPlayMoveMode.Controls.Add(radioAutoPlayMoveGma);

            flowLayoutPanel1.Controls.SetChildIndex(chkBothSync, 0);
            flowLayoutPanel1.Controls.SetChildIndex(radioBlack, 1);
            flowLayoutPanel1.Controls.SetChildIndex(pnlAutoPlayColorStatus, 2);
            flowLayoutPanel1.Controls.SetChildIndex(panel1, 3);
            flowLayoutPanel1.Controls.SetChildIndex(panel2, 4);
            flowLayoutPanel1.Controls.SetChildIndex(textBox2, 5);

            flowLayoutPanel2.Controls.SetChildIndex(chkAutoPlay, 0);
            flowLayoutPanel2.Controls.SetChildIndex(radioWhite, 1);
            flowLayoutPanel2.Controls.SetChildIndex(pnlFoxAutoPlayIdentity, 2);
            flowLayoutPanel2.Controls.SetChildIndex(panel3, 3);
            flowLayoutPanel2.Controls.SetChildIndex(textBox1, 4);
            flowLayoutPanel2.Controls.SetChildIndex(panel4, 5);
            flowLayoutPanel2.Controls.SetChildIndex(textBox3, 6);

            flowLayoutPanelAutoPlayMoveMode.Controls.SetChildIndex(lblAutoPlayMoveMode, 0);
            flowLayoutPanelAutoPlayMoveMode.Controls.SetChildIndex(radioAutoPlayMoveFirst, 1);
            flowLayoutPanelAutoPlayMoveMode.Controls.SetChildIndex(radioAutoPlayMoveGma, 2);
        }

        private void ArrangeMainSyncAutoStatusColumn(int rowHeight)
        {
            int columnWidth = GetMainSyncAutoStatusColumnWidth();
            int columnHeight = Math.Max(rowHeight, btnFoxAutoPlayIdentity.PreferredSize.Height + ScaleValue(2));
            pnlAutoPlayColorStatus.Size = new Size(columnWidth, columnHeight);
            pnlFoxAutoPlayIdentity.Size = new Size(columnWidth, columnHeight);
            radioAutoPlayColor.Margin = Padding.Empty;
            lblAutoPlayColorStatus.Margin = Padding.Empty;
            btnFoxAutoPlayIdentity.Margin = Padding.Empty;
            radioAutoPlayColor.Location = new Point(0, Math.Max(0, (columnHeight - radioAutoPlayColor.PreferredSize.Height) / 2));
            lblAutoPlayColorStatus.Location = new Point(
                radioAutoPlayColor.Right + ScaleValue(6),
                Math.Max(0, (columnHeight - lblAutoPlayColorStatus.PreferredSize.Height) / 2));
            btnFoxAutoPlayIdentity.Location = new Point(0, Math.Max(0, (columnHeight - btnFoxAutoPlayIdentity.PreferredSize.Height) / 2));
        }

        private int GetMainSyncAutoStatusColumnWidth()
        {
            int autoStatusWidth = GetLayoutOptionPreferredSize(radioAutoPlayColor).Width + ScaleValue(6) + GetMainSyncAutoPlayStatusTextWidth();
            int identityWidth = GetLayoutOptionPreferredSize(btnFoxAutoPlayIdentity).Width;
            return Math.Max(autoStatusWidth, identityWidth);
        }

        private int GetMainSyncAutoPlayStatusTextWidth()
        {
            string[] statusTexts = new string[]
            {
                string.Empty,
                getLangStr("MainForm_autoPlayColorStatusUnconfigured"),
                getLangStr("MainForm_autoPlayColorStatusBlack"),
                getLangStr("MainForm_autoPlayColorStatusWhite"),
                getLangStr("MainForm_autoPlayColorStatusUnsupported"),
                getLangStr("MainForm_autoPlayColorStatusSpectating"),
                getLangStr("MainForm_autoPlayColorStatusWaiting")
            };
            int width = 0;
            for (int i = 0; i < statusTexts.Length; i++)
                width = Math.Max(width, TextRenderer.MeasureText(statusTexts[i], lblAutoPlayColorStatus.Font).Width);
            return width;
        }

        private int GetMainSyncConditionTimeLabelWidth()
        {
            return Math.Max(lblPlayCondition.PreferredSize.Width, lblTime.PreferredSize.Width);
        }

        private int GetMainSyncTimeLabelPanelWidth()
        {
            return lblTime.PreferredSize.Width + ScaleValue(18);
        }

        private int GetMainSyncConditionTimeSlotWidth()
        {
            return GetMainSyncConditionTimeLabelWidth() + ScaleValue(18) + ScaleValue(8) + ScaleValue(68);
        }

        private int GetMainSyncTimeRowVisitsLeftMargin()
        {
            int usedWidth = GetMainSyncTimeLabelPanelWidth() + ScaleValue(8) + ScaleValue(68);
            return ScaleValue(12) + Math.Max(0, GetMainSyncConditionTimeSlotWidth() - usedWidth);
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

            if (option is Button button)
            {
                using (Button probe = new Button())
                {
                    probe.AutoSize = true;
                    probe.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    probe.Text = button.Text;
                    probe.Font = button.Font;
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
                new ButtonBase[] { rdoFox, rdoFoxBack, rdoYike, rdoTygem, rdoSina, rdoBack, rdoFore },
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
            int buttonGap = ScaleValue(12);
            int sharedVisitsPanelWidth = GetLegacyMainSyncVisitsPanelWidth();
            int row1Width =
                GetLayoutOptionPreferredSize(chkBothSync).Width
                + buttonGap
                + GetLayoutOptionPreferredSize(radioBlack).Width
                + buttonGap
                + GetMainSyncAutoStatusColumnWidth()
                + buttonGap
                + GetMainSyncConditionTimeSlotWidth()
                + buttonGap
                + sharedVisitsPanelWidth
                + ScaleValue(8)
                + ScaleValue(92);
            int row2Width =
                GetLayoutOptionPreferredSize(chkAutoPlay).Width
                + buttonGap
                + GetLayoutOptionPreferredSize(radioWhite).Width
                + buttonGap
                + GetMainSyncAutoStatusColumnWidth()
                + buttonGap
                + GetMainSyncConditionTimeSlotWidth()
                + buttonGap
                + sharedVisitsPanelWidth
                + ScaleValue(8)
                + ScaleValue(92);
            int moveModeWidth =
                lblAutoPlayMoveMode.PreferredSize.Width
                + buttonGap
                + GetLayoutOptionPreferredSize(radioAutoPlayMoveFirst).Width
                + buttonGap
                + GetLayoutOptionPreferredSize(radioAutoPlayMoveGma).Width;
            return left * 2 + ScaleValue(34) + Math.Max(Math.Max(row1Width, row2Width), moveModeWidth);
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
            if (isInitializingProtocolState)
                return;
            if (suppressControlCenterProjectionEvents)
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetPlatform((SyncMode)syncType));
            ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void setManualSelectionMode(int syncType)
        {
            if (isInitializingProtocolState)
                return;
            if (suppressControlCenterProjectionEvents)
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetPlatform((SyncMode)syncType));
            ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void ApplySyncModeControlState()
        {
            bool manualSelectionMode = UsesManualSelectionType(CurrentSyncType);
            if (CurrentSyncType != TYPE_YIKE)
                ClearYikeContext();
            btnCircleBoard.Enabled = manualSelectionMode;
            btnCircleRow1.Enabled = manualSelectionMode;
            btnClickBoard.Enabled = !manualSelectionMode;
            ApplyControlCenterNativeEnablement();
            ApplyShowInBoardControlState();
            ApplyAutoPlayColorAvailability();
            ResetMainWindowTitle();
        }

        private void ApplyShowInBoardControlState()
        {
            bool supportsShowInBoard = controlCenterRuntime.Snapshot.ShowOnBoardEnabled;
            chkShowInBoard.Enabled = supportsShowInBoard;
            if (!supportsShowInBoard && chkShowInBoard.Checked)
                chkShowInBoard.Checked = false;
        }

        private void ApplyAutoPlayColorAvailability()
        {
            ControlCenterRuntimeSnapshot controlCenter = controlCenterRuntime == null
                ? null
                : controlCenterRuntime.Snapshot;
            bool autoPlayEnabled = controlCenter == null
                ? false
                : controlCenter.AutoPlayEnabled;
            bool foxAutoEnabled = controlCenter != null && controlCenter.FoxAutoColorEnabled;
            radioBlack.Enabled = controlCenter != null && controlCenter.ManualColorEnabled;
            radioWhite.Enabled = controlCenter != null && controlCenter.ManualColorEnabled;
            radioAutoPlayColor.Enabled = foxAutoEnabled;
            btnFoxAutoPlayIdentity.Enabled = IsFoxSyncType(CurrentSyncType);
            if (!autoPlayEnabled)
                UpdateAutoPlayColorStatus(null);
        }

        private void SetSyncConfigurationControlsEnabled(bool enabled)
        {
            if (!enabled)
            {
                rdoFox.Enabled = false;
                rdoFoxBack.Enabled = false;
                rdoYike.Enabled = false;
                rdoTygem.Enabled = false;
                rdoBack.Enabled = false;
                rdoSina.Enabled = false;
                rdo19x19.Enabled = false;
                rdo13x13.Enabled = false;
                rdo9x9.Enabled = false;
                rdoOtherBoard.Enabled = false;
                rdoFore.Enabled = false;
                txtBoardWidth.Enabled = false;
                txtBoardHeight.Enabled = false;
                return;
            }

            ApplyControlCenterNativeEnablement();
        }

        private void ApplyControlCenterNativeEnablement()
        {
            ControlCenterRuntimeSnapshot controlCenter = controlCenterRuntime == null
                ? null
                : controlCenterRuntime.Snapshot;
            bool configurationEnabled = controlCenter == null || controlCenter.ConfigurationEnabled;
            bool manualSelectionEnabled = configurationEnabled
                && (controlCenter == null
                    ? UsesManualSelectionType(CurrentSyncType)
                    : ControlCenterPreferences.UsesManualSelection(controlCenter.Platform));
            bool customBoardDimensionsEnabled = controlCenter == null
                ? manualSelectionEnabled && rdoOtherBoard.Checked
                : controlCenter.CustomBoardDimensionsEnabled;
            bool customBoardSizeEnabled = controlCenter == null
                ? manualSelectionEnabled
                : controlCenter.CustomBoardSizeEnabled;
            bool twoWaySyncEnabled = controlCenter == null
                || controlCenter.TwoWaySyncEnabled;
            bool showOnBoardEnabled = controlCenter == null
                || controlCenter.ShowOnBoardEnabled;

            rdoFox.Enabled = configurationEnabled;
            rdoFoxBack.Enabled = configurationEnabled;
            rdoYike.Enabled = configurationEnabled;
            rdoTygem.Enabled = configurationEnabled;
            rdoBack.Enabled = configurationEnabled;
            rdoSina.Enabled = configurationEnabled;
            rdo19x19.Enabled = configurationEnabled;
            rdo13x13.Enabled = configurationEnabled;
            rdo9x9.Enabled = configurationEnabled;
            rdoOtherBoard.Enabled = customBoardSizeEnabled;
            rdoFore.Enabled = configurationEnabled;
            txtBoardWidth.Enabled = customBoardDimensionsEnabled;
            txtBoardHeight.Enabled = customBoardDimensionsEnabled;
            chkBothSync.Enabled = twoWaySyncEnabled;
            chkShowInBoard.Enabled = showOnBoardEnabled;
            if (controlCenter != null)
            {
                chkAutoPlay.Enabled = controlCenter.AutoPlayToggleEnabled;
                radioBlack.Enabled = controlCenter.ManualColorEnabled;
                radioWhite.Enabled = controlCenter.ManualColorEnabled;
                radioAutoPlayColor.Enabled = controlCenter.FoxAutoColorEnabled;
                radioAutoPlayMoveFirst.Enabled = controlCenter.MoveModeEnabled;
                radioAutoPlayMoveGma.Enabled = controlCenter.MoveModeEnabled;
                textBox1.Enabled = controlCenter.AiTimeEnabled;
                textBox2.Enabled = controlCenter.PlayoutsEnabled;
                textBox3.Enabled = controlCenter.FirstPolicyEnabled;
                btnFastSync.Enabled = controlCenter.QuickSyncEnabled;
                btnKeepSync.Enabled = controlCenter.ContinuousSyncEnabled;
                btnOneTimeSync.Enabled = controlCenter.OneTimeSyncEnabled;
                btnClickBoard.Enabled = controlCenter.BoardSelectionInsideEnabled;
                btnCircleBoard.Enabled = controlCenter.BoardSelectionRectangleEnabled;
                btnCircleRow1.Enabled = controlCenter.BoardSelectionLine1Enabled;
            }
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

        private static string GetProtocolNumericValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "0" : value;
        }

        private void SendPlayCommandIfSelected()
        {
            ControlCenterRuntimeSnapshot controlCenter = controlCenterRuntime.Snapshot;
            if (!controlCenter.TwoWaySync || !controlCenter.AutoPlayEnabled)
                return;
            FoxWindowContext foxWindowContext = controlCenter.AutoPlayColorMode == AutoPlayColorMode.FoxAuto
                ? ResolveFoxWindowContext()
                : FoxWindowContext.Unknown();
            ResolveCurrentAutoPlayColor(foxWindowContext);
            controlCenter = controlCenterRuntime.Snapshot;
            if (!controlCenter.AutoPlayColorResolution.IsKnown)
                return;

            sessionCoordinator.SendPlay(
                controlCenter.PlayColor,
                GetProtocolNumericValue(controlCenter.AiTimeValue),
                GetProtocolNumericValue(controlCenter.PlayoutsValue),
                GetProtocolNumericValue(controlCenter.FirstPolicyValue),
                controlCenter.AutoPlayMoveMode);
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
            return CurrentSyncType == TYPE_FOX;
        }

        private void SendForegroundFoxInBoardCommand(bool enabled)
        {
            sessionCoordinator.SendForegroundFoxInBoard(enabled);
        }

        private void SendBothSyncStateChange()
        {
            ControlCenterPreferences preferences = controlCenterRuntime.CurrentPreferences;
            SendBothSyncCommand(preferences.TwoWaySync);
            if (preferences.ShowOnBoard && CanUseForegroundFoxInBoardProtocol())
                SendForegroundFoxInBoardCommand(preferences.TwoWaySync);
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
            sessionCoordinator.StopSyncSessionAndClearBoard();
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
            sessionCoordinator.SendTimeChanged(
                GetProtocolNumericValue(controlCenterRuntime.CurrentSessionState.AiTimeValue));
        }

        private void SendPlayoutsChangedCommand()
        {
            sessionCoordinator.SendPlayoutsChanged(
                GetProtocolNumericValue(controlCenterRuntime.CurrentSessionState.PlayoutsValue));
        }

        private void SendFirstPolicyChangedCommand()
        {
            sessionCoordinator.SendFirstPolicyChanged(
                GetProtocolNumericValue(controlCenterRuntime.CurrentSessionState.FirstPolicyValue));
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
                case TYPE_YIKE:
                    return SyncMode.Yike;
                default:
                    return SyncMode.Fox;
            }
        }

        private BoardDimensions CreateCurrentBoardSize()
        {
            if (controlCenterRuntime == null)
                return new BoardDimensions(19, 19);

            ControlCenterPreferences preferences = controlCenterRuntime.CurrentPreferences;
            return new BoardDimensions(preferences.BoardWidth, preferences.BoardHeight);
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

        private AutoPlayColorMode GetSelectedAutoPlayColorMode()
        {
            return controlCenterRuntime.CurrentPreferences.AutoPlayColorMode;
        }

        private AutoPlayMoveMode GetSelectedAutoPlayMoveMode()
        {
            return controlCenterRuntime.CurrentPreferences.AutoPlayMoveMode;
        }

        private void ApplyAutoPlayColorMode(AutoPlayColorMode mode)
        {
            suppressAutoPlayColorModeEvents = true;
            try
            {
                radioBlack.Checked = mode == AutoPlayColorMode.ManualBlack;
                radioWhite.Checked = mode == AutoPlayColorMode.ManualWhite;
                radioAutoPlayColor.Checked = mode == AutoPlayColorMode.FoxAuto;
                if (mode == AutoPlayColorMode.ManualBlack || mode == AutoPlayColorMode.ManualWhite)
                    lastManualAutoPlayColorMode = mode;
            }
            finally
            {
                suppressAutoPlayColorModeEvents = false;
            }

            if (mode != AutoPlayColorMode.FoxAuto)
            {
                ClearFoxAutoPlayColorDetectionState();
                UpdateAutoPlayColorStatus(null);
            }
        }

        private void ApplyAutoPlayMoveMode(AutoPlayMoveMode mode)
        {
            suppressAutoPlayMoveModeEvents = true;
            try
            {
                radioAutoPlayMoveFirst.Checked = mode == AutoPlayMoveMode.FirstCandidate;
                radioAutoPlayMoveGma.Checked = mode == AutoPlayMoveMode.GenmoveAnalyze;
                if (!radioAutoPlayMoveFirst.Checked && !radioAutoPlayMoveGma.Checked)
                    radioAutoPlayMoveFirst.Checked = true;
            }
            finally
            {
                suppressAutoPlayMoveModeEvents = false;
            }
            ApplyAutoPlayMoveModeControlState();
        }

        private void ApplyAutoPlayMoveModeControlState()
        {
            ControlCenterRuntimeSnapshot controlCenter = controlCenterRuntime == null
                ? null
                : controlCenterRuntime.Snapshot;
            if (controlCenter == null)
            {
                radioAutoPlayMoveFirst.Enabled = false;
                radioAutoPlayMoveGma.Enabled = false;
                textBox3.Enabled = false;
                return;
            }

            radioAutoPlayMoveFirst.Enabled = controlCenter.MoveModeEnabled;
            radioAutoPlayMoveGma.Enabled = controlCenter.MoveModeEnabled;
            textBox3.Enabled = controlCenter.FirstPolicyEnabled;
        }

        private AutoPlayColorResolution ResolveCurrentAutoPlayColor(FoxWindowContext foxWindowContext)
        {
            ControlCenterRuntimeSnapshot controlCenter = controlCenterRuntime.Snapshot;
            if (!controlCenter.AutoPlayEnabled)
            {
                UpdateAutoPlayColorStatus(null);
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);
            }

            AutoPlayColorResolution detected = controlCenter.AutoPlayColorMode == AutoPlayColorMode.FoxAuto
                ? ResolveDetectedFoxAutoPlayColor(foxWindowContext)
                : null;
            controlCenterRuntime.UpdateAutoPlayObservation(
                ResolveCurrentFoxAutoPlayNicknameSignature(),
                foxWindowContext,
                detected);
            AutoPlayColorResolution resolution = controlCenterRuntime.Snapshot.AutoPlayColorResolution;
            UpdateAutoPlayColorStatus(resolution);
            return resolution;
        }

        private AutoPlayColorResolution ResolveDetectedFoxAutoPlayColor(FoxWindowContext foxWindowContext)
        {
            string nicknameSignature = ResolveCurrentFoxAutoPlayNicknameSignature();
            if (!IsFoxSyncType(CurrentSyncType)
                || hwnd == IntPtr.Zero
                || string.IsNullOrWhiteSpace(nicknameSignature))
                return null;

            IntPtr captureHandle = ResolveFoxAutoPlayCaptureHandle(hwnd);
            if (captureHandle == IntPtr.Zero)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.NicknameNotMatched);

            string contextSignature = BuildFoxAutoPlayColorDetectionContextSignature(foxWindowContext);
            DateTime now = DateTime.UtcNow;
            if (lastFoxAutoPlayColorDetection != null
                && lastFoxAutoPlayColorDetectionWindowHandle == captureHandle
                && string.Equals(lastFoxAutoPlayColorDetectionContextSignature, contextSignature, StringComparison.Ordinal)
                && string.Equals(lastFoxAutoPlayColorDetectionNicknameSignature, nicknameSignature, StringComparison.Ordinal)
                && (now - lastFoxAutoPlayColorDetectionTimestampUtc).TotalMilliseconds < FoxAutoPlayColorDetectionCacheMs)
                return lastFoxAutoPlayColorDetection;

            AutoPlayColorResolution detection;
            using (Bitmap bitmap = foxAutoPlayCapturePlatform.CaptureWindow(captureHandle))
            {
                detection = FoxAutoPlayColorDetector.DetectPlayerListPanel(bitmap, nicknameSignature);
            }

            lastFoxAutoPlayColorDetection = detection;
            lastFoxAutoPlayColorDetectionWindowHandle = captureHandle;
            lastFoxAutoPlayColorDetectionContextSignature = contextSignature;
            lastFoxAutoPlayColorDetectionNicknameSignature = nicknameSignature;
            lastFoxAutoPlayColorDetectionTimestampUtc = now;
            return detection;
        }

        private static string BuildFoxAutoPlayColorDetectionContextSignature(FoxWindowContext context)
        {
            if (context == null)
                return string.Empty;

            if (context.Kind == FoxWindowKind.LiveRoom)
            {
                return "live|state=" + (int)context.LiveRoomState
                    + "|room=" + (context.RoomToken ?? string.Empty).Trim();
            }

            if (context.Kind == FoxWindowKind.RecordView)
            {
                return "record|current=" + FormatNullableInt(context.RecordCurrentMove)
                    + "|total=" + FormatNullableInt(context.RecordTotalMove)
                    + "|end=" + (context.RecordAtEnd ? "1" : "0")
                    + "|fingerprint=" + (context.TitleFingerprint ?? string.Empty).Trim();
            }

            return "kind=" + (int)context.Kind;
        }

        private static string FormatNullableInt(int? value)
        {
            return value.HasValue ? value.Value.ToString() : string.Empty;
        }

        private string ResolveCurrentFoxAutoPlayNicknameSignature()
        {
            string runtimeSignature = controlCenterRuntime == null
                ? string.Empty
                : controlCenterRuntime.CurrentSessionState.FoxAutoPlayNicknameSignature;
            if (!string.IsNullOrWhiteSpace(runtimeSignature))
                return runtimeSignature;
            if (!string.IsNullOrWhiteSpace(currentFoxAutoPlayNicknameSignature))
                return currentFoxAutoPlayNicknameSignature;
            return Program.CurrentContext.Config.FoxAutoPlayNicknameSignature;
        }

        private void UpdateAutoPlayColorStatus(AutoPlayColorResolution resolution)
        {
            ControlCenterRuntimeSnapshot controlCenter = controlCenterRuntime == null
                ? null
                : controlCenterRuntime.Snapshot;
            if (controlCenter == null
                || controlCenter.AutoPlayColorMode != AutoPlayColorMode.FoxAuto
                || resolution == null)
            {
                SetAutoPlayColorStatusText(string.Empty);
                return;
            }

            switch (resolution.Status)
            {
                case AutoPlayColorStatus.Unconfigured:
                    SetAutoPlayColorStatusText(getLangStr("MainForm_autoPlayColorStatusUnconfigured"));
                    return;
                case AutoPlayColorStatus.RecognizedBlack:
                    SetAutoPlayColorStatusText(getLangStr("MainForm_autoPlayColorStatusBlack"));
                    return;
                case AutoPlayColorStatus.RecognizedWhite:
                    SetAutoPlayColorStatusText(getLangStr("MainForm_autoPlayColorStatusWhite"));
                    return;
                case AutoPlayColorStatus.UnsupportedPlatform:
                    SetAutoPlayColorStatusText(getLangStr("MainForm_autoPlayColorStatusUnsupported"));
                    return;
                case AutoPlayColorStatus.Spectating:
                    SetAutoPlayColorStatusText(getLangStr("MainForm_autoPlayColorStatusSpectating"));
                    return;
                default:
                    SetAutoPlayColorStatusText(getLangStr("MainForm_autoPlayColorStatusWaiting"));
                    return;
            }
        }

        private void SetAutoPlayColorStatusText(string text)
        {
            if (string.Equals(lblAutoPlayColorStatus.Text, text, StringComparison.Ordinal))
                return;

            lblAutoPlayColorStatus.Text = text;
        }

        private void ClearSavedFoxAutoPlayIdentity()
        {
            AppConfig updatedConfig = Program.CurrentConfig.Clone();
            updatedConfig.FoxAutoPlayNickname = string.Empty;
            updatedConfig.FoxAutoPlayNicknameSignature = string.Empty;
            Program.SaveAppConfig(updatedConfig);
            if (string.IsNullOrWhiteSpace(currentFoxAutoPlayNicknameSignature))
            {
                controlCenterRuntime.UpdateAutoPlayObservation(
                    string.Empty,
                    ResolveFoxWindowContext(),
                    null);
            }
        }

        private IntPtr ResolveFoxAutoPlayIdentityBoardHandle()
        {
            if (!IsFoxSyncType(CurrentSyncType))
                return IntPtr.Zero;
            if (hwnd != IntPtr.Zero && IsWindow(hwnd))
                return hwnd;
            return new LegacySyncWindowLocator().FindWindowHandle(GetCurrentSyncMode());
        }

        private IntPtr ResolveFoxAutoPlayCaptureHandle(IntPtr boardHandle)
        {
            return FindFoxPlayerListPanelHandle(boardHandle);
        }

        private static IntPtr FindFoxPlayerListPanelHandle(IntPtr boardHandle)
        {
            if (boardHandle == IntPtr.Zero || !IsWindow(boardHandle))
                return IntPtr.Zero;

            IntPtr rootHandle = boardHandle;
            IntPtr parent = GetParent(rootHandle);
            while (parent != IntPtr.Zero)
            {
                rootHandle = parent;
                parent = GetParent(rootHandle);
            }

            IntPtr playerListHandle = IntPtr.Zero;
            EnumChildWindows(rootHandle, delegate(IntPtr childHandle, IntPtr parameter)
            {
                if (!IsWindowVisible(childHandle))
                    return true;
                if (!string.Equals(GetWindowText(childHandle), "CRoomPlayerListPanel", StringComparison.Ordinal))
                    return true;

                playerListHandle = childHandle;
                return false;
            }, IntPtr.Zero);
            return playerListHandle;
        }

        private static string GetWindowText(IntPtr handle)
        {
            StringBuilder builder = new StringBuilder(256);
            GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private static Bitmap CropBitmap(Bitmap source, PixelRect bounds)
        {
            if (source == null || bounds == null || bounds.IsEmpty)
                return null;

            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, bounds.Width, bounds.Height),
                    new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    GraphicsUnit.Pixel);
            }
            return bitmap;
        }

        private void ClearFoxAutoPlayColorDetectionState()
        {
            lastFoxAutoPlayColorDetection = null;
            lastFoxAutoPlayColorDetectionWindowHandle = IntPtr.Zero;
            lastFoxAutoPlayColorDetectionContextSignature = string.Empty;
            lastFoxAutoPlayColorDetectionNicknameSignature = string.Empty;
            lastFoxAutoPlayColorDetectionTimestampUtc = DateTime.MinValue;
            if (controlCenterRuntime != null)
                controlCenterRuntime.ClearAutoPlayObservation();
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
            ControlCenterPreferences controlCenter = controlCenterRuntime.CurrentPreferences;
            SyncMode syncMode = controlCenter.Platform;
            string syncPlatform = ResolveSyncPlatform(syncMode);
            FoxWindowContext foxWindowContext = ResolveFoxWindowContext();
            int? foxMoveNumber = foxWindowContext.ResolveDisplayedMoveNumber();
            UpdateMainWindowTitle(foxWindowContext);
            AutoPlayColorResolution autoPlayColor = ResolveCurrentAutoPlayColor(foxWindowContext);
            ControlCenterRuntimeSnapshot runtimeSnapshot = controlCenterRuntime.Snapshot;

            SyncCoordinatorHostSnapshot snapshot = new SyncCoordinatorHostSnapshot
            {
                SyncMode = syncMode,
                BoardWidth = controlCenter.BoardWidth,
                BoardHeight = controlCenter.BoardHeight,
                SelectionBounds = BuildCaptureSelectionBounds(),
                SelectedWindowHandle = hwnd,
                DpiScale = factor,
                LegacyTypeToken = ((int)syncMode).ToString(),
                ShowInBoard = controlCenter.ShowOnBoard,
                SupportsForegroundFoxInBoardProtocol = CanUseForegroundFoxInBoardProtocol(),
                AutoMinimize = Program.autoMin,
                SampleIntervalMs = Program.timeinterval,
                UseEnhancedCapture = Program.useEnhanceScreen,
                FoxMoveNumber = foxMoveNumber,
                PlayColor = autoPlayColor.PlayColor,
                AiTimeValue = runtimeSnapshot.AiTimeValue,
                PlayoutsValue = runtimeSnapshot.PlayoutsValue,
                FirstPolicyValue = runtimeSnapshot.FirstPolicyValue,
                AutoPlayMoveMode = runtimeSnapshot.AutoPlayMoveMode
            };

            sessionCoordinator.SetSyncPlatform(syncPlatform);
            sessionCoordinator.SetFoxWindowContext(foxWindowContext);
            UpdateCapturedFoxMoveNumber(snapshot.FoxMoveNumber);
            return snapshot;
        }

        private static string ResolveSyncPlatform(SyncMode syncMode)
        {
            if (syncMode == SyncMode.Fox || syncMode == SyncMode.FoxBackgroundPlace)
                return "fox";
            if (syncMode == SyncMode.Yike)
                return ProtocolKeywords.Yike;
            return "generic";
        }

        private YikeWindowContext ResolveYikeWindowContext()
        {
            if (CurrentSyncType != TYPE_YIKE)
                return YikeWindowContext.Unknown();
            return YikeWindowContext.CopyOf(lastYikeWindowContext);
        }

        private void ClearYikeContext()
        {
            lastYikeWindowContext = YikeWindowContext.Unknown();
            lastYikeContextWindowHandle = IntPtr.Zero;
            sessionCoordinator.SetYikeContext(lastYikeWindowContext);
            sessionCoordinator.SetYikeGeometry(null);
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
            ClearFoxAutoPlayColorDetectionState();
        }

        private void UpdateMainWindowTitle(FoxWindowContext foxWindowContext)
        {
            string previousContextSignature = BuildFoxAutoPlayColorDetectionContextSignature(lastFoxWindowContext);
            string nextContextSignature = BuildFoxAutoPlayColorDetectionContextSignature(foxWindowContext);
            if (!string.Equals(previousContextSignature, nextContextSignature, StringComparison.Ordinal))
            {
                ClearFoxAutoPlayColorDetectionState();
            }
            lastFoxWindowContext = FoxWindowContext.CopyOf(foxWindowContext);
            ApplyMainWindowTitle();
            if (controlCenterRuntime != null)
            {
                ApplyControlCenterSessionObservation(
                    new ControlCenterSessionObservation(
                        controlCenterRuntime.CaptureSessionObservationGeneration())
                        .WithFoxWindowContext(lastFoxWindowContext));
            }
        }

        private void RefreshMainWindowTitleFromCurrentWindow()
        {
            UpdateMainWindowTitle(ResolveFoxWindowContext());
        }

        private void ResetMainWindowTitle()
        {
            hasRetainedFoxTitleSnapshot = false;
            lastMainWindowTitleTurn = MainWindowTitleTurn.None;
            lastFoxWindowContext = FoxWindowContext.Unknown();
            if (CurrentSyncType != TYPE_YIKE)
                lastYikeWindowContext = YikeWindowContext.Unknown();
            if (CurrentSyncType != TYPE_YIKE || lastYikeContextWindowHandle != hwnd)
                lastYikeContextWindowHandle = IntPtr.Zero;
            InvalidateFoxWindowBinding();
            ApplyMainWindowTitle();
            if (controlCenterRuntime != null)
            {
                ApplyControlCenterSessionObservation(
                    new ControlCenterSessionObservation(
                        controlCenterRuntime.CaptureSessionObservationGeneration())
                        .WithTargetWindowValid(
                            hwnd == IntPtr.Zero ? (bool?)null : IsWindow(hwnd))
                        .WithBoardRegion(false, false)
                        .WithFoxWindowContext(lastFoxWindowContext)
                        .WithYikeWindowContext(lastYikeWindowContext)
                        .WithTitleTurn(lastMainWindowTitleTurn));
            }
        }

        private MainWindowTitleDisplayMode ResolveMainWindowTitleDisplayMode()
        {
            if (isShuttingDown || (!IsFoxSyncType(CurrentSyncType) && CurrentSyncType != TYPE_YIKE))
                return MainWindowTitleDisplayMode.Hidden;
            if (HasActiveSyncOperation())
                return MainWindowTitleDisplayMode.Syncing;
            if (hasRetainedFoxTitleSnapshot)
                return MainWindowTitleDisplayMode.RetainedSnapshot;
            return MainWindowTitleDisplayMode.Hidden;
        }

        private void ApplyMainWindowTitle()
        {
            string baseTitle = MainWindowTitleFormatter.FormatBaseTitle(
                getLangStr("MainForm_title"),
                AppReleaseVersion.GetCurrentVersion(),
                lastMainWindowTitleTurn);

            if (CurrentSyncType == TYPE_YIKE)
            {
                YikeWindowContext yikeWindowContext = ResolveYikeWindowContext();
                string yikeTitle = MainWindowTitleFormatter.FormatYike(
                    baseTitle,
                    ResolveMainWindowTitleDisplayMode(),
                    IsSelectedYikeWindowHandleValid(),
                    yikeWindowContext,
                    getLangStr("MainForm_titleTagYike"),
                    "号",
                    getLangStr("MainForm_titleMoveFormatSingle"),
                    getLangStr("MainForm_titleTagTitleMissing"),
                    getLangStr("MainForm_titleTagSyncing"));
                ApplyMainWindowTitleText(yikeTitle);
                return;
            }

            string title = MainWindowTitleFormatter.Format(
                baseTitle,
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
            ApplyMainWindowTitleText(title);
        }

        private void ApplyMainWindowTitleText(string title)
        {
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

        long ISyncCoordinatorHost.AllocateSessionObservationGeneration()
        {
            return controlCenterRuntime.BeginSessionObservationGeneration();
        }

        void ISyncCoordinatorHost.UpdateSelectedWindowHandle(
            IntPtr handle,
            long observationGeneration)
        {
            bool? targetWindowValid = handle == IntPtr.Zero
                ? (bool?)null
                : IsWindow(handle);
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(
                observationGeneration)
                .WithTargetWindowValid(targetWindowValid)
                .WithBoardRegion(false, false)
                .WithFoxWindowContext(FoxWindowContext.Unknown())
                .WithYikeWindowContext(YikeWindowContext.Unknown())
                .WithTitleTurn(MainWindowTitleTurn.None);
            InvokeUiHostAction(delegate
            {
                RunWithSuppressedWebViewStatePublication(delegate
                {
                    ControlCenterSessionObservationApplyResult result =
                        ApplyControlCenterSessionObservation(observation);
                    if (result.Outcome != ControlCenterSessionObservationApplyOutcome.Applied)
                        return;
                    SetSelectedWindowHandle(handle);
                    hasRetainedFoxTitleSnapshot = false;
                    lastMainWindowTitleTurn = MainWindowTitleTurn.None;
                    lastFoxWindowContext = FoxWindowContext.Unknown();
                    InvalidateFoxWindowBinding();
                    if (HasActiveSyncOperation())
                    {
                        RefreshMainWindowTitleFromCurrentWindow();
                        return;
                    }
                    ApplyMainWindowTitle();
                });
            });
        }

        private void SetSelectedWindowHandle(IntPtr handle)
        {
            if (CurrentSyncType == TYPE_YIKE && hwnd != handle)
                ClearYikeContext();
            if (hwnd != handle)
            {
                ClearFoxAutoPlayColorDetectionState();
            }
            hwnd = handle;
        }

        private bool IsSelectedYikeWindowHandleValid()
        {
            return hwnd != IntPtr.Zero && IsWindow(hwnd);
        }

        void ISyncCoordinatorHost.OnKeepSyncStarted(long observationGeneration)
        {
            bool quickSyncActive = sessionCoordinator.IsContinuousSyncing;
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(
                observationGeneration)
                .WithSyncActivity(quickSyncActive, !quickSyncActive);
            if (!quickSyncActive)
                observation = observation.WithSemanticLog("SYNC", "WebView_continuousSyncStarted");
            InvokeUiHostAction(delegate
            {
                RunWithSuppressedWebViewStatePublication(delegate
                {
                    ControlCenterSessionObservationApplyResult result =
                        ApplyControlCenterSessionObservation(observation);
                    if (result.Outcome != ControlCenterSessionObservationApplyOutcome.Applied)
                        return;
                    ApplyKeepSyncStartedUi();
                });
            });
        }

        void ISyncCoordinatorHost.OnKeepSyncStopped(
            bool continuousSyncActive,
            long observationGeneration)
        {
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(
                observationGeneration)
                .WithSyncActivity(continuousSyncActive, false);
            if (!continuousSyncActive)
                observation = observation.WithSemanticLog("SYNC", "WebView_continuousSyncStopped");
            InvokeUiHostAction(delegate
            {
                RunWithSuppressedWebViewStatePublication(
                    delegate
                    {
                        ControlCenterSessionObservationApplyResult result =
                            ApplyControlCenterSessionObservation(observation);
                        if (result.Outcome != ControlCenterSessionObservationApplyOutcome.Applied)
                            return;
                        ApplyKeepSyncStoppedUi(continuousSyncActive);
                    });
            });
        }

        void ISyncCoordinatorHost.OnContinuousSyncStarted(long observationGeneration)
        {
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(
                observationGeneration)
                .WithSyncActivity(true, false)
                .WithSemanticLog("SYNC", "WebView_quickSyncStarted");
            InvokeUiHostAction(delegate
            {
                RunWithSuppressedWebViewStatePublication(delegate
                {
                    ControlCenterSessionObservationApplyResult result =
                        ApplyControlCenterSessionObservation(observation);
                    if (result.Outcome != ControlCenterSessionObservationApplyOutcome.Applied)
                        return;
                    ApplyContinuousSyncStartedUi();
                });
            });
        }

        void ISyncCoordinatorHost.OnContinuousSyncStopped(long observationGeneration)
        {
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(
                observationGeneration)
                .WithSyncActivity(false, sessionCoordinator.StartedSync)
                .WithSemanticLog("SYNC", "WebView_quickSyncStopped");
            InvokeUiHostAction(delegate
            {
                RunWithSuppressedWebViewStatePublication(delegate
                {
                    ControlCenterSessionObservationApplyResult result =
                        ApplyControlCenterSessionObservation(observation);
                    if (result.Outcome != ControlCenterSessionObservationApplyOutcome.Applied)
                        return;
                    ApplyContinuousSyncStoppedUi();
                });
            });
        }

        void ISyncCoordinatorHost.OnSyncCachesReset(long observationGeneration)
        {
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(
                observationGeneration)
                .WithTitleTurn(MainWindowTitleTurn.None);
            InvokeUiHostAction(delegate
            {
                RunWithSuppressedWebViewStatePublication(delegate
                {
                    ControlCenterSessionObservationApplyResult result =
                        ApplyControlCenterSessionObservation(observation);
                    if (result.Outcome != ControlCenterSessionObservationApplyOutcome.Applied)
                        return;
                    lastMainWindowTitleTurn = MainWindowTitleTurn.None;
                    ApplyMainWindowTitle();
                });
            });
        }

        void IWebViewSyncCoordinatorHost.OnRuntimeFrameCleared(long observationGeneration)
        {
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(
                observationGeneration)
                .ClearRuntimeFrame();
            InvokeUiHostAction(delegate
            {
                ApplyControlCenterSessionObservation(observation);
            });
        }

        void IWebViewSyncCoordinatorHost.OnBoardFrameRecognized(
            BoardFrame frame,
            int boardPixelWidth,
            int boardPixelHeight,
            bool placementRegionResolved,
            long observationGeneration)
        {
            bool boardRegionRecognized = IsBoardRegionRecognized(
                frame,
                boardPixelWidth,
                boardPixelHeight);
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(
                observationGeneration)
                .WithBoardRegion(
                    boardRegionRecognized,
                    boardRegionRecognized && placementRegionResolved);
            InvokeUiHostAction(delegate
            {
                ApplyControlCenterSessionObservation(observation);
            });
        }

        void IWebViewSyncCoordinatorHost.OnBoardSnapshotSent(
            BoardSnapshot snapshot,
            long observationGeneration)
        {
            if (snapshot == null || !snapshot.IsValid)
                return;
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(
                observationGeneration)
                .WithSemanticLog("SYNC", "WebView_boardSent");
            InvokeUiHostAction(delegate
            {
                ApplyControlCenterSessionObservation(observation);
            });
        }

        void ISyncCoordinatorHost.OnBoardSnapshotRecognized(
            BoardSnapshot snapshot,
            TimeSpan duration,
            long observationGeneration)
        {
            if (snapshot == null || !snapshot.IsValid)
                return;
            MainWindowTitleTurn titleTurn = ResolveMainWindowTitleTurn(snapshot);
            ControlCenterSessionObservation observation = new ControlCenterSessionObservation(
                observationGeneration)
                .WithTitleTurn(titleTurn)
                    .WithRecentSync(
                        DateTime.Now.ToString("HH:mm:ss"),
                        snapshot.BlackStoneCount + snapshot.WhiteStoneCount,
                        FormatWebViewDuration(duration));
            InvokeUiHostAction(delegate
            {
                RunWithSuppressedWebViewStatePublication(delegate
                {
                    ControlCenterSessionObservationApplyResult result =
                        ApplyControlCenterSessionObservation(observation);
                    if (result.Outcome != ControlCenterSessionObservationApplyOutcome.Applied)
                        return;
                    lastMainWindowTitleTurn = titleTurn;
                    ApplyMainWindowTitle();
                });
            });
        }

        void ISyncCoordinatorHost.ShowMissingSyncSourceMessage()
        {
            InvokeUiHostAction(delegate
            {
                ShowWebViewMessage("WebView_syncFailedTitle", "noSelectedBoardAndFailed");
            });
        }

        void ISyncCoordinatorHost.ShowRecognitionFailureMessage()
        {
            InvokeUiHostAction(delegate
            {
                ShowWebViewMessage("WebView_recognitionFailedTitle", "recgnizeFaild");
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
            if (lastMainWindowTitleTurn == MainWindowTitleTurn.None)
                lastMainWindowTitleTurn = MainWindowTitleTurn.Unknown;
            RefreshMainWindowTitleFromCurrentWindow();
        }

        private static MainWindowTitleTurn ResolveMainWindowTitleTurn(BoardSnapshot snapshot)
        {
            if (snapshot == null || snapshot.BoardState == null)
                return MainWindowTitleTurn.Unknown;

            int blackLastMoveCount = 0;
            int whiteLastMoveCount = 0;
            for (int i = 0; i < snapshot.BoardState.Length; i++)
            {
                if (snapshot.BoardState[i] == BoardCellState.BlackLastMove)
                    blackLastMoveCount++;
                else if (snapshot.BoardState[i] == BoardCellState.WhiteLastMove)
                    whiteLastMoveCount++;
            }

            if (blackLastMoveCount == 1 && whiteLastMoveCount == 0)
                return MainWindowTitleTurn.White;
            if (whiteLastMoveCount == 1 && blackLastMoveCount == 0)
                return MainWindowTitleTurn.Black;
            return MainWindowTitleTurn.Unknown;
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
            lastMainWindowTitleTurn = MainWindowTitleTurn.Unknown;
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

        internal bool IsShutdownRequested
        {
            get { return isShuttingDown; }
        }

        internal bool HostedUpdateSupported
        {
            get { return hostedUpdateSupported; }
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
            this.hostedUpdateJourney = new HostedUpdateJourney(
                new HostedUpdatePackageDownloader(),
                new HostedUpdatePackageVerifier(),
                delegate(string tag, string zipPath)
                {
                    return this.sessionCoordinator.SendReadboardUpdateReady(tag, zipPath);
                },
                new HostedUpdateResponseTimeoutScheduler(),
                OnHostedUpdateObservation);
            InitializeComponent();
            this.controlCenterRuntime = new ControlCenterRuntime(
                ControlCenterPreferences.FromConfig(Program.CurrentConfig),
                ControlCenterSessionState.FromLaunchOptions(launchOptions),
                new MainFormControlCenterSessionAdapter(this),
                new AppConfigControlCenterPreferencePersistence(
                    delegate { return Program.CurrentConfig; },
                    Program.SaveAppConfig),
                new MainFormControlCenterActionAdapter(this));
            this.controlCenterRuntime.UpdateAutoPlayObservation(
                Program.CurrentConfig.FoxAutoPlayNicknameSignature,
                FoxWindowContext.Unknown(),
                null);
            using (System.Drawing.Bitmap bitmap = new Bitmap(1, 1))
            using (System.Drawing.Graphics graphics2 = Graphics.FromImage(bitmap))
            {
                factor = graphics2.DpiX / 96;
            }
            if (factor > 1.0f)
            {
                Program.isScaled = true;
            }
            ApplyLoadedConfiguration();
            this.MaximizeBox = false;
            radioWhite.Enabled = false;
            radioBlack.Enabled = false;
            radioAutoPlayColor.Enabled = false;
            radioAutoPlayMoveFirst.Enabled = false;
            radioAutoPlayMoveGma.Enabled = false;
            btnFoxAutoPlayIdentity.Enabled = false;
            textBox1.Enabled = false;
            textBox2.Enabled = false;
            textBox3.Enabled = false;
            if (controlCenterRuntime.CurrentPreferences.TwoWaySync)
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
            this.rdoYike.Text = getLangStr("MainForm_rdoYike");
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
            this.radioAutoPlayColor.Text = getLangStr("MainForm_radioAutoPlayColor");
            this.btnFoxAutoPlayIdentity.Text = getLangStr("MainForm_btnFoxAutoPlayIdentity");
            this.lblAutoPlayColorStatus.Text = string.Empty;
            this.lblAutoPlayMoveMode.Text = getLangStr("MainForm_lblAutoPlayMoveMode");
            this.radioAutoPlayMoveFirst.Text = getLangStr("MainForm_radioAutoPlayMoveFirst");
            this.radioAutoPlayMoveGma.Text = getLangStr("MainForm_radioAutoPlayMoveGma");
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
            InitializeWebViewShell();
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

        private void btnCheckUpdate_Click(object sender, EventArgs e)
        {
            _ = CheckForWebViewUpdateAsync();
        }

        public void sendPonderStatus()
        {
            SendPonderStatusCommand();
        }

        GlobalMouseHook mouseHook;
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

            mouseHook = new GlobalMouseHook();

            mouseHook.MouseMove += mh_MouseMoveEvent;
            mouseHook.MouseClick += mh_MouseMoveEvent2;
            mouseHook.Enabled = false;
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
                ShowWebViewMessage("WebView_recognitionFailedTitle", "recgnizeFaild");
                RestoreMainWindowAfterSelection();
            }
            else if (CurrentSyncType == TYPE_BACKGROUND)
                BeginResolveBackgroundSelectionWindowAsync();
            else
                RestoreMainWindowAfterSelection();
            //mouseHook.Enabled = false;
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
            BoardDimensions boardSize = CreateCurrentBoardSize();
            int gapX = (int)Math.Round((ox2 - selectionX1) / ((boardSize.Width - 1) * 2f));
            int gapY = (int)Math.Round((oy2 - selectionY1) / ((boardSize.Height - 1) * 2f));
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
                    SetSelectedWindowHandle(handle);
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
                //     mouseHook.Enabled = false;
                clicked = false;
                SetSelectedWindowHandle(getMousePointHwnd());
                ResetMainWindowTitle();
            }


        }

        private void Form1_MouseClick(object sender, MouseEventArgs e)
        {

        }

        private void button3_Click(object sender, EventArgs e)
        {
            ApplyNativeControlCenterAction(ControlCenterActionIntent.SelectBoard(
                ControlCenterBoardSelectionMode.Inside));
        }

        private void button4_Click(object sender, EventArgs e)
        {
            ApplyNativeControlCenterAction(ControlCenterActionIntent.OneTimeSync());
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

        private bool TryRunOneTimeSyncAction()
        {
            hasRetainedFoxTitleSnapshot = false;
            bool oneTimeSyncSucceeded = sessionCoordinator.TryRunOneTimeSync();
            if (!oneTimeSyncSucceeded)
            {
                ResetMainWindowTitle();
                return false;
            }
            if (IsFoxSyncType(CurrentSyncType))
            {
                hasRetainedFoxTitleSnapshot = true;
                ApplyMainWindowTitle();
            }
            return true;
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
            ApplyNativeControlCenterAction(ControlCenterActionIntent.ContinuousSync());
        }

        private void stopSync()
        {
            sessionCoordinator.StopSyncSession();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            ApplyNativeControlCenterAction(ControlCenterActionIntent.ClearBoard());
        }

        private void btnForceRebuild_Click(object sender, EventArgs e)
        {
            ApplyNativeControlCenterAction(ControlCenterActionIntent.ForceRebuild());
        }

        private void ArmForceRebuildAction()
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

        private void radioButtonYike_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoYike.Checked)
                setNativeBoardMode(TYPE_YIKE);
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
            RunShutdownStep(shutdownExceptions, delegate { DisposeWebViewUpdateBridge(); });
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
            if (!isShuttingDown && !IsDisposed && !Disposing && webView == null)
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
            if (webView == null)
                ApplyMainFormUi();
            else
                UpdateWebViewMinimumSizeForCurrentDpi();
        }

        private void DisposeInputHooks()
        {
            if (mouseHook == null)
                return;
            mouseHook.MouseMove -= mh_MouseMoveEvent;
            mouseHook.MouseClick -= mh_MouseMoveEvent2;
            mouseHook.Enabled = false;
            mouseHook.Stop();
            mouseHook.Dispose();
            mouseHook = null;
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
            if (suppressControlCenterProjectionEvents)
                return;
            if (this.rdo19x19.Checked)
            {
                ControlCenterApplyResult result = ApplyControlCenterIntent(
                    ControlCenterIntent.SetBoardSize(ControlCenterBoardSizeKind.Preset19));
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
            }
        }

        private void radioButton6_CheckedChanged(object sender, EventArgs e)
        {
            if (isInitializingProtocolState)
                return;
            if (suppressControlCenterProjectionEvents)
                return;
            if (this.rdo13x13.Checked)
            {
                ControlCenterApplyResult result = ApplyControlCenterIntent(
                    ControlCenterIntent.SetBoardSize(ControlCenterBoardSizeKind.Preset13));
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
            }
        }

        private void radioButton7_CheckedChanged(object sender, EventArgs e)
        {
            if (isInitializingProtocolState)
                return;
            if (suppressControlCenterProjectionEvents)
                return;
            if (this.rdo9x9.Checked)
            {
                ControlCenterApplyResult result = ApplyControlCenterIntent(
                    ControlCenterIntent.SetBoardSize(ControlCenterBoardSizeKind.Preset9));
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
            }
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

        private delegate bool EnumChildWindowProc(IntPtr handle, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr window, EnumChildWindowProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder title, int maxCount);

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
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private static int buildMouseLParam(int x, int y)
        {
            return (x & 0xFFFF) | ((y & 0xFFFF) << 16);
        }

        // Keep legacy background modes non-blocking to preserve their historical behavior.
        private void postBackgroundMouseClick(int x, int y, IntPtr hwnd)
        {
            int lParam = buildMouseLParam(x, y);
            PostMessage(hwnd, WM_LBUTTONDOWN, IntPtr.Zero, (IntPtr)lParam);
            PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, (IntPtr)lParam);
        }

        // Fox background placement needs a blocking move/click sequence in client coordinates.
        private void sendBackgroundMouseClickWithMove(int x, int y, IntPtr hwnd)
        {
            int lParam = buildMouseLParam(x, y);
            SendMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)lParam);
            SendMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, (IntPtr)lParam);
            SendMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, (IntPtr)lParam);
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
            if (isInitializingProtocolState || suppressControlCenterProjectionEvents)
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetAiTime(textBox1.Text));
            if (result.Outcome == ControlCenterApplyOutcome.Rejected)
                ProjectControlCenterState();
            else
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void button7_Click_1(object sender, EventArgs e)
        {
            SendNoPonderCommand();
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            NormalizeNumericTextBox(textBox2);
            if (isInitializingProtocolState || suppressControlCenterProjectionEvents)
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetPlayouts(textBox2.Text));
            if (result.Outcome == ControlCenterApplyOutcome.Rejected)
                ProjectControlCenterState();
            else
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
            NormalizeNumericTextBox(textBox3);
            if (isInitializingProtocolState || suppressControlCenterProjectionEvents)
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetFirstPolicy(textBox3.Text));
            if (result.Outcome == ControlCenterApplyOutcome.Rejected)
                ProjectControlCenterState();
            else
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void checkBox1_CheckedChanged_1(object sender, EventArgs e)
        {
            if (isInitializingProtocolState)
                return;
            if (suppressControlCenterProjectionEvents)
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetTwoWaySync(chkBothSync.Checked));
            ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
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
            if (isInitializingProtocolState || suppressControlCenterProjectionEvents)
                return;

            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetAutoPlayEnabled(chkAutoPlay.Checked));
            if (result.Outcome == ControlCenterApplyOutcome.Rejected)
                ProjectControlCenterState();
            else
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {
            webViewState.Page = "settings";
            GetWebViewSettingsState();
            PostWebViewState();
        }

        private void radioButton8_CheckedChanged(object sender, EventArgs e)
        {
            if (isInitializingProtocolState)
                return;
            if (suppressControlCenterProjectionEvents)
                return;
            if (this.rdoOtherBoard.Checked)
            {
                ControlCenterApplyResult result = ApplyControlCenterIntent(
                    ControlCenterIntent.SetBoardSize(ControlCenterBoardSizeKind.Custom));
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
            }
        }



        private void parseWidth(object sender, EventArgs e)
        {
            if (isInitializingProtocolState)
                return;
            if (suppressControlCenterProjectionEvents
                || controlCenterRuntime.CurrentPreferences.BoardSizeKind != ControlCenterBoardSizeKind.Custom)
                return;
            int parsed;
            if (!int.TryParse(txtBoardWidth.Text, out parsed))
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetCustomBoardWidth(parsed));
            ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void parseHeight(object sender, EventArgs e)
        {
            if (isInitializingProtocolState)
                return;
            if (suppressControlCenterProjectionEvents
                || controlCenterRuntime.CurrentPreferences.BoardSizeKind != ControlCenterBoardSizeKind.Custom)
                return;
            int parsed;
            if (!int.TryParse(txtBoardHeight.Text, out parsed))
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetCustomBoardHeight(parsed));
            ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
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
            if (suppressAutoPlayColorModeEvents || isInitializingProtocolState || !radioBlack.Checked)
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.ManualBlack));
            if (result.Outcome == ControlCenterApplyOutcome.Rejected)
                ProjectControlCenterState();
            else
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void radioWhite_CheckedChanged(object sender, EventArgs e)
        {
            if (suppressAutoPlayColorModeEvents || isInitializingProtocolState || !radioWhite.Checked)
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.ManualWhite));
            if (result.Outcome == ControlCenterApplyOutcome.Rejected)
                ProjectControlCenterState();
            else
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void radioAutoPlayColor_CheckedChanged(object sender, EventArgs e)
        {
            if (suppressAutoPlayColorModeEvents || isInitializingProtocolState || !radioAutoPlayColor.Checked)
                return;
            ControlCenterRuntimeSnapshot controlCenter = controlCenterRuntime.Snapshot;
            if (!controlCenter.AutoPlayEnabled
                || (controlCenter.Platform != SyncMode.Fox
                    && controlCenter.Platform != SyncMode.FoxBackgroundPlace))
            {
                ProjectControlCenterState();
                return;
            }
            if (string.IsNullOrWhiteSpace(ResolveCurrentFoxAutoPlayNicknameSignature()))
            {
                OpenWebViewIdentity(true);
                PostWebViewState();
                return;
            }
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.FoxAuto));
            if (result.Outcome == ControlCenterApplyOutcome.Rejected)
                ProjectControlCenterState();
            else
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void radioAutoPlayMoveFirst_CheckedChanged(object sender, EventArgs e)
        {
            if (suppressAutoPlayMoveModeEvents
                || isInitializingProtocolState
                || !radioAutoPlayMoveFirst.Checked)
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetAutoPlayMoveMode(AutoPlayMoveMode.FirstCandidate));
            if (result.Outcome == ControlCenterApplyOutcome.Rejected)
                ProjectControlCenterState();
            else
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void radioAutoPlayMoveGma_CheckedChanged(object sender, EventArgs e)
        {
            if (suppressAutoPlayMoveModeEvents
                || isInitializingProtocolState
                || !radioAutoPlayMoveGma.Checked)
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetAutoPlayMoveMode(AutoPlayMoveMode.GenmoveAnalyze));
            if (result.Outcome == ControlCenterApplyOutcome.Rejected)
                ProjectControlCenterState();
            else
                ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
        }

        private void btnFoxAutoPlayIdentity_Click(object sender, EventArgs e)
        {
            if (!IsFoxSyncType(CurrentSyncType))
                return;

            OpenWebViewIdentity(false);
            PostWebViewState();
        }


        private void button8_Click(object sender, EventArgs e)
        {
            ApplyNativeControlCenterAction(ControlCenterActionIntent.SwapOrder());
        }

        private void chkShowInBoard_CheckedChanged(object sender, EventArgs e)
        {
            if (isInitializingProtocolState)
                return;
            if (suppressControlCenterProjectionEvents)
                return;
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetShowOnBoard(chkShowInBoard.Checked));
            ControlCenterSnapshotPublisher.PublishIfNeeded(result, PostWebViewState);
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
            ApplyNativeControlCenterAction(ControlCenterActionIntent.QuickSync());
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            ApplyNativeControlCenterAction(ControlCenterActionIntent.SelectBoard(
                ControlCenterBoardSelectionMode.Rectangle));
        }

        private void button11_Click(object sender, EventArgs e)
        {
            ApplyNativeControlCenterAction(ControlCenterActionIntent.SelectBoard(
                ControlCenterBoardSelectionMode.Line1));
        }

        private void ApplyNativeBoardSelection(ControlCenterBoardSelectionMode mode)
        {
            if (mode == ControlCenterBoardSelectionMode.Inside)
            {
                mouseHook.Enabled = true;
                clicked = true;
                return;
            }

            if (mode == ControlCenterBoardSelectionMode.Rectangle)
            {
                isMannulCircle = false;
            }
            else if (mode == ControlCenterBoardSelectionMode.Line1)
            {
                isMannulCircle = true;
            }
            else
            {
                throw new ArgumentOutOfRangeException("mode");
            }
            selectBoard();
        }

        private void selectBoard()
        {
            mouseHook.Enabled = true;
            this.WindowState = FormWindowState.Minimized;
            form2 = new Form2(this, isMannulCircle);
            form2.ShowDialog(this);
        }

    }
}
