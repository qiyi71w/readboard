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

        private sealed class RecordingTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived;

            public bool IsConnected { get; private set; }

            public void Dispose()
            {
            }

            public void Emit(string rawLine)
            {
                MessageReceived?.Invoke(this, rawLine);
            }

            public void Send(string line)
            {
            }

            public void SendError(string message)
            {
            }

            public void Start()
            {
                IsConnected = true;
            }

            public void Stop()
            {
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
