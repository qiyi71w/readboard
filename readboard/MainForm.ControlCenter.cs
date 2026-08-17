using System;

namespace readboard
{
    public partial class MainForm
    {
        private sealed class MainFormControlCenterSessionAdapter : IControlCenterSessionAdapter
        {
            private readonly MainForm form;
            private bool hasAppliedPlatform;
            private SyncMode appliedPlatform;
            private bool hasAppliedPreferences;
            private bool appliedTwoWaySync;
            private bool appliedShowOnBoard;
            private bool hasAppliedSession;
            private bool appliedAutoPlayEnabled;
            private AutoPlayColorMode appliedAutoPlayColorMode;
            private AutoPlayMoveMode appliedAutoPlayMoveMode;
            private string appliedAiTimeValue;
            private string appliedPlayoutsValue;
            private string appliedFirstPolicyValue;

            public MainFormControlCenterSessionAdapter(MainForm form)
            {
                this.form = form ?? throw new ArgumentNullException("form");
            }

            public bool HasActiveSyncOperation
            {
                get { return form.HasActiveSyncOperation(); }
            }

            public void Apply(
                ControlCenterPreferences preferences,
                ControlCenterSessionState sessionState)
            {
                if (preferences == null)
                    throw new ArgumentNullException("preferences");
                if (sessionState == null)
                    throw new ArgumentNullException("sessionState");

                bool platformChanged = !hasAppliedPlatform || appliedPlatform != preferences.Platform;
                bool twoWaySyncChanged = hasAppliedPreferences
                    && appliedTwoWaySync != preferences.TwoWaySync;
                bool showOnBoardChanged = hasAppliedPreferences
                    && appliedShowOnBoard != preferences.ShowOnBoard;
                bool autoPlayChanged = hasAppliedSession
                    && appliedAutoPlayEnabled != sessionState.AutoPlayEnabled;
                bool autoPlayColorChanged = hasAppliedPreferences
                    && appliedAutoPlayColorMode != preferences.AutoPlayColorMode;
                bool autoPlayMoveModeChanged = hasAppliedPreferences
                    && appliedAutoPlayMoveMode != preferences.AutoPlayMoveMode;
                bool aiTimeChanged = hasAppliedSession
                    && !string.Equals(appliedAiTimeValue, sessionState.AiTimeValue, StringComparison.Ordinal);
                bool playoutsChanged = hasAppliedSession
                    && !string.Equals(appliedPlayoutsValue, sessionState.PlayoutsValue, StringComparison.Ordinal);
                bool firstPolicyChanged = hasAppliedSession
                    && !string.Equals(appliedFirstPolicyValue, sessionState.FirstPolicyValue, StringComparison.Ordinal);

                if (platformChanged)
                {
                    form.ClearFoxAutoPlayColorDetectionState();
                    form.ResetWebViewSyncState();
                }
                if (autoPlayChanged && !sessionState.AutoPlayEnabled)
                    form.ClearFoxAutoPlayColorDetectionState();
                if (!hasAppliedPreferences || twoWaySyncChanged)
                    form.SetSyncBoth(preferences.TwoWaySync);
                form.ApplyAutoPlayColorMode(preferences.AutoPlayColorMode);
                if (platformChanged)
                    form.ApplySyncModeControlState();
                hasAppliedPlatform = true;
                appliedPlatform = preferences.Platform;
                hasAppliedPreferences = true;
                appliedTwoWaySync = preferences.TwoWaySync;
                appliedShowOnBoard = preferences.ShowOnBoard;
                hasAppliedSession = true;
                appliedAutoPlayEnabled = sessionState.AutoPlayEnabled;
                appliedAutoPlayColorMode = preferences.AutoPlayColorMode;
                appliedAutoPlayMoveMode = preferences.AutoPlayMoveMode;
                appliedAiTimeValue = sessionState.AiTimeValue;
                appliedPlayoutsValue = sessionState.PlayoutsValue;
                appliedFirstPolicyValue = sessionState.FirstPolicyValue;

                form.sessionCoordinator.SetSyncPlatform(MainForm.ResolveSyncPlatform(preferences.Platform));
                form.ApplyMainWindowTitle();
                if (!form.isInitializingProtocolState)
                {
                    if (twoWaySyncChanged)
                        form.ApplyControlCenterTwoWaySyncEffect();
                    if (showOnBoardChanged && !platformChanged)
                        form.ApplyControlCenterShowOnBoardEffect(preferences.ShowOnBoard);
                    if (autoPlayChanged)
                    {
                        if (sessionState.AutoPlayEnabled)
                            form.SendPlayCommandIfSelected();
                        else
                            form.SendStopAutoPlayCommand();
                    }
                    else if ((autoPlayColorChanged || autoPlayMoveModeChanged)
                        && sessionState.AutoPlayEnabled
                        && form.sessionCoordinator.KeepSync)
                    {
                        form.SendPlayCommandIfSelected();
                    }
                    if (aiTimeChanged)
                        form.SendTimeChangedCommand();
                    if (playoutsChanged)
                        form.SendPlayoutsChangedCommand();
                    if (firstPolicyChanged)
                        form.SendFirstPolicyChangedCommand();
                }
            }
        }

        private sealed class MainFormControlCenterActionAdapter : IControlCenterActionAdapter
        {
            private readonly MainForm form;

            public MainFormControlCenterActionAdapter(MainForm form)
            {
                this.form = form ?? throw new ArgumentNullException("form");
            }

            public ControlCenterActionExecutionOutcome Execute(ControlCenterActionEffect effect)
            {
                if (effect == null)
                    throw new ArgumentNullException("effect");

                switch (effect.Kind)
                {
                    case ControlCenterActionEffectKind.StartQuickSync:
                        return form.sessionCoordinator.TryStartContinuousSync()
                            ? ControlCenterActionExecutionOutcome.Applied
                            : ControlCenterActionExecutionOutcome.Rejected;
                    case ControlCenterActionEffectKind.StopSync:
                        form.stopSync();
                        return ControlCenterActionExecutionOutcome.Applied;
                    case ControlCenterActionEffectKind.StartContinuousSync:
                        return form.sessionCoordinator.TryStartKeepSync()
                            ? ControlCenterActionExecutionOutcome.Applied
                            : ControlCenterActionExecutionOutcome.Rejected;
                    case ControlCenterActionEffectKind.RunOneTimeSync:
                        return form.TryRunOneTimeSyncAction()
                            ? ControlCenterActionExecutionOutcome.Applied
                            : ControlCenterActionExecutionOutcome.Rejected;
                    case ControlCenterActionEffectKind.ResumeAnalysis:
                        form.sessionCoordinator.SendResumePonder();
                        return ControlCenterActionExecutionOutcome.Applied;
                    case ControlCenterActionEffectKind.PauseAnalysis:
                        form.sessionCoordinator.SendNoPonder();
                        return ControlCenterActionExecutionOutcome.Applied;
                    case ControlCenterActionEffectKind.SwapOrder:
                        form.SendPassCommand();
                        return ControlCenterActionExecutionOutcome.Applied;
                    case ControlCenterActionEffectKind.ForceRebuild:
                        form.ArmForceRebuildAction();
                        return ControlCenterActionExecutionOutcome.Applied;
                    case ControlCenterActionEffectKind.ClearBoard:
                        form.SendClearCommand();
                        return ControlCenterActionExecutionOutcome.Applied;
                    case ControlCenterActionEffectKind.SelectBoard:
                        form.ApplyNativeBoardSelection(effect.BoardSelectionMode);
                        return ControlCenterActionExecutionOutcome.Applied;
                    default:
                        throw new ArgumentOutOfRangeException("effect");
                }
            }
        }


        private ControlCenterApplyResult ApplyControlCenterIntent(ControlCenterIntent intent)
        {
            return webViewStatePublisher.Suppress(
                delegate { return controlCenterRuntime.Apply(intent); });
        }

        private ControlCenterActionApplyResult ApplyControlCenterAction(ControlCenterActionIntent intent)
        {
            return webViewStatePublisher.Suppress(
                delegate { return controlCenterRuntime.ApplyAction(intent); });
        }


        private void ApplyControlCenterTwoWaySyncEffect()
        {
            ControlCenterPreferences preferences = controlCenterRuntime.CurrentPreferences;
            foreach (ControlCenterSessionEffect effect in ControlCenterSessionEffectPlanner.PlanTwoWaySync(
                preferences,
                CanUseForegroundFoxInBoardProtocol()))
            {
                ApplyControlCenterSessionEffect(effect);
            }
        }

        private void ApplyControlCenterShowOnBoardEffect(bool enabled)
        {
            ControlCenterPreferences preferences = controlCenterRuntime.CurrentPreferences;
            foreach (ControlCenterSessionEffect effect in ControlCenterSessionEffectPlanner.PlanShowOnBoard(
                enabled,
                preferences.TwoWaySync,
                CanUseForegroundFoxInBoardProtocol(),
                Program.showInBoardHint))
            {
                ApplyControlCenterSessionEffect(effect);
            }
        }

        private void ApplyControlCenterSessionEffect(ControlCenterSessionEffect effect)
        {
            switch (effect.Kind)
            {
                case ControlCenterSessionEffectKind.SendBothSync:
                    SendBothSyncCommand(effect.Enabled);
                    return;
                case ControlCenterSessionEffectKind.SendForegroundFoxInBoard:
                    SendForegroundFoxInBoardCommand(effect.Enabled);
                    return;
                case ControlCenterSessionEffectKind.SendNotInBoard:
                    SendNotInBoardCommand();
                    return;
                case ControlCenterSessionEffectKind.ShowOnBoardHint:
                    webViewSettingsDialog = CreateWebViewDialog("showInBoardHint");
                    return;
                case ControlCenterSessionEffectKind.ResendSyncSessionState:
                    ResendSyncSessionState();
                    return;
                default:
                    throw new ArgumentOutOfRangeException("effect");
            }
        }

        private void ProjectControlCenterState()
        {
            webViewStatePublisher.Suppress(controlCenterRuntime.ProjectCurrentState);
        }

        private void RunWithBatchedWebViewStatePublication(Action action)
        {
            webViewStatePublisher.Batch(action);
        }

        private ControlCenterSessionObservationApplyResult ApplyControlCenterSessionObservation(
            ControlCenterSessionObservation observation)
        {
            ControlCenterSessionObservationApplyResult result = controlCenterRuntime.ApplyObservation(observation);
            if (result.Outcome != ControlCenterSessionObservationApplyOutcome.Applied)
                return result;

            ProjectControlCenterState();
            for (int i = 0; i < result.SemanticMessages.Count; i++)
            {
                SemanticMessage message = result.SemanticMessages[i];
                AddWebViewSemanticLog(message.Level, message);
            }
            if (result.ShouldPublishSnapshot)
                PostWebViewState();
            return result;
        }
    }
}
