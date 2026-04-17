using System;
using System.Drawing;

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
            if (posX != -1 && posY != -1)
            {
                Point desiredLocation = new Point(posX, posY);
                Location = ClampToScreenWorkingArea(desiredLocation, Size);
            }
            chkShowInBoard.Checked = Program.showInBoard;
            Program.showInBoard = chkShowInBoard.Checked;
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
            GetCustomBoardDimensions(out customBoardWidth, out customBoardHeight);
            config.BoardWidth = boardW;
            config.BoardHeight = boardH;
            config.CustomBoardWidth = customBoardWidth;
            config.CustomBoardHeight = customBoardHeight;
            config.SyncBoth = sessionCoordinator.SyncBoth;
            config.SyncMode = (SyncMode)CurrentSyncType;
            config.WindowPosX = Location.X;
            config.WindowPosY = Location.Y;
            return config;
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
