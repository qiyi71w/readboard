using System;
using System.Collections.Generic;
using Xunit;
using readboard;

namespace Readboard.VerificationTests
{
    public sealed class SyncSessionCoordinatorTests
    {
        [Fact]
        public void NotifyReady_SendsReadyAndPlayPonderLinesInOrder()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.NotifyReady(true);

            Assert.Equal(new[] { "ready", "playponder on" }, transport.SentLines);
        }

        [Fact]
        public void StartAndStop_ManageDispatchLifecycleForInboundPlaceMessages()
        {
            FakeTransport transport = new FakeTransport();
            CapturingHost host = new CapturingHost();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.AttachHost(host);

            coordinator.Start();
            transport.Emit("place 3 4");
            coordinator.Stop();
            transport.Emit("place 9 9");

            Assert.Equal(1, transport.StartCount);
            Assert.Equal(1, transport.StopCount);
            Assert.Equal(1, host.DispatchCount);
            Assert.NotNull(host.LastMoveRequest);
            Assert.Equal(3, host.LastMoveRequest.X);
            Assert.Equal(4, host.LastMoveRequest.Y);
        }

        [Fact]
        public void SendError_ForwardsToTransport()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SendError("boom");

            Assert.Equal(new[] { "boom" }, transport.ErrorMessages);
        }

        [Fact]
        public void SendBoardSnapshot_SendsProtocolLinesOncePerDistinctPayload()
        {
            FakeTransport transport = new FakeTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            BoardSnapshot snapshot = new BoardSnapshot
            {
                Payload = "payload-1",
                ProtocolLines = new[] { "re=000", "re=111" }
            };

            coordinator.SendBoardSnapshot(snapshot);
            coordinator.SendBoardSnapshot(snapshot);

            Assert.Equal(new[] { "re=000", "re=111", "end" }, transport.SentLines);
        }

        private sealed class FakeTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived;

            public List<string> SentLines { get; } = new List<string>();
            public List<string> ErrorMessages { get; } = new List<string>();
            public int StartCount { get; private set; }
            public int StopCount { get; private set; }

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
                SentLines.Add(line);
            }

            public void SendError(string message)
            {
                ErrorMessages.Add(message);
            }

            public void Start()
            {
                StartCount++;
                IsConnected = true;
            }

            public void Stop()
            {
                StopCount++;
                IsConnected = false;
            }
        }

        private sealed class CapturingHost : IProtocolCommandHost
        {
            public int DispatchCount { get; private set; }
            public MoveRequest LastMoveRequest { get; private set; }

            public void DispatchProtocolCommand(Action command)
            {
                DispatchCount++;
                command();
            }

            public void HandleLossFocus()
            {
            }

            public void HandlePlaceRequest(MoveRequest request)
            {
                LastMoveRequest = request;
            }

            public void HandleQuitRequest()
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
