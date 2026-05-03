using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class StartupAndShutdownRegressionTests
    {
        [Fact]
        public void MainFormRuntimeComposer_BindsSessionStateBeforeCreatingMainForm()
        {
            string source = LoadSource("readboard", "Core", "Protocol", "MainFormRuntimeComposer.cs");

            int bindIndex = IndexOfRequired(source, "coordinator.BindSessionState(sessionState);");
            int createHostIndex = IndexOfRequired(source, "MainForm host = new MainForm(");

            Assert.True(bindIndex < createHostIndex, "SessionState must be bound before MainForm applies loaded configuration.");
        }

        [Fact]
        public void Program_ReplaysStartupProtocolStateAfterReadyHandshake()
        {
            string source = LoadSource("readboard", "Program.cs");

            int readyIndex = IndexOfRequired(source, "mainForm.NotifyProtocolReady();");
            int replayIndex = IndexOfRequired(source, "mainForm.ReplayStartupProtocolState();");

            Assert.True(replayIndex > readyIndex, "Startup protocol replay should happen after the ready/playponder handshake.");
        }

        [Theory]
        [InlineData("private void textbox1_TextChanged(object sender, EventArgs e)")]
        [InlineData("private void textBox2_TextChanged(object sender, EventArgs e)")]
        [InlineData("private void textBox3_TextChanged(object sender, EventArgs e)")]
        [InlineData("private void checkBox1_CheckedChanged_1(object sender, EventArgs e)")]
        public void InitializationSensitiveHandlers_GuardProtocolSideEffects(string methodSignature)
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, methodSignature);

            Assert.Contains("if (isInitializingProtocolState)", methodSlice);
        }

        [Theory]
        [InlineData("private void radioButton5_CheckedChanged(object sender, EventArgs e)")]
        [InlineData("private void radioButton6_CheckedChanged(object sender, EventArgs e)")]
        [InlineData("private void radioButton7_CheckedChanged(object sender, EventArgs e)")]
        [InlineData("private void radioButton8_CheckedChanged(object sender, EventArgs e)")]
        public void StartupBoardSelectionHandlers_GuardPersistenceDuringInitialization(string methodSignature)
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, methodSignature);

            Assert.Contains("if (isInitializingProtocolState)", methodSlice);
        }

        [Fact]
        public void ApplyLoadedConfiguration_ReappliesShowInBoardConstraintsAfterRestoringConfig()
        {
            string source = LoadSource("readboard", "MainForm.Configuration.cs");

            int restoreIndex = IndexOfRequired(source, "chkShowInBoard.Checked = Program.showInBoard;");
            int normalizeIndex = source.LastIndexOf("ApplySyncModeControlState();", StringComparison.Ordinal);

            Assert.True(normalizeIndex > restoreIndex, "Loaded show-in-board state must be normalized after restoring the saved checkbox value.");
        }

        [Fact]
        public void ApplyLoadedConfiguration_DefersWindowClampUntilFinalUiLayout()
        {
            string source = LoadSource("readboard", "MainForm.Configuration.cs");
            string methodSlice = GetMethodSlice(source, "private void ApplyLoadedConfiguration()");

            Assert.DoesNotContain("Location = ClampToScreenWorkingArea", methodSlice);
        }

        [Fact]
        public void ApplyMainFormUi_ClampsSavedWindowLocationAfterLayoutStabilizes()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void ApplyMainFormUi()");
            int layoutIndex = IndexOfRequired(methodSlice, "PerformLayout();");
            int restoreIndex = IndexOfRequired(methodSlice, "RestoreSavedWindowLocationIfNeeded();");

            Assert.True(restoreIndex > layoutIndex, "Saved window coordinates must clamp after final layout has established the runtime size.");
        }

        [Fact]
        public void MainForm_UsesTargetCommitWidthClampBeforeThemeApplication()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void ApplyMainFormUi()");

            Assert.Contains("ConstrainMainFormWidth();", methodSlice);
            Assert.DoesNotContain("MainFormLayoutProfile layoutProfile = CreateMainFormLayoutProfile();", methodSlice);
            Assert.DoesNotContain("ApplyMainFormClientSizeProfile(layoutProfile);", methodSlice);
        }

        [Fact]
        public void MainForm_UsesHeaderPlatformAnchorOnlyWhenUtilitiesStayInRightColumn()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void ApplyMainFormUi()");

            Assert.Contains("int boardTop = headerLayout.UtilitiesInRightColumn", methodSlice);
            Assert.Contains("? headerLayout.PlatformBottom + ScaleValue(12)", methodSlice);
            Assert.Contains(": headerLayout.UtilityBottom + ScaleValue(12);", methodSlice);
            Assert.Contains("int syncBottom = ArrangeMainSyncSection(Math.Max(boardBottom, headerLayout.UtilityBottom) + ScaleValue(12));", methodSlice);
        }

        [Fact]
        public void MainForm_SwitchTheme_ReusesTheSameLayoutEntryPoint()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void SwitchTheme(int themeMode)");

            Assert.Contains("Program.uiThemeMode = themeMode;", methodSlice);
            Assert.Contains("ApplyMainFormUi();", methodSlice);
        }

        [Fact]
        public void MainForm_PrefersLegacyDesktopLayoutBeforeAdaptiveFallback()
        {
            string source = LoadSource("readboard", "Form1.cs");

            Assert.Contains("if (CanUseLegacyMainDesktopLayout())", source);
            Assert.Contains("return ArrangeLegacyMainHeader();", source);
            Assert.Contains("return ArrangeAdaptiveMainHeader();", source);
            Assert.Contains("return ArrangeLegacyMainBoardSection(top);", source);
            Assert.Contains("return ArrangeAdaptiveMainBoardSection(top, headerLayout);", source);
            Assert.Contains("return ArrangeLegacyMainSyncSection(top);", source);
            Assert.Contains("return ArrangeAdaptiveMainSyncSection(top);", source);
            Assert.Contains("ArrangeLegacyMainActions(top);", source);
            Assert.Contains("ArrangeAdaptiveMainActions(top);", source);
        }

        [Fact]
        public void MainForm_SyncVisitsInputs_RestoreTargetCommitSizing()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string legacySlice = GetMethodSlice(source, "private int ArrangeLegacyMainSyncSection(int top)");
            string adaptiveSlice = GetMethodSlice(source, "private int ArrangeAdaptiveMainSyncSection(int top)");
            string helperSlice = GetMethodSlice(source, "private int GetSharedMainSyncVisitsLabelWidth()");

            Assert.Contains("int sharedVisitsLabelWidth = GetSharedMainSyncVisitsLabelWidth();", legacySlice);
            Assert.Contains("int sharedLegacyVisitsPanelWidth = GetLegacyMainSyncVisitsPanelWidth();", legacySlice);
            Assert.Contains("lblTotalVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(18));", legacySlice);
            Assert.Contains("lblBestMoveVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(18));", legacySlice);
            Assert.Contains("int sharedVisitsLabelWidth = GetSharedMainSyncVisitsLabelWidth();", adaptiveSlice);
            Assert.Contains("int sharedAdaptiveVisitsPanelWidth = GetAdaptiveMainSyncVisitsPanelWidth();", adaptiveSlice);
            Assert.Contains("panel2.Size = new System.Drawing.Size(sharedAdaptiveVisitsPanelWidth, rowHeight);", adaptiveSlice);
            Assert.Contains("panel4.Size = new System.Drawing.Size(sharedAdaptiveVisitsPanelWidth, rowHeight);", adaptiveSlice);
            Assert.Contains("return Math.Max(lblTotalVisits.PreferredSize.Width, lblBestMoveVisits.PreferredSize.Width);", helperSlice);
        }

        [Fact]
        public void BuildCurrentAppConfig_PersistsRestoreBoundsInsteadOfMinimizedCoordinates()
        {
            string source = LoadSource("readboard", "MainForm.Configuration.cs");
            string methodSlice = GetMethodSlice(source, "private AppConfig BuildCurrentAppConfig()");

            Assert.Contains("Point persistedWindowLocation = ResolvePersistableWindowLocation();", methodSlice);
            Assert.Contains("config.WindowPosX = persistedWindowLocation.X;", methodSlice);
            Assert.Contains("config.WindowPosY = persistedWindowLocation.Y;", methodSlice);
        }

        [Fact]
        public void ResolvePersistableWindowLocation_RejectsHiddenOrOffscreenCoordinates()
        {
            string source = LoadSource("readboard", "MainForm.Configuration.cs");
            string methodSlice = GetMethodSlice(source, "private Point ResolvePersistableWindowLocation()");

            Assert.Contains("WindowState == FormWindowState.Normal", methodSlice);
            Assert.Contains("RestoreBounds", methodSlice);
            Assert.Contains("location.X <= -16000", methodSlice);
            Assert.Contains("location.Y <= -16000", methodSlice);
            Assert.Contains("SystemInformation.VirtualScreen", methodSlice);
            Assert.Contains("!virtualScreen.Contains(location)", methodSlice);
            Assert.Contains("return new Point(-1, -1);", methodSlice);
        }

        [Fact]
        public void MainForm_OnlyRestoresSavedWindowLocationDuringStartupLayout()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void RestoreSavedWindowLocationIfNeeded()");

            Assert.Contains("if (isMainFormSizeInitialized)", methodSlice);
            Assert.Contains("RestoreSavedWindowLocation();", methodSlice);
        }

        [Fact]
        public void MainForm_UsesSavedStartupScreenForInitialWorkingAreaAndScale()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string scaleSlice = GetMethodSlice(source, "private float GetCurrentDpiScale()");
            string workingAreaSlice = GetMethodSlice(source, "private Rectangle GetCurrentWorkingArea()");
            string startupPointSlice = GetMethodSlice(source, "private Point? TryGetStartupReferencePoint()");

            Assert.Contains("Point? startupReferencePoint = TryGetStartupReferencePoint();", scaleSlice);
            Assert.Contains("DisplayScaling.GetScaleForPoint(startupReferencePoint.Value)", scaleSlice);
            Assert.Contains("ResolveLayoutReferencePoint()", workingAreaSlice);
            Assert.Contains("if (isMainFormSizeInitialized || posX == -1 || posY == -1)", startupPointSlice);
        }

        [Fact]
        public void ShowInBoardToggle_RejectsOnlyForegroundSyncMode()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string supportSlice = GetMethodSlice(source, "private bool SupportsShowInBoard()");
            string methodSlice = GetMethodSlice(source, "private void chkShowInBoard_CheckedChanged(object sender, EventArgs e)");

            Assert.Contains("CurrentSyncType != TYPE_FOREGROUND", supportSlice);
            Assert.DoesNotContain("UsesManualSelectionType(CurrentSyncType)", supportSlice);
            Assert.Contains("CurrentSyncType == TYPE_FOREGROUND", methodSlice);
            Assert.Contains("chkShowInBoard.Checked = false;", methodSlice);
        }

        [Fact]
        public void ShowInBoardHotkey_OnlyTogglesWhenCurrentModeSupportsIt()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void HookListener_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)");

            Assert.Contains("SupportsShowInBoard()", methodSlice);
            Assert.Contains("!Program.disableShowInBoardShortcut", methodSlice);
        }

        [Fact]
        public void ResolveSyncPlatform_UsesFoxTokenForAllFoxSyncModes()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private string ResolveSyncPlatform()");

            Assert.Contains("return IsFoxSyncType(CurrentSyncType) ? \"fox\" : \"generic\";", methodSlice);
        }

        [Fact]
        public void MainForm_RemovesLegacyPublicStaticSelectionAndTypeMirrors()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string setSyncTypeSlice = GetMethodSlice(source, "private void SetCurrentSyncType(int syncType)");
            string updateSelectionSlice = GetMethodSlice(source, "private void UpdateSelectionBounds(int x1, int y1, int x2, int y2)");
            string captureSlice = GetMethodSlice(source, "private SyncCoordinatorHostSnapshot CaptureSnapshotCore()");

            Assert.DoesNotContain("public static int ox1", source);
            Assert.DoesNotContain("public static int oy1", source);
            Assert.DoesNotContain("public static int type", source);
            Assert.Contains("currentSyncType = syncType;", setSyncTypeSlice);
            Assert.DoesNotContain("type = syncType;", setSyncTypeSlice);
            Assert.Contains("selectionX1 = x1;", updateSelectionSlice);
            Assert.Contains("selectionY1 = y1;", updateSelectionSlice);
            Assert.DoesNotContain("ox1 = x1;", updateSelectionSlice);
            Assert.DoesNotContain("oy1 = y1;", updateSelectionSlice);
            Assert.Contains("LegacyTypeToken = CurrentSyncType.ToString()", captureSlice);
        }

        [Fact]
        public void ResolveFoxWindowContext_ResolvesFoxTitlesFromSelectedHandleOrAncestors()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private FoxWindowContext ResolveFoxWindowContext()");

            Assert.Contains("if (!IsFoxSyncType(CurrentSyncType) || hwnd == IntPtr.Zero)", methodSlice);
            Assert.Contains("InvalidateFoxWindowBinding();", methodSlice);
            Assert.Contains("TryRefreshFoxWindowContextFromBinding(out foxWindowContext)", methodSlice);
            Assert.Contains("TryResolveFoxWindowBinding(out foxWindowContext)", methodSlice);
        }

        [Fact]
        public void ShowInBoardToggle_ReplaysForegroundFoxProtocolStateImmediately()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void chkShowInBoard_CheckedChanged(object sender, EventArgs e)");

            Assert.Contains("CanUseForegroundFoxInBoardProtocol()", methodSlice);
            Assert.Contains("SendForegroundFoxInBoardCommand(chkShowInBoard.Checked && sessionCoordinator.SyncBoth);", methodSlice);
        }

        [Fact]
        public void ShowInBoardTooltip_ClearsCtrlXHintWhenShortcutIsDisabled()
        {
            string source = LoadSource("readboard", "Form1.cs");

            Assert.Contains("showInBoardShortcutToolTip.SetToolTip(this.chkShowInBoard, Program.disableShowInBoardShortcut ? string.Empty : \"Ctrl+X\");", source);
        }

        [Fact]
        public void SettingsForm_LoadsAndPersistsShowInBoardShortcutToggle()
        {
            string source = LoadSource("readboard", "Form4.cs");

            Assert.Contains("chkDisableShowInBoardShortcut.Checked = config.DisableShowInBoardShortcut;", source);
            Assert.Contains("updatedConfig.DisableShowInBoardShortcut = chkDisableShowInBoardShortcut.Checked;", source);
        }

        [Fact]
        public void SettingsForm_LoadsPersistsAndOpensDebugDiagnostics()
        {
            string source = LoadSource("readboard", "Form4.cs");
            string designerSource = LoadSource("readboard", "Form4.Designer.cs");
            string programSource = LoadSource("readboard", "Program.cs");
            string cnSource = LoadSource("readboard", "language_cn.txt");
            string enSource = LoadSource("readboard", "language_en.txt");

            Assert.Contains("chkDebugDiagnostics.Checked = config.DebugDiagnosticsEnabled;", source);
            Assert.Contains("updatedConfig.DebugDiagnosticsEnabled = chkDebugDiagnostics.Checked;", source);
            Assert.Contains("btnOpenDebugDiagnostics.Text = getLangStr(\"SettingsForm_btnOpenDebugDiagnostics\");", source);
            Assert.Contains("OpenDebugDiagnosticsDirectory();", source);
            Assert.Contains("this.chkDebugDiagnostics", designerSource);
            Assert.Contains("this.btnOpenDebugDiagnostics", designerSource);
            Assert.Contains("SettingsForm_chkDebugDiagnostics", programSource);
            Assert.Contains("SettingsForm_btnOpenDebugDiagnostics", programSource);
            Assert.Contains("SettingsForm_chkDebugDiagnostics=", cnSource);
            Assert.Contains("SettingsForm_btnOpenDebugDiagnostics=", cnSource);
            Assert.Contains("SettingsForm_chkDebugDiagnostics=", enSource);
            Assert.Contains("SettingsForm_btnOpenDebugDiagnostics=", enSource);
        }

        [Fact]
        public void SettingsForm_RefreshesMainFormShortcutTooltipAfterSaving()
        {
            string source = LoadSource("readboard", "Form4.cs");
            string methodSlice = GetMethodSlice(source, "private void button1_Click(object sender, EventArgs e)");

            Assert.Contains("mainForm.RefreshShowInBoardShortcutToolTip();", methodSlice);
        }

        [Fact]
        public void SettingsForm_ArrangesShortcutToggleAlignedWithVerifyMove()
        {
            string source = LoadSource("readboard", "Form4.cs");
            string layoutSlice = GetMethodSlice(source, "private void ArrangeSettingsLayout()");
            string adaptiveSlice = GetMethodSlice(source, "private void ArrangeAdaptiveSettingsLayout()");
            string legacySlice = GetMethodSlice(source, "private void ArrangeLegacySettingsLayout()");

            Assert.Contains("CanUseLegacySettingsDesktopLayout()", layoutSlice);
            Assert.Contains("ArrangeLegacySettingsLayout();", layoutSlice);
            Assert.Contains("ArrangeAdaptiveSettingsLayout();", layoutSlice);
            Assert.Contains("LayoutOptionRow(chkVerifyMove, chkDisableShowInBoardShortcut", adaptiveSlice);
            Assert.Contains("LayoutOptionRow(chkDebugDiagnostics, btnOpenDebugDiagnostics", adaptiveSlice);
            Assert.Contains("chkDisableShowInBoardShortcut.Location = new Point(ScaleValue(170), top + optionRowGap * 2);", legacySlice);
            Assert.Contains("chkDebugDiagnostics.Location = new Point(left, top + optionRowGap * 3);", legacySlice);
            Assert.Contains("btnOpenDebugDiagnostics.SetBounds(ScaleValue(170), top + optionRowGap * 3", legacySlice);
        }

        [Fact]
        public void SettingsForm_LegacyLayoutMeasurement_ClearsAdaptiveCheckboxWidthConstraintsBeforeDecision()
        {
            string source = LoadSource("readboard", "Form4.cs");
            string decisionSlice = GetMethodSlice(source, "private bool CanUseLegacySettingsDesktopLayout()");
            string helperSlice = GetMethodSlice(source, "private int GetLegacyOptionPreferredWidth(params CheckBox[] checkBoxes)");

            Assert.Contains("GetLegacyOptionPreferredWidth(chkAutoMin, chkMag, chkVerifyMove)", decisionSlice);
            Assert.Contains("GetLegacyOptionPreferredWidth(chkPonder, chkEnhanceScreen, chkDisableShowInBoardShortcut)", decisionSlice);
            Assert.Contains("ConfigureLegacyOptionCheckBox(checkBox);", helperSlice);
            Assert.Contains("checkBox.PreferredSize.Width", helperSlice);
        }

        [Fact]
        public void TipsForm_PrefersLegacyDesktopLayoutBeforeAdaptiveFallback()
        {
            string source = LoadSource("readboard", "Form7.cs");
            string layoutSlice = GetMethodSlice(source, "private void ArrangeTipsLayout()");
            string legacySlice = GetMethodSlice(source, "private void ArrangeLegacyTipsLayout()");
            string adaptiveSlice = GetMethodSlice(source, "private void ArrangeAdaptiveTipsLayout()");

            Assert.Contains("CanUseLegacyTipsDesktopLayout()", layoutSlice);
            Assert.Contains("ArrangeLegacyTipsLayout();", layoutSlice);
            Assert.Contains("ArrangeAdaptiveTipsLayout();", layoutSlice);
            Assert.Contains("btnConfirm.SetBounds(footerLeft, footerTop, primaryWidth, buttonHeight);", legacySlice);
            Assert.Contains("btnNotAskAgain.SetBounds(btnConfirm.Right + buttonGap, footerTop, secondaryWidth, buttonHeight);", legacySlice);
            Assert.Contains("btnConfirm.SetBounds(left, footerTop, contentWidth, buttonHeight);", adaptiveSlice);
            Assert.Contains("btnNotAskAgain.SetBounds(left, btnConfirm.Bottom + rowGap, contentWidth, buttonHeight);", adaptiveSlice);
        }

        [Fact]
        public void UpdateDialog_KeepsDesktopInformationOrderAndNonWrappingFooter()
        {
            string source = LoadSource("readboard", "FormUpdate.cs");
            string designerSource = LoadSource("readboard", "FormUpdate.Designer.cs");

            Assert.Contains("ApplyInfoPanelLayout();", source);
            Assert.Contains("ConstrainUpdateDialogSize();", source);
            Assert.Contains("buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;", designerSource);
            Assert.Contains("buttonPanel.WrapContents = false;", designerSource);
            Assert.Contains("rootPanel.Controls.Add(lblTitle, 0, 0);", designerSource);
            Assert.Contains("rootPanel.Controls.Add(infoPanel, 0, 1);", designerSource);
            Assert.Contains("rootPanel.Controls.Add(lblReleaseNotes, 0, 2);", designerSource);
            Assert.Contains("rootPanel.Controls.Add(txtReleaseNotes, 0, 3);", designerSource);
            Assert.Contains("rootPanel.Controls.Add(buttonPanel, 0, 4);", designerSource);
        }

        [Fact]
        public void ReplayStartupProtocolState_SkipsBlankNumericOverrides()
        {
            string source = LoadSource("readboard", "MainForm.Protocol.cs");
            string methodSlice = GetMethodSlice(source, "public void ReplayStartupProtocolState()");

            Assert.Contains("if (!string.IsNullOrWhiteSpace(textBox1.Text))", methodSlice);
            Assert.Contains("if (!string.IsNullOrWhiteSpace(textBox2.Text))", methodSlice);
            Assert.Contains("if (!string.IsNullOrWhiteSpace(textBox3.Text))", methodSlice);
        }

        [Fact]
        public void ReplayStartupProtocolState_ReusesSyncBothAwareInBoardStateChange()
        {
            string source = LoadSource("readboard", "MainForm.Protocol.cs");
            string methodSlice = GetMethodSlice(source, "public void ReplayStartupProtocolState()");

            Assert.Contains("SendBothSyncStateChange();", methodSlice);
            Assert.DoesNotContain("SendCurrentForegroundFoxInBoardCommand();", methodSlice);
        }

        [Fact]
        public void MainForm_HandlePlaceRequest_UsesSerializedPlaceQueue()
        {
            string source = LoadSource("readboard", "MainForm.Protocol.cs");
            string methodSlice = GetMethodSlice(source, "void IProtocolCommandHost.HandlePlaceRequest(MoveRequest request)");

            Assert.Contains("EnqueuePlaceRequest(protocolMove);", methodSlice);
            Assert.DoesNotContain("ThreadPool.QueueUserWorkItem", methodSlice);
        }

        [Fact]
        public void MainForm_PlaceMove_UsesSerializedPlaceQueue()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "public void placeMove(int x, int y)");

            Assert.Contains("EnqueuePlaceRequest(new MoveRequest", methodSlice);
            Assert.DoesNotContain("sessionCoordinator.HandlePlaceRequest(", methodSlice);
        }

        [Fact]
        public void MainForm_Shutdown_StopsPlaceQueueBeforeSendingShutdownProtocol()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "public void shutdown(bool persistConfiguration)");
            int stopQueueIndex = IndexOfRequired(methodSlice, "placeRequestQueue.Stop();");
            int sendShutdownIndex = IndexOfRequired(methodSlice, "SendShutdownProtocol();");

            Assert.True(stopQueueIndex < sendShutdownIndex, "Queued place requests must be stopped before shutdown protocol lines are sent.");
            Assert.Contains("lock (placeProtocolSyncRoot)", methodSlice);
        }

        [Fact]
        public void MainForm_ExecutePlaceRequest_UsesShutdownGuardedPlaceResultSender()
        {
            string source = LoadSource("readboard", "MainForm.Protocol.cs");
            string methodSlice = GetMethodSlice(source, "private void ExecutePlaceRequest(MoveRequest request)");

            Assert.Contains("if (!result.ShouldSendResponse)", methodSlice);
            Assert.Contains("TrySendPlaceProtocolResult(result.Success);", methodSlice);
            Assert.DoesNotContain("SendPlacementResultCommand(result.Success);", methodSlice);
        }

        [Fact]
        public void MainForm_ExecutePlaceRequest_UsesShutdownGuardedPlaceErrorSender()
        {
            string source = LoadSource("readboard", "MainForm.Protocol.cs");
            string methodSlice = GetMethodSlice(source, "private void ExecutePlaceRequest(MoveRequest request)");

            Assert.Contains("TrySendPlaceProtocolError(ex.ToString());", methodSlice);
            Assert.DoesNotContain("sessionCoordinator.SendError(ex.ToString());", methodSlice);
        }

        [Fact]
        public void MainForm_PlaceProtocolGate_ChecksShutdownWhileHoldingSharedLock()
        {
            string source = LoadSource("readboard", "MainForm.Protocol.cs");
            string methodSlice = GetMethodSlice(source, "private bool TrySendPlaceProtocolMessage(Action sendAction)");

            Assert.Contains("lock (placeProtocolSyncRoot)", methodSlice);
            Assert.Contains("if (isShuttingDown)", methodSlice);
            Assert.Contains("sendAction();", methodSlice);
        }

        [Fact]
        public void Coordinator_PlacePendingMove_ReportsFailuresThroughHostPlaceGate()
        {
            string source = LoadSource("readboard", "Core", "Protocol", "SyncSessionCoordinator.Orchestration.cs");
            string methodSlice = GetMethodSlice(source, "private bool PlacePendingMove(");

            Assert.Contains("runtime.Host.TrySendPlaceProtocolError(", methodSlice);
            Assert.DoesNotContain("SendError(result == null ? \"Move placement returned no result.\" : result.FailureReason);", methodSlice);
        }

        [Fact]
        public void SettingsReset_UsesShutdownWithoutPersistingCurrentWindowState()
        {
            string source = LoadSource("readboard", "Form4.cs");

            Assert.Contains("GetHost().shutdown(false);", source);
            Assert.DoesNotContain("GetHost().shutdown();", source);
        }

        [Fact]
        public void MainForm_ShutdownSupportsSkippingPersistence()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "public void shutdown(bool persistConfiguration)");

            Assert.Contains("if (persistConfiguration)", methodSlice);
        }

        [Fact]
        public void MainForm_ShutdownContinuesCleanupAfterSaveFailure()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "public void shutdown(bool persistConfiguration)");
            int stopQueueIndex = IndexOfRequired(methodSlice, "placeRequestQueue.Stop();");
            int clearPendingIndex = IndexOfRequired(methodSlice, "ClearPendingProtocolCommands();");
            int persistIndex = IndexOfRequired(methodSlice, "PersistConfiguration();");
            int disposeHooksIndex = IndexOfRequired(methodSlice, "DisposeInputHooks();");
            int sendShutdownIndex = IndexOfRequired(methodSlice, "SendShutdownProtocol();");
            int disposeBitmapIndex = IndexOfRequired(methodSlice, "Program.DisposeBitmap();");
            int stopCoordinatorIndex = IndexOfRequired(methodSlice, "sessionCoordinator.Stop();");
            int requestCloseIndex = IndexOfRequired(methodSlice, "if (!IsHandleCreated)");
            int throwIndex = IndexOfRequired(methodSlice, "ThrowShutdownExceptions(shutdownExceptions);");

            Assert.True(stopQueueIndex < clearPendingIndex, "Shutdown must still stop the place queue before clearing pending protocol commands.");
            Assert.True(clearPendingIndex < persistIndex, "Queued startup commands must be cleared before persistence and protocol teardown.");
            Assert.True(persistIndex < disposeHooksIndex, "Cleanup must continue after a persistence failure.");
            Assert.True(disposeHooksIndex < sendShutdownIndex, "Input hooks must release before outbound shutdown protocol is sent.");
            Assert.True(sendShutdownIndex < disposeBitmapIndex, "Bitmap disposal belongs after shutdown protocol delivery.");
            Assert.True(disposeBitmapIndex < stopCoordinatorIndex, "Coordinator stop should happen after bitmap release.");
            Assert.True(stopCoordinatorIndex < requestCloseIndex, "Close request belongs after runtime teardown.");
            Assert.True(requestCloseIndex < throwIndex, "Shutdown must surface accumulated failures after requesting close.");
        }

        [Fact]
        public void MainForm_ShutdownRethrowsCapturedFailures()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string helperSlice = GetMethodSlice(source, "private static void ThrowShutdownExceptions(List<Exception> shutdownExceptions)");

            Assert.Contains("ExceptionDispatchInfo.Capture(shutdownExceptions[0]).Throw();", helperSlice);
            Assert.Contains("throw new AggregateException(\"MainForm shutdown failed.\", shutdownExceptions);", helperSlice);
        }

        [Fact]
        public void MainForm_ShutdownDefersCloseUntilHandleExists()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "public void shutdown(bool persistConfiguration)");

            Assert.Contains("if (!IsHandleCreated)", methodSlice);
            Assert.Contains("closeRequestedBeforeHandle = true;", methodSlice);
        }

        [Fact]
        public void MainForm_OnHandleCreated_ClosesPendingStartupShutdown()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "protected override void OnHandleCreated(EventArgs e)");

            Assert.Contains("if (!closeRequestedBeforeHandle || IsDisposed)", methodSlice);
            Assert.Contains("BeginInvoke((Action)Close);", methodSlice);
        }

        [Fact]
        public void MainForm_ShutdownStopsAndDisposesGlobalHooks()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string shutdownSlice = GetMethodSlice(source, "public void shutdown(bool persistConfiguration)");
            string helperSlice = GetMethodSlice(source, "private void DisposeInputHooks()");

            Assert.Contains("DisposeInputHooks();", shutdownSlice);
            Assert.Contains("keyboardHook.KeyDown -= HookListener_KeyDown;", helperSlice);
            Assert.Contains("keyboardHook.KeyUp -= HookListener_KeyUp;", helperSlice);
            Assert.Contains("keyboardHook.Stop();", helperSlice);
            Assert.Contains("keyboardHook.Dispose();", helperSlice);
            Assert.Contains("mouseHook.MouseMove -= mh_MouseMoveEvent;", helperSlice);
            Assert.Contains("mouseHook.MouseClick -= mh_MouseMoveEvent2;", helperSlice);
            Assert.Contains("mouseHook.Enabled = false;", helperSlice);
            Assert.Contains("mouseHook.Stop();", helperSlice);
            Assert.Contains("mouseHook.Dispose();", helperSlice);
        }

        [Fact]
        public void MainForm_ShutdownUsesCoordinatorStopInsteadOfBlockingStopSyncSession()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string shutdownSlice = GetMethodSlice(source, "public void shutdown(bool persistConfiguration)");

            Assert.DoesNotContain("sessionCoordinator.StopSyncSession();", shutdownSlice);
            Assert.Contains("sessionCoordinator.Stop();", shutdownSlice);
        }

        [Fact]
        public void MainForm_SnapshotCaptureCancellationAlsoCoversStoppedSyncState()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string cancelSlice = GetMethodSlice(source, "private bool IsSnapshotCaptureCancelled()");
            string stateSlice = GetMethodSlice(source, "private bool HasActiveSyncOperation()");

            Assert.Contains("return isShuttingDown || !HasActiveSyncOperation();", cancelSlice);
            Assert.Contains("return sessionCoordinator.StartedSync || sessionCoordinator.IsContinuousSyncing;", stateSlice);
        }

        [Fact]
        public void MainForm_InvokeUiHostAction_SkipsWhenShutdownOrHandleIsGone()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void InvokeUiHostAction(Action action)");

            Assert.Contains("if (isShuttingDown || IsDisposed || Disposing || !IsHandleCreated)", methodSlice);
            Assert.Contains("BeginInvoke(action);", methodSlice);
        }

        [Fact]
        public void MainForm_ShutdownUiCallbacks_UseUiOnlyInvoker()
        {
            string source = LoadSource("readboard", "Form1.cs");

            Assert.Contains("InvokeUiHostAction(delegate", GetMethodSlice(source, "void ISyncCoordinatorHost.OnKeepSyncStopped(bool continuousSyncActive)"));
            Assert.Contains("InvokeUiHostAction(ApplyContinuousSyncStoppedUi);", GetMethodSlice(source, "void ISyncCoordinatorHost.OnContinuousSyncStopped()"));
            Assert.Contains("InvokeUiHostAction(delegate", GetMethodSlice(source, "void ISyncCoordinatorHost.ShowMissingSyncSourceMessage()"));
            Assert.Contains("InvokeUiHostAction(delegate", GetMethodSlice(source, "void ISyncCoordinatorHost.ShowRecognitionFailureMessage()"));
            Assert.Contains("InvokeUiHostAction(delegate", GetMethodSlice(source, "void ISyncCoordinatorHost.MinimizeWindow()"));
        }

        [Fact]
        public void MainForm_KeepSyncStopUi_PreservesContinuousSyncLockout()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void ApplyKeepSyncStoppedUi(bool continuousSyncActive)");

            Assert.Contains(
                "if (!SyncToolbarTextResolver.ShouldRestoreIdleUiAfterKeepSyncStop(continuousSyncActive))",
                methodSlice);
        }

        [Fact]
        public void SelectionMagnifier_DoesNotUseSelectionOverlayAsShowOwner()
        {
            string source = LoadSource("readboard", "Form2.cs");

            Assert.Contains("form5.Show();", source);
            Assert.DoesNotContain("form5.Show(this);", source);
        }

        [Fact]
        public void BackgroundSelectionWindowBinding_ResolvesBothCircleModesFromSelectionCenter()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string snapSlice = GetMethodSlice(source, "public void Snap(int x1, int y1, int x2, int y2)");
            string moveSlice = GetMethodSlice(source, "void mh_MouseMoveEvent(object sender, MouseEventArgs e)");
            string clickSlice = GetMethodSlice(source, "void mh_MouseMoveEvent2(object sender, MouseEventArgs e)");

            Assert.Contains("if (CurrentSyncType == TYPE_BACKGROUND)", snapSlice);
            Assert.Contains("BeginResolveBackgroundSelectionWindowAsync();", snapSlice);
            Assert.DoesNotContain("CurrentSyncType == TYPE_BACKGROUND && !isMannulCircle", moveSlice);
            Assert.DoesNotContain("CurrentSyncType == TYPE_BACKGROUND && !isMannulCircle", clickSlice);
        }

        [Fact]
        public void BackgroundSelectionWindowBinding_DefersMainFormRestoreUntilAsyncBindingCompletes()
        {
            string form1Source = LoadSource("readboard", "Form1.cs");
            string form2Source = LoadSource("readboard", "Form2.cs");
            string snapSlice = GetMethodSlice(form1Source, "public void Snap(int x1, int y1, int x2, int y2)");
            string asyncSlice = GetMethodSlice(form1Source, "private void BeginResolveBackgroundSelectionWindowAsync()");
            string mouseUpSlice = GetMethodSlice(form2Source, "private void Form2_MouseUp(object sender, MouseEventArgs e)");

            Assert.Contains("BeginResolveBackgroundSelectionWindowAsync();", snapSlice);
            Assert.Contains("RestoreMainWindowAfterSelection();", asyncSlice);
            Assert.DoesNotContain("mainForm.Show();", mouseUpSlice);
        }

        [Fact]
        public void Program_StopsStartupHandshakeAfterShutdownRequest()
        {
            string source = LoadSource("readboard", "Program.cs");

            Assert.Equal(3, CountOccurrences(source, "if (mainForm.IsShutdownRequested)"));
        }

        [Fact]
        public void Program_DrainsStartupProtocolCommandsBeforeEachStartupHandshakeGate()
        {
            string source = LoadSource("readboard", "Program.cs");
            int firstDrainIndex = IndexOfRequired(source, "mainForm.DrainStartupProtocolCommands();");
            int firstShutdownCheckIndex = IndexOfRequired(source, "if (mainForm.IsShutdownRequested)");
            int readyIndex = IndexOfRequired(source, "mainForm.NotifyProtocolReady();");
            int secondDrainIndex = IndexOfRequired(source, "mainForm.DrainStartupProtocolCommands();", firstDrainIndex + 1);
            int replayIndex = IndexOfRequired(source, "mainForm.ReplayStartupProtocolState();");
            int thirdDrainIndex = IndexOfRequired(source, "mainForm.DrainStartupProtocolCommands();", secondDrainIndex + 1);

            Assert.True(firstDrainIndex < firstShutdownCheckIndex, "Early queued quit requests must drain before the first shutdown gate.");
            Assert.True(readyIndex < secondDrainIndex, "Ready handshake must be followed by a startup command drain.");
            Assert.True(replayIndex < thirdDrainIndex, "Startup protocol replay must be followed by a startup command drain.");
            Assert.Equal(3, CountOccurrences(source, "mainForm.DrainStartupProtocolCommands();"));
        }

        [Fact]
        public void Program_Main_StopsStartupHandshakeWhenSessionStartFails()
        {
            string source = LoadSource("readboard", "Program.cs");
            int startCheckIndex = IndexOfRequired(source, "if (!TryStartSession(mainForm))");
            int readyIndex = IndexOfRequired(source, "mainForm.NotifyProtocolReady();");
            int runIndex = IndexOfRequired(source, "Application.Run(mainForm);");

            Assert.True(startCheckIndex < readyIndex, "Startup failure must short-circuit the ready handshake.");
            Assert.True(startCheckIndex < runIndex, "Startup failure must short-circuit the run loop.");
        }

        [Fact]
        public void Program_TryStartSession_ReturnsFailureStateToCaller()
        {
            string source = LoadSource("readboard", "Program.cs");
            string methodSlice = GetMethodSlice(source, "private static bool TryStartSession(IWin32Window owner)");

            Assert.Contains("return true;", methodSlice);
            Assert.Contains("return false;", methodSlice);
        }

        [Fact]
        public void Manifest_UsesAsInvokerForHostLaunchedReadboard()
        {
            string content = LoadSource("readboard", "Properties", "app.manifest");
            XDocument manifest = XDocument.Parse(content);

            XElement requestedExecutionLevel = manifest
                .Descendants()
                .Single(element => element.Name.LocalName == "requestedExecutionLevel");

            Assert.Equal("asInvoker", (string)requestedExecutionLevel.Attribute("level"));
            Assert.Equal("false", (string)requestedExecutionLevel.Attribute("uiAccess"));
        }

        [Fact]
        public void MainForm_DispatchProtocolCommand_QueuesInboundCommandsUntilHandleExists()
        {
            string source = LoadSource("readboard", "MainForm.Protocol.cs");
            string methodSlice = GetMethodSlice(source, "void IProtocolCommandHost.DispatchProtocolCommand(Action command)");

            Assert.Contains("if (TryDispatchProtocolCommand(command))", methodSlice);
            Assert.Contains("EnqueuePendingProtocolCommand(command);", methodSlice);
        }

        [Fact]
        public void MainForm_OnHandleCreated_FlushesQueuedProtocolCommandsBeforePendingClose()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "protected override void OnHandleCreated(EventArgs e)");
            int flushIndex = IndexOfRequired(methodSlice, "FlushPendingProtocolCommands();");
            int closeIndex = IndexOfRequired(methodSlice, "if (!closeRequestedBeforeHandle || IsDisposed)");

            Assert.True(flushIndex < closeIndex, "Queued inbound protocol commands must re-enter on UI after handle creation.");
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path);
        }

        private static int IndexOfRequired(string source, string value)
        {
            int index = source.IndexOf(value, StringComparison.Ordinal);
            Assert.True(index >= 0, "Expected to find source fragment: " + value);
            return index;
        }

        private static int IndexOfRequired(string source, string value, int startIndex)
        {
            int index = source.IndexOf(value, startIndex, StringComparison.Ordinal);
            Assert.True(index >= 0, "Expected to find source fragment after index " + startIndex + ": " + value);
            return index;
        }

        private static string GetMethodSlice(string source, string methodSignature)
        {
            int startIndex = IndexOfRequired(source, methodSignature);
            int nextMethodIndex = source.IndexOf("\n        private ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            int publicMethodIndex = source.IndexOf("\n        public ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            int defaultMethodIndex = source.IndexOf("\n        void ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            int internalMethodIndex = source.IndexOf("\n        internal ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            if (publicMethodIndex >= 0 && (nextMethodIndex < 0 || publicMethodIndex < nextMethodIndex))
                nextMethodIndex = publicMethodIndex;
            if (defaultMethodIndex >= 0 && (nextMethodIndex < 0 || defaultMethodIndex < nextMethodIndex))
                nextMethodIndex = defaultMethodIndex;
            if (internalMethodIndex >= 0 && (nextMethodIndex < 0 || internalMethodIndex < nextMethodIndex))
                nextMethodIndex = internalMethodIndex;
            if (nextMethodIndex < 0)
                nextMethodIndex = source.Length;
            return source.Substring(startIndex, nextMethodIndex - startIndex);
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }
    }
}
