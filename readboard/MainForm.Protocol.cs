using System;

namespace readboard
{
    public partial class MainForm
    {
        void IProtocolCommandHost.DispatchProtocolCommand(Action command)
        {
            if (command == null)
                throw new ArgumentNullException("command");
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(command);
                return;
            }
            command();
        }

        public void NotifyProtocolReady()
        {
            sessionCoordinator.NotifyReady(Program.playPonder);
        }

        public void ReplayStartupProtocolState()
        {
            SendBothSyncStateChange();
            if (!string.IsNullOrWhiteSpace(textBox1.Text))
                SendTimeChangedCommand();
            if (!string.IsNullOrWhiteSpace(textBox2.Text))
                SendPlayoutsChangedCommand();
            if (!string.IsNullOrWhiteSpace(textBox3.Text))
                SendFirstPolicyChangedCommand();
            SendPlayCommandIfSelected();
        }

        void IProtocolCommandHost.HandlePlaceRequest(MoveRequest request)
        {
            if (request == null)
                return;
            try
            {
                sessionCoordinator.HandlePlaceRequest(new MoveRequest
                {
                    X = request.X,
                    Y = request.Y,
                    VerifyMove = Program.verifyMove
                });
            }
            catch (Exception ex)
            {
                sessionCoordinator.SendError(ex.ToString());
            }
        }

        void IProtocolCommandHost.HandleLossFocus()
        {
            lossFocus();
        }

        void IProtocolCommandHost.HandleStopInBoardRequest()
        {
            stopInBoard();
        }

        void IProtocolCommandHost.HandleVersionRequest()
        {
            sessionCoordinator.SendVersion(Program.version);
        }

        void IProtocolCommandHost.HandleQuitRequest()
        {
            shutdown();
        }
    }
}
