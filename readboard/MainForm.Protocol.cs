using System;

namespace readboard
{
    public partial class MainForm
    {
        void IProtocolCommandHost.DispatchProtocolCommand(Action command)
        {
            if (command == null)
                throw new ArgumentNullException("command");
            Action trackedCommand = delegate
            {
                MarkHostCommunicationEstablished();
                command();
            };
            if (TryDispatchProtocolCommand(trackedCommand))
                return;
            EnqueuePendingProtocolCommand(trackedCommand);
        }

        private void MarkHostCommunicationEstablished()
        {
            if (hostCommunicationEstablished)
                return;
            hostCommunicationEstablished = true;
            ApplyControlCenterSessionObservation(
                new ControlCenterSessionObservation(
                    controlCenterRuntime.CaptureSessionObservationGeneration())
                    .WithHostConnected(true)
                    .WithSemanticLog("INFO", "WebView_hostConnected"));
        }

        public void NotifyProtocolReady()
        {
            sessionCoordinator.NotifyReady(Program.playPonder);
            ApplyControlCenterSessionObservation(
                new ControlCenterSessionObservation(
                    controlCenterRuntime.CaptureSessionObservationGeneration())
                    .WithHostConnected(true)
                    .WithSemanticLog("INFO", "WebView_hostReadyLog"));
        }

        public void ReplayStartupProtocolState()
        {
            SendBothSyncStateChange();
            ControlCenterSessionState sessionState = controlCenterRuntime.CurrentSessionState;
            if (!string.IsNullOrWhiteSpace(sessionState.AiTimeValue))
                SendTimeChangedCommand();
            if (!string.IsNullOrWhiteSpace(sessionState.PlayoutsValue))
                SendPlayoutsChangedCommand();
            if (!string.IsNullOrWhiteSpace(sessionState.FirstPolicyValue))
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
                VerifyMove = Program.verifyMove,
                MoveVerifyMaxAttempts = Program.CurrentConfig.MoveVerifyMaxAttempts
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
                ApplyControlCenterSessionObservation(
                    new ControlCenterSessionObservation(
                        controlCenterRuntime.CaptureSessionObservationGeneration())
                        .WithYikeWindowContext(YikeWindowContext.Unknown()));
                return;
            }

            yikeContextRuntime.Apply(context);
        }

        private sealed class MainFormYikeContextAdapter : IYikeContextAdapter
        {
            private readonly MainForm owner;

            public MainFormYikeContextAdapter(MainForm owner)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public long CaptureObservationGeneration()
            {
                return owner.controlCenterRuntime.CaptureSessionObservationGeneration();
            }

            public void StoreContext(YikeWindowContext context)
            {
                owner.lastYikeWindowContext = context;
                if (owner.hwnd != IntPtr.Zero)
                    owner.lastYikeContextWindowHandle = owner.hwnd;
            }

            public void SetCoordinatorContext(YikeWindowContext context)
            {
                owner.sessionCoordinator.SetYikeContext(context);
            }

            public void ApplyTitle()
            {
                owner.ApplyMainWindowTitle();
            }

            public ControlCenterSessionObservationApplyResult ApplyObservation(
                ControlCenterSessionObservation observation)
            {
                return owner.ApplyControlCenterSessionObservation(observation);
            }
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

        void IProtocolCommandHost.HandleReadboardUpdatePackageV2Supported()
        {
            hostedUpdatePackageV2Supported = true;
        }

        void IProtocolCommandHost.HandleReadboardUpdateInstalling()
        {
            MarkWebViewHostedUpdateInstalling();
        }

        void IAnalysisStateProtocolHost.HandleAnalysisState(bool running)
        {
            ApplyControlCenterSessionObservation(
                new ControlCenterSessionObservation(
                    controlCenterRuntime.CaptureSessionObservationGeneration())
                    .WithAnalysisState(running, true));
        }

        void IProtocolCommandHost.HandleReadboardUpdateCancelled()
        {
            MarkWebViewHostedUpdateCancelled();
        }

        void IProtocolCommandHost.HandleReadboardUpdateFailed(string message)
        {
            MarkWebViewHostedUpdateFailed(message ?? string.Empty);
        }
    }
}
