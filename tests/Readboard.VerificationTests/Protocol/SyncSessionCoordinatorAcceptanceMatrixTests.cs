using System;
using System.Collections.Generic;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class SyncSessionCoordinatorAcceptanceMatrixTests
    {
        public static IEnumerable<object[]> BoardSizeCases()
        {
            yield return new object[] { 19, 19 };
            yield return new object[] { 13, 13 };
            yield return new object[] { 9, 9 };
            yield return new object[] { 17, 11 };
        }

        [Fact]
        public void LossFocusMessages_DispatchOnlyWhileCoordinatorIsStarted()
        {
            RecordingTransport transport = new RecordingTransport();
            RecordingHost host = new RecordingHost();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.AttachHost(host);

            coordinator.Start();
            transport.Emit("loss");
            coordinator.Stop();
            transport.Emit("loss");

            Assert.Equal(1, host.LossFocusCount);
        }

        [Theory]
        [MemberData(nameof(BoardSizeCases))]
        public void StartSyncFlow_PreservesBoardDimensions(
            int boardWidth,
            int boardHeight)
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            coordinator.SendStart(boardWidth, boardHeight, new IntPtr(4242), includeWindowHandle: true);
            coordinator.SendSync();
            coordinator.SendBothSync(true);

            Assert.Equal(
                new[]
                {
                    $"start {boardWidth} {boardHeight} 4242",
                    "sync",
                    "bothSync"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendSync_YikePlatformControlsBrowserSync()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("yike");

            coordinator.SendSync();

            Assert.Equal(
                new[]
                {
                    "sync",
                    "yikeSyncStart"
                },
                transport.SentLines);
        }

        [Fact]
        public void SendStopSync_YikePlatformControlsBrowserSyncBeforeLegacyStop()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("yike");

            coordinator.SendStopSync();

            Assert.Equal(
                new[]
                {
                    "yikeSyncStop",
                    "stopsync"
                },
                transport.SentLines);
        }


        private sealed class RecordingTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived;

            public List<string> SentLines { get; } = new List<string>();

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
            public int LossFocusCount { get; private set; }


            public void DispatchProtocolCommand(Action command)
            {
                command();
            }

            public void HandleLossFocus()
            {
                LossFocusCount++;
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
