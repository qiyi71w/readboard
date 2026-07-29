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

                form.suppressControlCenterProjectionEvents = true;
                try
                {
                    if (!hasAppliedPlatform || appliedPlatform != preferences.Platform)
                    {
                        form.ClearFoxAutoPlayColorDetectionState();
                        form.ResetWebViewSyncState();
                    }
                    form.ApplyControlCenterBoardSelection(preferences);
                    form.ApplySyncModeSelection();
                    form.ApplySyncModeControlState();
                    form.ApplyControlCenterNativeEnablement();
                    hasAppliedPlatform = true;
                    appliedPlatform = preferences.Platform;
                }
                finally
                {
                    form.suppressControlCenterProjectionEvents = false;
                }

                form.sessionCoordinator.SetSyncPlatform(MainForm.ResolveSyncPlatform(preferences.Platform));
                form.ApplyMainWindowTitle();
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
