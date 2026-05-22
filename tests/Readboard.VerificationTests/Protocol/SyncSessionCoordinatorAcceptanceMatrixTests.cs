using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        public async Task StartSyncFlow_PreservesBoardDimensionsAcrossPendingMoveVerification(
            int boardWidth,
            int boardHeight)
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            MoveRequest move = CreateMove(boardWidth, boardHeight);

            coordinator.SendStart(boardWidth, boardHeight, new IntPtr(4242), includeWindowHandle: true);
            coordinator.SendSync();
            coordinator.SendBothSync(true);
            coordinator.BeginKeepSync();
            coordinator.SetSyncBoth(true);

            Assert.True(coordinator.TryQueuePendingMove(move, boardWidth, boardWidth));
            Assert.True(coordinator.TryTakePendingMove(out MoveRequest dispatched));
            Assert.Equal(move.X, dispatched.X);
            Assert.Equal(move.Y, dispatched.Y);

            Task<bool> waitTask = Task.Run(() => coordinator.WaitForPendingMoveResult());
            coordinator.HandlePendingMovePlacementResult(true);
            coordinator.ResolvePendingMove(CreateSnapshot(boardWidth, boardHeight, move), boardWidth);

            bool result = await waitTask.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.True(result);
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
        public async Task VerifiedPendingMove_WithZeroConfiguredRetriesFailsAfterFirstUnconfirmedSnapshot()
        {
            SyncSessionCoordinator coordinator = CreateActiveBidirectionalCoordinator();

            Assert.True(coordinator.TryQueuePendingMove(
                new MoveRequest
                {
                    X = 1,
                    Y = 1,
                    VerifyMove = true,
                    MoveVerifyMaxAttempts = 0
                },
                19,
                19));
            Assert.True(coordinator.TryTakePendingMove(out MoveRequest dispatched));
            Assert.Equal(1, dispatched.X);
            Assert.Equal(1, dispatched.Y);

            Task<bool> waitTask = Task.Run(() => coordinator.WaitForPendingMoveResult());
            coordinator.HandlePendingMovePlacementResult(true);
            coordinator.ResolvePendingMove(CreateEmptySnapshot(19, 19), 19);

            bool result = await waitTask.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.False(result);
            Assert.False(coordinator.TryTakePendingMove(out _));
        }

        [Fact]
        public async Task VerifiedPendingMove_UsesConfiguredAttemptsBeforeFailing()
        {
            SyncSessionCoordinator coordinator = CreateActiveBidirectionalCoordinator();

            Assert.True(coordinator.TryQueuePendingMove(
                new MoveRequest
                {
                    X = 1,
                    Y = 1,
                    VerifyMove = true,
                    MoveVerifyMaxAttempts = 2
                },
                19,
                19));
            Assert.True(coordinator.TryTakePendingMove(out MoveRequest firstAttempt));

            Task<bool> waitTask = Task.Run(() => coordinator.WaitForPendingMoveResult());
            coordinator.HandlePendingMovePlacementResult(true);
            coordinator.ResolvePendingMove(CreateEmptySnapshot(19, 19), 19);

            Assert.True(coordinator.TryTakePendingMove(out MoveRequest secondAttempt));
            Assert.Equal(firstAttempt.X, secondAttempt.X);
            Assert.Equal(firstAttempt.Y, secondAttempt.Y);

            coordinator.HandlePendingMovePlacementResult(true);
            coordinator.ResolvePendingMove(CreateEmptySnapshot(19, 19), 19);

            bool result = await waitTask.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.False(result);
            Assert.False(coordinator.TryTakePendingMove(out _));
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

        private static MoveRequest CreateMove(int boardWidth, int boardHeight)
        {
            return new MoveRequest
            {
                X = Math.Min(2, boardWidth - 1),
                Y = Math.Min(3, boardHeight - 1),
                VerifyMove = true
            };
        }

        private static SyncSessionCoordinator CreateActiveBidirectionalCoordinator()
        {
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(new RecordingTransport(), new LegacyProtocolAdapter());
            coordinator.BeginKeepSync();
            coordinator.SetSyncBoth(true);
            return coordinator;
        }

        private static BoardSnapshot CreateSnapshot(int boardWidth, int boardHeight, MoveRequest move)
        {
            BoardCellState[] boardState = new BoardCellState[boardWidth * boardHeight];
            boardState[(move.Y * boardWidth) + move.X] = BoardCellState.Black;
            return new BoardSnapshot
            {
                Width = boardWidth,
                Height = boardHeight,
                IsValid = true,
                BoardState = boardState,
                Payload = "matrix-payload",
                ProtocolLines = new[] { "re=matrix" }
            };
        }

        private static BoardSnapshot CreateEmptySnapshot(int boardWidth, int boardHeight)
        {
            return new BoardSnapshot
            {
                Width = boardWidth,
                Height = boardHeight,
                IsValid = true,
                BoardState = new BoardCellState[boardWidth * boardHeight],
                Payload = "empty-matrix-payload",
                ProtocolLines = new[] { "re=empty" }
            };
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
