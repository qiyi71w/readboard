using System;

namespace readboard
{
    public partial class MainForm
    {
        private void ApplyLoadedConfiguration()
        {
            AppConfig config = Program.CurrentContext.Config;
            ProjectControlCenterState();
            posX = config.WindowPosX;
            posY = config.WindowPosY;
        }

        public void PersistConfiguration()
        {
            try
            {
                Program.SaveAppConfig(BuildCurrentAppConfig());
                controlCenterRuntime.MarkPersistenceSucceeded();
            }
            catch (Exception exception)
            {
                controlCenterRuntime.MarkPersistenceFailed(exception);
                throw;
            }
        }

        private AppConfig BuildCurrentAppConfig()
        {
            AppConfig config = Program.CurrentContext.Config.Clone();
            ControlCenterPreferences preferences = controlCenterRuntime.CurrentPreferences;
            config.BoardWidth = preferences.BoardWidth;
            config.BoardHeight = preferences.BoardHeight;
            config.CustomBoardWidth = preferences.CustomBoardWidth;
            config.CustomBoardHeight = preferences.CustomBoardHeight;
            config.SyncBoth = preferences.TwoWaySync;
            config.ShowInBoard = preferences.ShowOnBoard;
            config.SyncMode = preferences.Platform;
            config.AutoPlayColorMode = preferences.AutoPlayColorMode;
            config.AutoPlayMoveMode = preferences.AutoPlayMoveMode;
            webViewWindowModeRuntime.WriteTo(config);
            return config;
        }
    }
}
