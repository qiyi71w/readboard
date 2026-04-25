using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class SyncSessionCoordinatorCloseFlowTests
    {
        [Fact]
        public void QuitMessages_DispatchOnlyWhileCoordinatorIsStarted()
        {
            RecordingTransport transport = new RecordingTransport();
            RecordingHost host = new RecordingHost();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.AttachHost(host);

            coordinator.Start();
            transport.Emit("quit");
            coordinator.Stop();
            transport.Emit("quit");

            Assert.Equal(1, host.QuitCount);
        }

        [Fact]
        public async Task Stop_CancelsPendingMoveWaiters()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.BeginKeepSync();
            coordinator.SetSyncBoth(true);
            coordinator.TryQueuePendingMove(new MoveRequest { X = 1, Y = 1 }, 19, 19);

            Task<bool> waitTask = Task.Run(() => coordinator.WaitForPendingMoveResult());
            coordinator.Stop();

            bool result = await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(result);
        }

        [Fact]
        public void Stop_DropsQueuedInboundProtocolCommands()
        {
            RecordingTransport transport = new RecordingTransport();
            DeferredDispatchHost host = new DeferredDispatchHost();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.AttachHost(host);

            coordinator.Start();
            transport.Emit("quit");

            Assert.NotNull(host.PendingCommand);

            coordinator.Stop();
            host.RunPendingCommand();

            Assert.Equal(0, host.QuitCount);
        }

        [Fact]
        public void SendShutdownProtocol_ClosesOutboundProtocolAfterShutdownSequence()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SendLine("re=123");
            coordinator.SendShutdownProtocol();
            coordinator.SendLine("tail-after-shutdown");
            coordinator.Stop();
            coordinator.SendLine("tail-after-stop");

            Assert.Equal(
                new[]
                {
                    "re=123",
                    "stopsync",
                    "nobothSync",
                    "endsync"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendShutdownProtocol_AndStop_CloseErrorChannel()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SendError("before-shutdown");
            coordinator.SendShutdownProtocol();
            coordinator.SendError("after-shutdown");
            coordinator.Stop();
            coordinator.SendError("after-stop");

            Assert.Equal(new[] { "before-shutdown" }, transport.ErrorMessages);
        }

        [Fact]
        public void Dispose_IsIdempotentAndStopsTransportOnce()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.Start();
            coordinator.Dispose();
            coordinator.Dispose();

            Assert.Equal(1, transport.StopCount);
        }

        [Fact]
        public void Stop_AfterDispose_IsANoOp()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.Start();
            coordinator.Dispose();
            coordinator.Stop();

            Assert.Equal(1, transport.StopCount);
        }

        private sealed class RecordingTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived;

            public bool IsConnected { get; private set; }
            public List<string> SentLines { get; } = new List<string>();
            public List<string> ErrorMessages { get; } = new List<string>();
            public int StopCount { get; private set; }

            public void Dispose()
            {
            }

            public void Emit(string rawLine)
            {
                MessageReceived?.Invoke(this, rawLine);
            }

            public void Send(string line)
            {
                SentLines.Add(line);
            }

            public void SendError(string message)
            {
                ErrorMessages.Add(message);
            }

            public void Start()
            {
                IsConnected = true;
            }

            public void Stop()
            {
                StopCount++;
                IsConnected = false;
            }
        }

        private sealed class RecordingHost : IProtocolCommandHost
        {
            public int QuitCount { get; private set; }

            public void DispatchProtocolCommand(Action command)
            {
                command();
            }

            public void HandleLossFocus()
            {
            }

            public void HandlePlaceRequest(MoveRequest request)
            {
            }

            public void HandleYikeContext(YikeWindowContext context)
            {
            }

            public void HandleQuitRequest()
            {
                QuitCount++;
            }

            public void HandleStopInBoardRequest()
            {
            }

            public void HandleVersionRequest()
            {
            }
        }

        private sealed class DeferredDispatchHost : IProtocolCommandHost
        {
            public Action PendingCommand { get; private set; }
            public int QuitCount { get; private set; }

            public void RunPendingCommand()
            {
                Action command = PendingCommand;
                PendingCommand = null;
                if (command != null)
                    command();
            }

            public void DispatchProtocolCommand(Action command)
            {
                PendingCommand = command;
            }

            public void HandleLossFocus()
            {
            }

            public void HandlePlaceRequest(MoveRequest request)
            {
            }

            public void HandleYikeContext(YikeWindowContext context)
            {
            }

            public void HandleQuitRequest()
            {
                QuitCount++;
            }

            public void HandleStopInBoardRequest()
            {
            }

            public void HandleVersionRequest()
            {
            }
        }
    }
}
