using System;

namespace readboard
{
    public partial class MainForm
    {
        private sealed class MainFormControlCenterEnvironment : IControlCenterEnvironment
        {
            private readonly MainForm form;

            public MainFormControlCenterEnvironment(MainForm form)
            {
                this.form = form ?? throw new ArgumentNullException("form");
            }

            public bool HasActiveSyncOperation { get { return form.HasActiveSyncOperation(); } }
            public bool? TargetWindowValid { get { return form.hwnd == IntPtr.Zero ? (bool?)null : IsWindow(form.hwnd); } }
            public bool ShowInBoardHint { get { return Program.showInBoardHint; } }
            public DateTime UtcNow { get { return DateTime.UtcNow; } }

            public ControlCenterWindowFacts ReadWindow()
            {
                return form.ReadFoxWindowFacts();
            }

            public FoxMatchBarReading ReadPlayers(IntPtr windowHandle, FoxWindowContext context)
            {
                return FoxMatchBarWindowsReader.TryRead(windowHandle, context);
            }

            public FoxMatchBarReading DiscoverIdentityCandidates(IntPtr windowHandle)
            {
                return FoxMatchBarWindowsReader.DiscoverIdentityCandidates(windowHandle);
            }

            public void ProjectState(ControlCenterPreferences preferences, ControlCenterSessionState sessionState, bool platformChanged)
            {
                if (platformChanged)
                {
                    if (preferences.Platform != SyncMode.Yike)
                        form.ClearYikeContext();
                    form.ResetMainWindowTitleProjection();
                }
                form.ApplyMainWindowTitle();
            }

            public void ShowOnBoardHint()
            {
                form.webViewSettingsDialog = CreateWebViewDialog("showInBoardHint");
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
