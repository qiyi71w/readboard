using System;

namespace readboard
{
    public partial class MainForm
    {
        void IProtocolCommandHost.DispatchProtocolCommand(Action command)
        {
            if (command == null)
                throw new ArgumentNullException("command");
            if (TryDispatchProtocolCommand(command))
                return;
            EnqueuePendingProtocolCommand(command);
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
            MoveRequest protocolMove = new MoveRequest
            {
                X = request.X,
                Y = request.Y,
                VerifyMove = Program.verifyMove
            };
            EnqueuePlaceRequest(protocolMove);
        }

        private void EnqueuePlaceRequest(MoveRequest request)
        {
            if (request == null)
                return;
            placeRequestQueue.TryEnqueue(delegate
            {
                ExecutePlaceRequest(request);
            });
        }

        private void ExecutePlaceRequest(MoveRequest request)
        {
            try
            {
                PlaceRequestExecutionResult result = sessionCoordinator.HandlePlaceRequest(request);
                if (!result.ShouldSendResponse)
                    return;
                TrySendPlaceProtocolResult(result.Success);
            }
            catch (Exception ex)
            {
                try
                {
                    TrySendPlaceProtocolError(ex.ToString());
                }
                catch (Exception sendErrorException)
                {
                    System.Diagnostics.Trace.TraceError(ex.ToString());
                    System.Diagnostics.Trace.TraceError(sendErrorException.ToString());
                }
            }
        }

        private bool TrySendPlaceProtocolMessage(Action sendAction)
        {
            if (sendAction == null)
                throw new ArgumentNullException("sendAction");

            lock (placeProtocolSyncRoot)
            {
                if (isShuttingDown)
                    return false;

                sendAction();
                return true;
            }
        }

        private bool TrySendPlaceProtocolResult(bool success)
        {
            return TrySendPlaceProtocolMessage(delegate
            {
                SendPlacementResultCommand(success);
            });
        }

        private bool TrySendPlaceProtocolError(string message)
        {
            return TrySendPlaceProtocolMessage(delegate
            {
                sessionCoordinator.SendError(message);
            });
        }

        void IProtocolCommandHost.HandleYikeContext(YikeWindowContext context)
        {
            if (CurrentSyncType != TYPE_YIKE)
            {
                ClearYikeContext();
                ApplyMainWindowTitle();
                return;
            }

            lastYikeWindowContext = YikeWindowContext.CopyOf(context);
            if (hwnd != IntPtr.Zero)
                lastYikeContextWindowHandle = hwnd;
            sessionCoordinator.SetYikeContext(lastYikeWindowContext);
            ApplyMainWindowTitle();
        }

        void IProtocolCommandHost.HandleYikeGeometry(YikeBoardGeometry geometry)
        {
            if (CurrentSyncType != TYPE_YIKE)
            {
                sessionCoordinator.SetYikeGeometry(null);
                return;
            }

            sessionCoordinator.SetYikeGeometry(geometry);
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

        void IProtocolCommandHost.HandleReadboardUpdateSupported()
        {
            hostedUpdateSupported = true;
        }

        void IProtocolCommandHost.HandleReadboardUpdateInstalling()
        {
            FormUpdate dialog = activeHostedUpdateDialog;
            if (dialog != null && !dialog.IsDisposed)
                dialog.MarkHostedInstalling();
        }

        void IProtocolCommandHost.HandleReadboardUpdateCancelled()
        {
            FormUpdate dialog = activeHostedUpdateDialog;
            if (dialog != null && !dialog.IsDisposed)
                dialog.MarkHostedCancelled();
        }

        void IProtocolCommandHost.HandleReadboardUpdateFailed(string message)
        {
            FormUpdate dialog = activeHostedUpdateDialog;
            if (dialog != null && !dialog.IsDisposed)
                dialog.MarkHostedFailed(message ?? string.Empty);
        }
    }
}
