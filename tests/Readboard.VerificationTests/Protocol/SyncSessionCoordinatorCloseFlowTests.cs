using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using readboard;
using Readboard.VerificationTests.Support;

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
        public void TransportDisconnect_ClosesSessionBeforeQueuedShutdownAndRejectsNewWrites()
        {
            var transport = new RecordingTransport();
            var host = new DeferredDispatchHost();
            using var coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.AttachHost(host);
            coordinator.Start();
            coordinator.BeginKeepSync();
            coordinator.SendLine("before-disconnect");

            transport.EmitDisconnected();

            Assert.False(coordinator.IsProtocolSessionActive);
            Assert.False(coordinator.StartedSync);
            Assert.False(coordinator.KeepSync);
            Assert.False(transport.IsConnected);
            coordinator.SendLine("after-disconnect");
            coordinator.SendError("after-disconnect");
            Assert.Equal(new[] { "before-disconnect" }, transport.SentLines);
            Assert.Empty(transport.ErrorMessages);
            Assert.NotNull(host.PendingCommand);
            Assert.Equal(0, host.QuitCount);
            host.RunPendingCommand();
            transport.EmitDisconnected();
            Assert.Equal(1, host.QuitCount);
        }

        [Fact]
        public void Restart_DropsShutdownQueuedByPreviousDisconnect()
        {
            var transport = new RecordingTransport();
            var host = new DeferredDispatchHost();
            using var coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.AttachHost(host);
            coordinator.Start();
            transport.EmitDisconnected();
            Assert.NotNull(host.PendingCommand);

            coordinator.Start();
            host.RunPendingCommand();

            Assert.True(coordinator.IsProtocolSessionActive);
            Assert.Equal(0, host.QuitCount);
        }

        [Fact]
        public async Task Restart_DropsPreviousTcpDisconnectAlreadyBeingDelivered()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var transport = new TcpTransport(((IPEndPoint)listener.LocalEndpoint).Port);
            using var releaseNotification = new ManualResetEventSlim(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int notificationCount = 0;
            transport.Disconnected += (_, _) =>
            {
                if (Interlocked.Increment(ref notificationCount) != 1)
                    return;
                entered.TrySetResult(true);
                if (!releaseNotification.Wait(VerificationCompletion.WatchdogTimeout))
                    delivered.TrySetException(new TimeoutException("Old TCP disconnect notification was not released."));
            };
            var host = new DeferredDispatchHost();
            using var coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.AttachHost(host);
            coordinator.Start();
            transport.Disconnected += (_, _) => delivered.TrySetResult(true);
            using var oldPeer = await VerificationCompletion.WaitAsync(
                listener.AcceptTcpClientAsync(), "Old TCP session did not connect.");

            try
            {
                oldPeer.Client.Shutdown(SocketShutdown.Send);
                await VerificationCompletion.WaitAsync(entered.Task, "Old TCP disconnect did not reach the delivery barrier.");
                Assert.False(transport.IsConnected);
                coordinator.Stop();
                coordinator.Start();
                using var currentPeer = await VerificationCompletion.WaitAsync(
                    listener.AcceptTcpClientAsync(), "New TCP session did not connect.");
                Assert.True(coordinator.IsProtocolSessionActive);
                Assert.True(transport.IsConnected);

                releaseNotification.Set();
                await VerificationCompletion.WaitAsync(delivered.Task, "Old TCP disconnect delivery did not finish.");

                Assert.True(coordinator.IsProtocolSessionActive);
                Assert.True(transport.IsConnected);
                Assert.Null(host.PendingCommand);
                Assert.Equal(0, host.QuitCount);
                coordinator.SendLine("current-session-survives");
                using var reader = new StreamReader(currentPeer.GetStream(), Encoding.UTF8, false, 1024, leaveOpen: true);
                Assert.Equal("current-session-survives", await VerificationCompletion.WaitAsync(
                    reader.ReadLineAsync(), "New TCP session could not exchange a real protocol line."));
                coordinator.Stop();
            }
            finally
            {
                releaseNotification.Set();
                await VerificationCompletion.WaitAsync(delivered.Task, "Old TCP disconnect did not retire during cleanup.");
                coordinator.Stop();
            }
        }

        [Fact]
        public async Task Stop_CancelsPendingMoveWaiters()
        {
            using PendingMoveRequestHarness harness = new PendingMoveRequestHarness();
            harness.Start();
            Task<PlaceRequestExecutionResult> request = harness.Request();
            harness.WaitForObservation();
            harness.Coordinator.Stop();

            PlaceRequestExecutionResult result = await VerificationCompletion.WaitAsync(
                request, "Stop did not resolve the pending move waiter.");
            Assert.False(result.ShouldSendResponse);
            Assert.Equal(1, harness.PlacementCount);
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
        public void Restart_DropsQueuedInboundProtocolCommandsFromPreviousStart()
        {
            RecordingTransport transport = new RecordingTransport();
            DeferredDispatchHost host = new DeferredDispatchHost();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.AttachHost(host);

            try
            {
                coordinator.Start();
                transport.Emit("quit");

                Assert.NotNull(host.PendingCommand);

                coordinator.Stop();
                coordinator.Start();
                host.RunPendingCommand();
            }
            finally
            {
                coordinator.Stop();
            }

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
            public event EventHandler Disconnected;

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

            public void EmitDisconnected()
            {
                IsConnected = false;
                Disconnected?.Invoke(this, EventArgs.Empty);
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

            public void HandleYikeGeometry(YikeBoardGeometry geometry)
            {
            }

            public void HandleQuitRequest()
            {
                QuitCount++;
            }

            public void HandleReadboardUpdateSupported()
            {
            }

            public void HandleReadboardUpdatePackageV2Supported()
            {
            }

            public void HandleReadboardUpdateInstalling()
            {
            }

            public void HandleReadboardUpdateCancelled()
            {
            }

            public void HandleReadboardUpdateFailed(string message)
            {
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

            public void HandleYikeGeometry(YikeBoardGeometry geometry)
            {
            }

            public void HandleQuitRequest()
            {
                QuitCount++;
            }

            public void HandleReadboardUpdateSupported()
            {
            }

            public void HandleReadboardUpdatePackageV2Supported()
            {
            }

            public void HandleReadboardUpdateInstalling()
            {
            }

            public void HandleReadboardUpdateCancelled()
            {
            }

            public void HandleReadboardUpdateFailed(string message)
            {
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
