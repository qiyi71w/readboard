using System;

namespace readboard
{
    internal sealed partial class ControlCenterRuntime
    {
        private readonly ISyncSessionCoordinator coordinator;
        private readonly FoxIdentitySelection identitySelection;
        private readonly FoxMatchBarLiveRecognition liveRecognition = new FoxMatchBarLiveRecognition();
        private bool initializing = true;
        private bool hasProjectedState;
        private IntPtr observedWindowHandle;

        public FoxIdentitySelectionSnapshot IdentitySnapshot
        {
            get { return identitySelection.Snapshot; }
        }

        public void CompleteInitialization()
        {
            initializing = false;
        }

        private void ProjectState(bool platformChanged)
        {
            if (platformChanged)
            {
                InvalidateWindowEvidence();
                ControlCenterSessionObservation reset = new ControlCenterSessionObservation(
                    BeginSessionObservationGeneration())
                    .ClearRuntimeFrame()
                    .WithTargetWindowValid(environment.TargetWindowValid)
                    .WithFoxWindowContext(FoxWindowContext.Unknown())
                    .WithTitleTurn(MainWindowTitleTurn.None);
                if (preferences.Platform != SyncMode.Yike)
                    reset = reset.WithYikeWindowContext(YikeWindowContext.Unknown());
                ApplyObservation(reset);
            }
            coordinator.SetSyncBoth(preferences.TwoWaySync);
            coordinator.SetSyncPlatform(ResolveSyncPlatform(preferences.Platform));
            environment.ProjectState(preferences.Clone(), sessionState.Clone(), platformChanged);
            hasProjectedState = true;
        }

        private static string ResolveSyncPlatform(SyncMode platform)
        {
            if (platform == SyncMode.Fox || platform == SyncMode.FoxBackgroundPlace)
                return "fox";
            return platform == SyncMode.Yike ? ProtocolKeywords.Yike : "generic";
        }

        private void ApplySessionEffects(
            ControlCenterPreferences previousPreferences,
            ControlCenterSessionState previousSession)
        {
            bool platformChanged = previousPreferences.Platform != preferences.Platform;
            bool twoWayChanged = previousPreferences.TwoWaySync != preferences.TwoWaySync;
            bool autoPlayChanged = previousSession.AutoPlayEnabled != sessionState.AutoPlayEnabled;
            bool colorChanged = previousSession.SelectedAutoPlayColorMode != sessionState.SelectedAutoPlayColorMode;
            if (!platformChanged && (autoPlayChanged || sessionState.SelectedAutoPlayColorMode != AutoPlayColorMode.FoxAuto))
                InvalidateWindowEvidence();
            ProjectState(platformChanged || !hasProjectedState);
            if (initializing)
                return;

            if (twoWayChanged)
            {
                SendBothSyncState();
                ResendSyncSessionState();
            }
            if (!platformChanged && previousPreferences.ShowOnBoard != preferences.ShowOnBoard)
            {
                if (preferences.Platform == SyncMode.Fox)
                    coordinator.SendForegroundFoxInBoard(preferences.ShowOnBoard && preferences.TwoWaySync);
                if (preferences.ShowOnBoard)
                {
                    if (environment.ShowInBoardHint)
                        environment.ShowOnBoardHint();
                }
                else
                    coordinator.SendNotInBoard();
            }
            if (autoPlayChanged)
            {
                if (sessionState.AutoPlayEnabled)
                    RequestAutoPlay();
                else
                    coordinator.SendStopAutoPlay();
            }
            else if ((colorChanged || previousPreferences.AutoPlayMoveMode != preferences.AutoPlayMoveMode)
                && sessionState.AutoPlayEnabled && coordinator.KeepSync)
                RequestAutoPlay();

            if (!string.Equals(previousSession.AiTimeValue, sessionState.AiTimeValue, StringComparison.Ordinal))
                coordinator.SendTimeChanged(AutoPlayWireIssuer.ToProtocolNumericValue(sessionState.AiTimeValue));
            if (!string.Equals(previousSession.PlayoutsValue, sessionState.PlayoutsValue, StringComparison.Ordinal))
                coordinator.SendPlayoutsChanged(AutoPlayWireIssuer.ToProtocolNumericValue(sessionState.PlayoutsValue));
            if (!string.Equals(previousSession.FirstPolicyValue, sessionState.FirstPolicyValue, StringComparison.Ordinal))
                coordinator.SendFirstPolicyChanged(AutoPlayWireIssuer.ToProtocolNumericValue(sessionState.FirstPolicyValue));
        }

        private void SendBothSyncState()
        {
            coordinator.SendBothSync(preferences.TwoWaySync);
            if (preferences.ShowOnBoard && preferences.Platform == SyncMode.Fox)
                coordinator.SendForegroundFoxInBoard(preferences.TwoWaySync);
        }

        public void ReplayStartupProtocolState()
        {
            if (initializing)
                return;
            SendBothSyncState();
            if (!string.IsNullOrWhiteSpace(sessionState.AiTimeValue))
                coordinator.SendTimeChanged(AutoPlayWireIssuer.ToProtocolNumericValue(sessionState.AiTimeValue));
            if (!string.IsNullOrWhiteSpace(sessionState.PlayoutsValue))
                coordinator.SendPlayoutsChanged(AutoPlayWireIssuer.ToProtocolNumericValue(sessionState.PlayoutsValue));
            if (!string.IsNullOrWhiteSpace(sessionState.FirstPolicyValue))
                coordinator.SendFirstPolicyChanged(AutoPlayWireIssuer.ToProtocolNumericValue(sessionState.FirstPolicyValue));
            RequestAutoPlay();
        }

        public void ResendSyncSessionState()
        {
            if (initializing || !coordinator.KeepSync)
                return;
            coordinator.SendSync();
            RequestAutoPlay();
        }

        public void RequestAutoPlay()
        {
            if (initializing)
                return;
            ControlCenterRuntimeSnapshot snapshot = BuildSnapshot();
            if (snapshot.CanSendAutoPlayCommand(coordinator.KeepSync))
            {
                ResolveAutoPlayColor(sessionState.SelectedAutoPlayColorMode == AutoPlayColorMode.FoxAuto
                    ? RefreshWindowContext()
                    : FoxWindowContext.Unknown());
                snapshot = BuildSnapshot();
            }
            AutoPlayWireIssuer.IssueIfAuthorized(snapshot, coordinator.KeepSync, coordinator);
        }

        public void InvalidateWindowEvidence()
        {
            liveRecognition.Invalidate();
            identitySelection.ClearRoomRecognition();
            sessionState.DetectedAutoPlayColor = null;
        }

        public FoxWindowContext RefreshWindowContext()
        {
            ControlCenterWindowFacts facts = environment.ReadWindow();
            FoxWindowContext context = FoxWindowContext.CopyOf(facts.Context);
            if (facts.BindingInvalidated || observedWindowHandle != facts.Handle
                || !string.Equals(BuildRecognitionContextSignature(sessionState.FoxWindowContext),
                    BuildRecognitionContextSignature(context), StringComparison.Ordinal))
                InvalidateWindowEvidence();
            observedWindowHandle = facts.Handle;
            sessionState.FoxWindowContext = context;
            sessionState.FoxAutoPlayNicknameSignature = identitySelection.EffectiveIdentitySignature;
            identitySelection.BeginRoomContext(context);
            return FoxWindowContext.CopyOf(context);
        }

        public AutoPlayColorResolution RefreshAutoPlayColor()
        {
            return ResolveAutoPlayColor(RefreshWindowContext());
        }

        private AutoPlayColorResolution ResolveAutoPlayColor(FoxWindowContext context)
        {
            if (!sessionState.AutoPlayEnabled)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);
            string signature = identitySelection.EffectiveIdentitySignature;
            if (sessionState.SelectedAutoPlayColorMode != AutoPlayColorMode.FoxAuto)
                UpdateAutoPlayObservation(signature, context, null);
            else
            {
                FoxIdentityRoomSnapshot room = identitySelection.BeginRoomContext(context);
                if ((preferences.Platform != SyncMode.Fox && preferences.Platform != SyncMode.FoxBackgroundPlace)
                    || observedWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(signature))
                    UpdateAutoPlayObservation(signature, context,
                        AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown));
                else
                {
                    AutoPlayColorResolution detection = SamplePlayers(context, false);
                    FoxIdentityRecognitionResult recognition = identitySelection.ApplyRoomRecognition(
                        room.OperationGeneration, context, preferences.Platform,
                        detection != null && detection.Status != AutoPlayColorStatus.NicknameNotMatched
                            && detection.Status != AutoPlayColorStatus.Unconfigured,
                        detection);
                    ApplyFoxIdentityRecognition(signature, context, recognition);
                }
            }
            return BuildSnapshot().AutoPlayColorResolution;
        }

        private AutoPlayColorResolution SamplePlayers(FoxWindowContext context, bool force)
        {
            string contextSignature = BuildRecognitionContextSignature(context);
            string identitySignature = identitySelection.EffectiveIdentitySignature;
            DateTime now = environment.UtcNow;
            if (!liveRecognition.NeedsSample(observedWindowHandle, contextSignature, identitySignature, now, force))
                return liveRecognition.CurrentResolution;
            return liveRecognition.AcceptSample(observedWindowHandle, contextSignature, identitySignature,
                now, environment.ReadPlayers(observedWindowHandle));
        }

        private static string BuildRecognitionContextSignature(FoxWindowContext context)
        {
            if (context == null)
                return string.Empty;
            if (context.Kind == FoxWindowKind.LiveRoom)
                return "live|state=" + (int)context.LiveRoomState + "|room=" + (context.RoomToken ?? string.Empty).Trim();
            if (context.Kind == FoxWindowKind.RecordView)
                return "record|current=" + (context.RecordCurrentMove.HasValue ? context.RecordCurrentMove.Value.ToString() : string.Empty)
                    + "|total=" + (context.RecordTotalMove.HasValue ? context.RecordTotalMove.Value.ToString() : string.Empty)
                    + "|end=" + (context.RecordAtEnd ? "1" : "0")
                    + "|fingerprint=" + (context.TitleFingerprint ?? string.Empty).Trim();
            return "kind=" + (int)context.Kind;
        }

        public FoxIdentitySelectionSnapshot OpenIdentity(bool firstAutomaticSelection = false)
        {
            if (preferences.Platform != SyncMode.Fox && preferences.Platform != SyncMode.FoxBackgroundPlace)
                return identitySelection.Snapshot;
            FoxWindowContext context = RefreshWindowContext();
            SamplePlayers(context, true);
            return identitySelection.Open(FoxMatchBarIdentityCandidates.Build(liveRecognition.CurrentReading.Players),
                firstAutomaticSelection);
        }

        public FoxIdentitySelectionResult SelectIdentity(string candidateId)
        {
            return identitySelection.Select(candidateId);
        }

        public FoxIdentitySelectionResult CancelIdentity()
        {
            return identitySelection.Cancel();
        }

        public FoxIdentitySelectionResult ConfirmIdentity(string candidateId, bool save)
        {
            FoxIdentitySelectionResult selection = identitySelection.Select(candidateId);
            if (selection.Outcome == FoxIdentitySelectionActionOutcome.Rejected)
                return selection;
            bool resumeAutomaticColor = identitySelection.IsFirstAutomaticSelectionPending;
            FoxIdentitySelectionResult result = save ? identitySelection.SaveAndUse() : identitySelection.UseOnce();
            if (!result.Accepted)
                return result;
            InvalidateWindowEvidence();
            UpdateAutoPlayObservation(identitySelection.EffectiveIdentitySignature, RefreshWindowContext(), null);
            ControlCenterApplyResult modeResult = resumeAutomaticColor
                ? Apply(ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.FoxAuto))
                : null;
            if (sessionState.SelectedAutoPlayColorMode == AutoPlayColorMode.FoxAuto)
            {
                RefreshAutoPlayColor();
                // A mode change owns the direct request even when its gate emits no wire.
                if ((modeResult == null || modeResult.Outcome != ControlCenterApplyOutcome.Changed)
                    && coordinator.KeepSync)
                    RequestAutoPlay();
            }
            return result;
        }

        public FoxIdentitySelectionResult ClearSavedIdentity()
        {
            FoxIdentitySelectionResult result = identitySelection.ClearSaved();
            if (result.Accepted && result.PersistedIdentityChanged
                && string.IsNullOrWhiteSpace(identitySelection.CurrentProcessIdentitySignature))
            {
                InvalidateWindowEvidence();
                UpdateAutoPlayObservation(identitySelection.EffectiveIdentitySignature, RefreshWindowContext(), null);
            }
            return result;
        }
    }
}
