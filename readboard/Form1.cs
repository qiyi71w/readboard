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
        private readonly FoxIdentitySelection foxIdentitySelection;
        private readonly UiThreadInvoker uiThreadInvoker;
        private readonly SerialBackgroundWorkQueue placeRequestQueue;
        private HostedUpdateJourney hostedUpdateJourney;
        private readonly object placeProtocolSyncRoot = new object();
        private readonly object protocolCommandSyncRoot = new object();
        private readonly GitHubUpdateChecker updateChecker = new GitHubUpdateChecker();
        private readonly Queue<Action> pendingProtocolCommands = new Queue<Action>();
        private readonly BackgroundSelectionWindowBindingCoordinator backgroundSelectionWindowBindingCoordinator =
            new BackgroundSelectionWindowBindingCoordinator();
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
        private DateTime lastFoxAutoPlayColorDetectionTimestampUtc = DateTime.MinValue;
        private const int FoxAutoPlayColorDetectionCacheMs = 1000;

        int posX = -1;
        int posY = -1;

        private bool isShuttingDown = false;
        private bool closeRequestedBeforeHandle = false;
        private bool webViewWindowBoundsAppliedAfterHandle = false;
        private bool isInitializingProtocolState = true;
        private bool hostedUpdateSupported = false;
        private bool hostedUpdatePackageV2Supported = false;
        private readonly WebViewStatePublisher webViewStatePublisher;
        private readonly WebViewWindowCommandRuntime webViewWindowCommandRuntime;
        private readonly WebViewUpdateCheckJourney webViewUpdateCheckJourney;
        private readonly MainFormShutdownCoordinator shutdownCoordinator;
        private readonly YikeContextRuntime yikeContextRuntime;
        private AutoPlayColorMode lastManualAutoPlayColorMode = AutoPlayColorMode.ManualBlack;

        private static Boolean IsFoxSyncType(int syncType)
        {
            return syncType == TYPE_FOX || syncType == TYPE_FOX_BACKGROUND_PLACE;
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


        private void ApplySyncModeControlState()
        {
            if (CurrentSyncType != TYPE_YIKE)
                ClearYikeContext();
            ResetMainWindowTitle();
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
            if (!controlCenter.CanSendAutoPlayCommand(sessionCoordinator.KeepSync))
            {
                if (controlCenter.AutoPlayColorMode == AutoPlayColorMode.FoxAuto)
                    sessionCoordinator.RevokeAutoPlayIfAuthorized();
                return;
            }
            FoxWindowContext foxWindowContext = controlCenter.AutoPlayColorMode == AutoPlayColorMode.FoxAuto
                ? ResolveFoxWindowContext()
                : FoxWindowContext.Unknown();
            ResolveCurrentAutoPlayColor(foxWindowContext);
            controlCenter = controlCenterRuntime.Snapshot;
            if (!controlCenter.AutoPlayColorResolution.IsKnown)
            {
                sessionCoordinator.RevokeAutoPlayIfAuthorized();
                return;
            }

            sessionCoordinator.SendPlay(
                controlCenter.PlayColor,
                controlCenter.AutoPlayColorMode,
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

        private void ApplyAutoPlayColorMode(AutoPlayColorMode mode)
        {
            if (mode == AutoPlayColorMode.ManualBlack || mode == AutoPlayColorMode.ManualWhite)
                lastManualAutoPlayColorMode = mode;

            if (mode != AutoPlayColorMode.FoxAuto)
                ClearFoxAutoPlayColorDetectionState();
        }


        private AutoPlayColorResolution ResolveCurrentAutoPlayColor(FoxWindowContext foxWindowContext)
        {
            ControlCenterRuntimeSnapshot controlCenter = controlCenterRuntime.Snapshot;
            if (!controlCenter.AutoPlayEnabled)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);

            FoxIdentityRecognitionResult recognition = null;
            AutoPlayColorResolution detected = controlCenter.AutoPlayColorMode == AutoPlayColorMode.FoxAuto
                ? ResolveDetectedFoxAutoPlayColor(foxWindowContext, out recognition)
                : null;
            if (recognition == null)
            {
                controlCenterRuntime.UpdateAutoPlayObservation(
                    foxIdentitySelection.EffectiveIdentitySignature,
                    foxWindowContext,
                    detected);
            }
            else if (recognition.Accepted)
            {
                controlCenterRuntime.ApplyFoxIdentityRecognition(
                    foxIdentitySelection.EffectiveIdentitySignature,
                    foxWindowContext,
                    recognition);
            }
            return controlCenterRuntime.Snapshot.AutoPlayColorResolution;
        }

        private AutoPlayColorResolution ResolveDetectedFoxAutoPlayColor(
            FoxWindowContext foxWindowContext,
            out FoxIdentityRecognitionResult recognitionResult)
        {
            recognitionResult = null;
            string nicknameSignature = foxIdentitySelection.EffectiveIdentitySignature;
            FoxIdentityRoomSnapshot roomSnapshot = foxIdentitySelection.BeginRoomContext(foxWindowContext);
            long operationGeneration = roomSnapshot.OperationGeneration;
            if (!IsFoxSyncType(CurrentSyncType)
                || hwnd == IntPtr.Zero
                || string.IsNullOrWhiteSpace(nicknameSignature))
            {
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);
            }

            IntPtr captureHandle = ResolveFoxAutoPlayCaptureHandle(hwnd);
            AutoPlayColorResolution detection;
            if (captureHandle == IntPtr.Zero)
            {
                detection = AutoPlayColorResolution.Unknown(AutoPlayColorStatus.NicknameNotMatched);
            }
            else
            {
                string contextSignature = BuildFoxAutoPlayColorDetectionContextSignature(foxWindowContext);
                DateTime now = DateTime.UtcNow;
                if (lastFoxAutoPlayColorDetection != null
                    && lastFoxAutoPlayColorDetectionWindowHandle == captureHandle
                    && string.Equals(lastFoxAutoPlayColorDetectionContextSignature, contextSignature, StringComparison.Ordinal)
                    && string.Equals(lastFoxAutoPlayColorDetectionNicknameSignature, nicknameSignature, StringComparison.Ordinal)
                    && (now - lastFoxAutoPlayColorDetectionTimestampUtc).TotalMilliseconds < FoxAutoPlayColorDetectionCacheMs)
                {
                    detection = lastFoxAutoPlayColorDetection;
                }
                else
                {
                    using (Bitmap bitmap = foxAutoPlayCapturePlatform.CaptureWindow(captureHandle))
                    {
                        detection = FoxAutoPlayColorDetector.DetectPlayerListPanel(bitmap, nicknameSignature);
                    }

                    lastFoxAutoPlayColorDetection = detection;
                    lastFoxAutoPlayColorDetectionWindowHandle = captureHandle;
                    lastFoxAutoPlayColorDetectionContextSignature = contextSignature;
                    lastFoxAutoPlayColorDetectionNicknameSignature = nicknameSignature;
                    lastFoxAutoPlayColorDetectionTimestampUtc = now;
                }
            }

            FoxIdentityRecognitionResult recognition = foxIdentitySelection.ApplyRoomRecognition(
                operationGeneration,
                foxWindowContext,
                (SyncMode)CurrentSyncType,
                IsUniqueFoxIdentityMatch(detection),
                detection);
            recognitionResult = recognition;
            return recognition.Snapshot.DerivedAuthorization;
        }

        private static bool IsUniqueFoxIdentityMatch(AutoPlayColorResolution detection)
        {
            return detection != null
                && detection.Status != AutoPlayColorStatus.NicknameNotMatched
                && detection.Status != AutoPlayColorStatus.Unconfigured;
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



        private FoxIdentitySelectionResult ClearSavedFoxAutoPlayIdentity()
        {
            FoxIdentitySelectionResult result = foxIdentitySelection.ClearSaved();
            if (result.Accepted
                && result.PersistedIdentityChanged
                && string.IsNullOrWhiteSpace(foxIdentitySelection.CurrentProcessIdentitySignature))
            {
                ClearFoxAutoPlayColorDetectionState();
                controlCenterRuntime.UpdateAutoPlayObservation(
                    foxIdentitySelection.EffectiveIdentitySignature,
                    ResolveFoxWindowContext(),
                    null);
            }
            return result;
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
            if (foxIdentitySelection != null)
                foxIdentitySelection.ClearRoomRecognition();
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
                AutoPlayColorMode = runtimeSnapshot.AutoPlayColorMode,
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
            if (foxIdentitySelection != null)
                foxIdentitySelection.BeginRoomContext(lastFoxWindowContext);
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
                RunWithBatchedWebViewStatePublication(delegate
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
                RunWithBatchedWebViewStatePublication(delegate
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
                RunWithBatchedWebViewStatePublication(
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
                RunWithBatchedWebViewStatePublication(delegate
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
                RunWithBatchedWebViewStatePublication(delegate
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
                RunWithBatchedWebViewStatePublication(delegate
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
                RunWithBatchedWebViewStatePublication(delegate
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
            if (!SyncToolbarTextResolver.ShouldRestoreIdleUiAfterKeepSyncStop(continuousSyncActive))
            {
                ApplyMainWindowTitle();
                return;
            }
            ResetMainWindowTitle();
        }

        private void ApplyContinuousSyncStartedUi()
        {
            hasRetainedFoxTitleSnapshot = false;
            lastMainWindowTitleTurn = MainWindowTitleTurn.Unknown;
            RefreshMainWindowTitleFromCurrentWindow();
        }

        private void ApplyContinuousSyncStoppedUi()
        {
            if (sessionCoordinator.StartedSync)
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
            this.foxIdentitySelection = new FoxIdentitySelection(new AppConfigFoxIdentityPersistence());
            this.webViewStatePublisher = new WebViewStatePublisher(PostWebViewStateCore);
            this.webViewWindowCommandRuntime = new WebViewWindowCommandRuntime(
                new MainFormWebViewWindowAdapter(this));
            this.webViewUpdateCheckJourney = new WebViewUpdateCheckJourney(
                OnWebViewUpdateCheckObservation);
            this.shutdownCoordinator = new MainFormShutdownCoordinator(
                new MainFormShutdownActions(this));
            this.yikeContextRuntime = new YikeContextRuntime(
                new MainFormYikeContextAdapter(this));
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
                foxIdentitySelection.EffectiveIdentitySignature,
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
            ResetMainWindowTitle();
            InitializeWebViewShell();
            isInitializingProtocolState = false;
        }

        private String getLangStr(String itemName)
        {
            return Program.ResolveLanguageText(itemName);
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


        [DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);


        private void stopSync()
        {
            sessionCoordinator.StopSyncSession();
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
            }
            shutdownCoordinator.Execute(persistConfiguration, shutdownExceptions.Add);
            ThrowShutdownExceptions(shutdownExceptions);
        }

        private void RequestCloseAfterShutdown()
        {
            if (!IsHandleCreated)
            {
                closeRequestedBeforeHandle = true;
                return;
            }
            if (IsDisposed || Disposing)
                return;
            BeginInvoke((Action)Close);
        }

        private sealed class MainFormShutdownActions : IMainFormShutdownActions
        {
            private readonly MainForm owner;

            public MainFormShutdownActions(MainForm owner)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public void StopPlaceRequestQueue() { owner.placeRequestQueue.Stop(); }
            public void ClearPendingProtocolCommands() { owner.ClearPendingProtocolCommands(); }
            public void ResetTitle() { owner.ResetMainWindowTitle(); }
            public void PersistConfiguration() { owner.PersistConfiguration(); }
            public void DisposeInputHooks() { owner.DisposeInputHooks(); }
            public void SendShutdownProtocol() { owner.SendShutdownProtocol(); }
            public void DisposeBitmap() { Program.DisposeBitmap(); }
            public void StopCoordinator() { owner.sessionCoordinator.Stop(); }
            public void DisposeWebViewUpdateBridge() { owner.DisposeWebViewUpdateBridge(); }
            public void RequestClose() { owner.RequestCloseAfterShutdown(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!isShuttingDown
                && !IsDisposed
                && !Disposing
                && webView != null
                && !webViewWindowBoundsAppliedAfterHandle)
            {
                ApplySavedWebViewWindowBounds();
                webViewWindowBoundsAppliedAfterHandle = true;
            }
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


        public void sendVersion()
        {
            SendVersionCommand();
        }

        public void stopInBoard()
        {
            ControlCenterApplyResult result = ApplyControlCenterIntent(
                ControlCenterIntent.SetShowOnBoard(false));
            if (result.Outcome == ControlCenterApplyOutcome.Rejected)
                ProjectControlCenterState();
            else
                if (result.ShouldPublishSnapshot)
                    PostWebViewState();
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
