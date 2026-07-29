using System;
using System.Drawing;
using System.Windows.Forms;

namespace readboard
{
    public partial class MainForm
    {
        private void ApplyLoadedConfiguration()
        {
            AppConfig config = Program.CurrentContext.Config;
            ControlCenterPreferences preferences = controlCenterRuntime.CurrentPreferences;
            ProjectControlCenterState();
            posX = config.WindowPosX;
            posY = config.WindowPosY;
            ApplyAutoPlayColorMode(preferences.AutoPlayColorMode);
            ApplyAutoPlayMoveMode(preferences.AutoPlayMoveMode);
            ApplySyncModeControlState();
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
            Rectangle persistedWindowBounds = ResolvePersistableWindowBounds();
            Point persistedWindowLocation = ResolvePersistableWindowLocation(persistedWindowBounds);
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
            config.WindowPosX = persistedWindowLocation.X;
            config.WindowPosY = persistedWindowLocation.Y;
            Size persistedWindowClientSize = ResolvePersistableWindowClientSize(persistedWindowBounds);
            Size logicalWindowSize = WebViewWindowLayoutPolicy.UnscalePhysicalSize(
                persistedWindowClientSize,
                DeviceDpi);
            config.WindowClientWidth = Math.Max(AppConfig.MinimumWindowClientWidth, logicalWindowSize.Width);
            config.WindowClientHeight = Math.Max(AppConfig.MinimumWindowClientHeight, logicalWindowSize.Height);
            config.WindowMaximized = WindowState == FormWindowState.Maximized;
            return config;
        }

        private Rectangle ResolvePersistableWindowBounds()
        {
            return
                WindowState == FormWindowState.Normal && Bounds.Width > 0 && Bounds.Height > 0
                    ? Bounds
                    : RestoreBounds;
        }

        private Size ResolvePersistableWindowClientSize(Rectangle persistedWindowBounds)
        {
            if (WindowState == FormWindowState.Normal && ClientSize.Width > 0 && ClientSize.Height > 0)
                return ClientSize;

            Size nonClientSize = SizeFromClientSize(Size.Empty);
            return ResolveClientSizeFromOuterBounds(persistedWindowBounds.Size, nonClientSize);
        }

        internal static Size ResolveClientSizeFromOuterBounds(Size outerSize, Size nonClientSize)
        {
            return new Size(
                Math.Max(0, outerSize.Width - nonClientSize.Width),
                Math.Max(0, outerSize.Height - nonClientSize.Height));
        }

        private static Point ResolvePersistableWindowLocation(Rectangle boundsToPersist)
        {
            Point location = boundsToPersist.Location;
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            if (location.X <= -16000
                || location.Y <= -16000
                || boundsToPersist.Width <= 0
                || boundsToPersist.Height <= 0
                || virtualScreen.Width <= 0
                || virtualScreen.Height <= 0)
                return new Point(-1, -1);
            if (!virtualScreen.Contains(location))
                return new Point(-1, -1);
            return location;
        }

        private void ApplySyncModeSelection()
        {
            switch (CurrentSyncType)
            {
                case TYPE_FOX:
                    rdoFox.Checked = true;
                    return;
                case TYPE_TYGEM:
                    rdoTygem.Checked = true;
                    return;
                case TYPE_SINA:
                    rdoSina.Checked = true;
                    return;
                case TYPE_FOX_BACKGROUND_PLACE:
                    rdoFoxBack.Checked = true;
                    return;
                case TYPE_FOREGROUND:
                    rdoFore.Checked = true;
                    return;
                case TYPE_YIKE:
                    rdoYike.Checked = true;
                    return;
            }
            rdoBack.Checked = true;
        }

    }
}
