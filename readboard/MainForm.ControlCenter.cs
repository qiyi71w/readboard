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

            public MainFormControlCenterSessionAdapter(MainForm form)
            {
                this.form = form ?? throw new ArgumentNullException("form");
            }

            public bool HasActiveSyncOperation
            {
                get { return form.HasActiveSyncOperation(); }
            }

            public void Apply(ControlCenterPreferences preferences)
            {
                if (preferences == null)
                    throw new ArgumentNullException("preferences");

                bool platformChanged = !hasAppliedPlatform || appliedPlatform != preferences.Platform;
                bool twoWaySyncChanged = hasAppliedPreferences
                    && appliedTwoWaySync != preferences.TwoWaySync;
                bool showOnBoardChanged = hasAppliedPreferences
                    && appliedShowOnBoard != preferences.ShowOnBoard;

                form.suppressControlCenterProjectionEvents = true;
                try
                {
                    if (platformChanged)
                    {
                        form.ClearFoxAutoPlayColorDetectionState();
                        form.ResetWebViewSyncState();
                    }
                    form.ApplyControlCenterBoardSelection(preferences);
                    if (platformChanged)
                        form.ApplySyncModeSelection();
                    if (!hasAppliedPreferences || twoWaySyncChanged)
                        form.SetSyncBoth(preferences.TwoWaySync);
                    form.chkBothSync.Checked = preferences.TwoWaySync;
                    form.chkAutoPlay.Enabled = preferences.TwoWaySync;
                    form.chkShowInBoard.Checked = preferences.ShowOnBoard;
                    if (platformChanged)
                        form.ApplySyncModeControlState();
                    form.ApplyControlCenterNativeEnablement();
                    hasAppliedPlatform = true;
                    appliedPlatform = preferences.Platform;
                    hasAppliedPreferences = true;
                    appliedTwoWaySync = preferences.TwoWaySync;
                    appliedShowOnBoard = preferences.ShowOnBoard;
                }
                finally
                {
                    form.suppressControlCenterProjectionEvents = false;
                }

                form.sessionCoordinator.SetSyncPlatform(MainForm.ResolveSyncPlatform(preferences.Platform));
                form.ApplyMainWindowTitle();
                if (!form.isInitializingProtocolState)
                {
                    if (twoWaySyncChanged)
                        form.ApplyControlCenterTwoWaySyncEffect();
                    if (showOnBoardChanged && !platformChanged)
                        form.ApplyControlCenterShowOnBoardEffect(preferences.ShowOnBoard);
                }
            }
        }

        private void ApplyControlCenterBoardSelection(ControlCenterPreferences preferences)
        {
            if (preferences.BoardSizeKind == ControlCenterBoardSizeKind.Custom)
            {
                txtBoardWidth.Text = preferences.BoardWidth.ToString();
                txtBoardHeight.Text = preferences.BoardHeight.ToString();
            }
            else
            {
                if (preferences.CustomBoardWidth > 0)
                    txtBoardWidth.Text = preferences.CustomBoardWidth.ToString();
                if (preferences.CustomBoardHeight > 0)
                    txtBoardHeight.Text = preferences.CustomBoardHeight.ToString();
            }
            switch (preferences.BoardSizeKind)
            {
                case ControlCenterBoardSizeKind.Preset19:
                    rdo19x19.Checked = true;
                    break;
                case ControlCenterBoardSizeKind.Preset13:
                    rdo13x13.Checked = true;
                    break;
                case ControlCenterBoardSizeKind.Preset9:
                    rdo9x9.Checked = true;
                    break;
                case ControlCenterBoardSizeKind.Custom:
                    rdoOtherBoard.Checked = true;
                    break;
                default:
                    rdo19x19.Checked = true;
                    break;
            }
        }

        private ControlCenterApplyResult ApplyControlCenterIntent(ControlCenterIntent intent)
        {
            suppressWebViewStatePublication = true;
            try
            {
                return controlCenterRuntime.Apply(intent);
            }
            finally
            {
                suppressWebViewStatePublication = false;
            }
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
                    webViewSettingsDialog = new ReadBoardDialogUiState { Open = true, Kind = "showInBoardHint" };
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
            suppressWebViewStatePublication = true;
            try
            {
                controlCenterRuntime.ProjectCurrentState();
            }
            finally
            {
                suppressWebViewStatePublication = false;
            }
        }
    }
}
