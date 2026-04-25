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
            boardW = config.BoardWidth;
            boardH = config.BoardHeight;
            SetSyncBoth(config.SyncBoth);
            posX = config.WindowPosX;
            posY = config.WindowPosY;
            SetCurrentSyncType((int)config.SyncMode);
            ApplyBoardSelection(config);
            ApplySyncModeSelection();
            ApplySyncModeControlState();
            chkShowInBoard.Checked = Program.showInBoard;
            Program.showInBoard = chkShowInBoard.Checked;
            ApplySyncModeControlState();
        }

        public void PersistConfiguration()
        {
            Program.SaveAppConfig(BuildCurrentAppConfig());
        }

        private AppConfig BuildCurrentAppConfig()
        {
            AppConfig config = Program.CurrentContext.Config.Clone();
            int customBoardWidth;
            int customBoardHeight;
            Point persistedWindowLocation = ResolvePersistableWindowLocation();
            GetCustomBoardDimensions(out customBoardWidth, out customBoardHeight);
            config.BoardWidth = boardW;
            config.BoardHeight = boardH;
            config.CustomBoardWidth = customBoardWidth;
            config.CustomBoardHeight = customBoardHeight;
            config.SyncBoth = sessionCoordinator.SyncBoth;
            config.SyncMode = (SyncMode)CurrentSyncType;
            config.WindowPosX = persistedWindowLocation.X;
            config.WindowPosY = persistedWindowLocation.Y;
            return config;
        }

        private Point ResolvePersistableWindowLocation()
        {
            Rectangle boundsToPersist =
                WindowState == FormWindowState.Normal && Bounds.Width > 0 && Bounds.Height > 0
                    ? Bounds
                    : RestoreBounds;
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

        private void ApplyBoardSelection(AppConfig config)
        {
            if (boardW == boardH)
            {
                ApplySquareBoardSelection(config);
                return;
            }

            txtBoardWidth.Text = boardW.ToString();
            txtBoardHeight.Text = boardH.ToString();
            rdoOtherBoard.Checked = true;
        }

        private void ApplySquareBoardSelection(AppConfig config)
        {
            if (boardW == 19)
            {
                rdo19x19.Checked = true;
                ApplyCustomBoardText(config);
                return;
            }
            if (boardW == 13)
            {
                rdo13x13.Checked = true;
                ApplyCustomBoardText(config);
                return;
            }
            if (boardW == 9)
            {
                rdo9x9.Checked = true;
                ApplyCustomBoardText(config);
                return;
            }

            txtBoardWidth.Text = boardW.ToString();
            txtBoardHeight.Text = boardH.ToString();
            rdoOtherBoard.Checked = true;
        }

        private void ApplyCustomBoardText(AppConfig config)
        {
            if (config.CustomBoardWidth > 0)
                txtBoardWidth.Text = config.CustomBoardWidth.ToString();
            if (config.CustomBoardHeight > 0)
                txtBoardHeight.Text = config.CustomBoardHeight.ToString();
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

        private void GetCustomBoardDimensions(out int customBoardWidth, out int customBoardHeight)
        {
            customBoardWidth = -1;
            customBoardHeight = -1;
            int parsedValue;
            if (int.TryParse(txtBoardWidth.Text, out parsedValue))
                customBoardWidth = parsedValue;
            if (int.TryParse(txtBoardHeight.Text, out parsedValue))
                customBoardHeight = parsedValue;
        }
    }
}
