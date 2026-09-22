using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using readboard;
using Readboard.VerificationTests.Support;

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

            Task<bool> waitTask = StartPendingMoveWait(coordinator);
            coordinator.HandlePendingMovePlacementResult(true);
            coordinator.ResolvePendingMove(CreateSnapshot(boardWidth, boardHeight, move), boardWidth);

            bool result = await VerificationCompletion.WaitAsync(
                waitTask,
                "Pending move result did not complete.");

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

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void VerifiedPendingMove_WaitsForDelayedSnapshotWithoutAnotherClick(int maxAttempts)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            using SyncSessionCoordinator coordinator = CreateActiveBidirectionalCoordinator(clock);
            MoveRequest move = CreateMove(19, 19);
            move.MoveVerifyMaxAttempts = maxAttempts;
            Assert.True(coordinator.TryQueuePendingMove(move, 19, 19));
            Assert.True(coordinator.TryTakePendingMove(out _));
            coordinator.HandlePendingMovePlacementResult(true);

            coordinator.ResolvePendingMove(CreateEmptySnapshot(19, 19), 19);
            Assert.False(coordinator.TryTakePendingMove(out _));
            clock.AdvanceMilliseconds(250);
            coordinator.ResolvePendingMove(CreateSnapshot(19, 19, move), 19);

            Assert.True(coordinator.WaitForPendingMoveResult());
            Assert.False(coordinator.TryTakePendingMove(out _));
        }

        [Fact]
        public async Task VerifiedPendingMove_WithOneConfiguredMaxAttemptFailsAfterConfirmationWindow()
        {
            await AssertVerifiedPendingMoveFailsAfterTotalPlacementAttempts(1, 1);
        }

        [Fact]
        public async Task VerifiedPendingMove_UsesConfiguredTotalAttemptsBeforeFailing()
        {
            await AssertVerifiedPendingMoveFailsAfterTotalPlacementAttempts(2, 2);
        }

        [Fact]
        public void VerifiedPendingMove_RetriesOnlyAfterAnUnconfirmedObservationWindow()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            using SyncSessionCoordinator coordinator = CreateActiveBidirectionalCoordinator(clock);
            MoveRequest move = CreateMove(19, 19);
            move.MoveVerifyMaxAttempts = 2;
            Assert.True(coordinator.TryQueuePendingMove(move, 19, 19));
            Assert.True(coordinator.TryTakePendingMove(out _));
            coordinator.HandlePendingMovePlacementResult(true);

            clock.AdvanceMilliseconds(499);
            coordinator.ResolvePendingMove(CreateEmptySnapshot(19, 19), 19);
            Assert.False(coordinator.TryTakePendingMove(out _));
            clock.AdvanceMilliseconds(1);
            Assert.False(coordinator.TryTakePendingMove(out _));
            coordinator.ResolvePendingMove(CreateEmptySnapshot(19, 19), 19);
            Assert.True(coordinator.TryTakePendingMove(out _));
            coordinator.HandlePendingMovePlacementResult(true);

            coordinator.ResolvePendingMove(CreateEmptySnapshot(19, 19), 19);
            clock.AdvanceMilliseconds(500);
            coordinator.ResolvePendingMove(CreateSnapshot(19, 19, move), 19);
            Assert.True(coordinator.WaitForPendingMoveResult());
            Assert.False(coordinator.TryTakePendingMove(out _));
        }

        [Fact]
        public void VerifiedPendingMove_WithoutUsableSnapshotsDoesNotRetryAndStillTimesOut()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            using SyncSessionCoordinator coordinator = CreateActiveBidirectionalCoordinator(clock);
            MoveRequest move = CreateMove(19, 19);
            move.MoveVerifyMaxAttempts = 10;
            Assert.True(coordinator.TryQueuePendingMove(move, 19, 19));
            Assert.True(coordinator.TryTakePendingMove(out _));
            coordinator.HandlePendingMovePlacementResult(true);

            clock.AdvanceMilliseconds(500);
            coordinator.ResolvePendingMove(new BoardSnapshot { IsValid = false }, 19);
            Assert.False(coordinator.TryTakePendingMove(out _));
            clock.AdvanceMilliseconds(1500);
            Assert.False(coordinator.WaitForPendingMoveResult());
            Assert.False(coordinator.TryTakePendingMove(out _));
        }

        [Fact]
        public void VerifiedPendingMove_TotalDeadlineBoundsRepeatedUnconfirmedClicks()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            using SyncSessionCoordinator coordinator = CreateActiveBidirectionalCoordinator(clock);
            MoveRequest move = CreateMove(19, 19);
            move.MoveVerifyMaxAttempts = 10;
            Assert.True(coordinator.TryQueuePendingMove(move, 19, 19));

            for (int attempt = 0; attempt < 4; attempt++)
            {
                Assert.True(coordinator.TryTakePendingMove(out _));
                coordinator.HandlePendingMovePlacementResult(true);
                clock.AdvanceMilliseconds(500);
                coordinator.ResolvePendingMove(CreateEmptySnapshot(19, 19), 19);
            }

            Assert.False(coordinator.TryTakePendingMove(out _));
            Assert.False(coordinator.WaitForPendingMoveResult());
        }

        [Theory]
        [InlineData(1999, false, true)]
        [InlineData(2000, false, false)]
        [InlineData(2000, true, false)]
        public void VerifiedPendingMove_OverallDeadlinePrecedesVisibleSnapshot(
            int elapsedMilliseconds,
            bool placementFinishesAtSnapshot,
            bool expectedSuccess)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            using SyncSessionCoordinator coordinator = CreateActiveBidirectionalCoordinator(clock);
            MoveRequest move = CreateMove(19, 19);
            Assert.True(coordinator.TryQueuePendingMove(move, 19, 19));
            Assert.True(coordinator.TryTakePendingMove(out _));
            if (!placementFinishesAtSnapshot)
                coordinator.HandlePendingMovePlacementResult(true);

            clock.AdvanceMilliseconds(elapsedMilliseconds);
            if (placementFinishesAtSnapshot)
                coordinator.HandlePendingMovePlacementResult(true);
            coordinator.ResolvePendingMove(CreateSnapshot(19, 19, move), 19);

            Assert.Equal(expectedSuccess, coordinator.WaitForPendingMoveResult());
            Assert.False(coordinator.TryTakePendingMove(out _));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void VerifiedPendingMove_StopOrCancelRetiresObservationBeforeNextRequest(bool stopSync)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            using SyncSessionCoordinator coordinator = CreateActiveBidirectionalCoordinator(clock);
            MoveRequest move = CreateMove(19, 19);
            Assert.True(coordinator.TryQueuePendingMove(move, 19, 19));
            Assert.True(coordinator.TryTakePendingMove(out _));
            coordinator.HandlePendingMovePlacementResult(true);
            if (stopSync)
                coordinator.EndKeepSync();
            else
                coordinator.CancelPendingMove();
            coordinator.ResolvePendingMove(CreateSnapshot(19, 19, move), 19);
            Assert.False(coordinator.WaitForPendingMoveResult());

            clock.AdvanceMilliseconds(3000);
            coordinator.BeginKeepSync();
            Assert.True(coordinator.TryQueuePendingMove(move, 19, 19));
            Assert.True(coordinator.TryTakePendingMove(out _));
            coordinator.HandlePendingMovePlacementResult(true);
            coordinator.ResolvePendingMove(CreateSnapshot(19, 19, move), 19);
            Assert.True(coordinator.WaitForPendingMoveResult());
        }

        [Fact]
        public async Task UnverifiedPendingMove_IgnoresConfiguredMaxAttempts()
        {
            SyncSessionCoordinator coordinator = CreateActiveBidirectionalCoordinator();
            Assert.True(coordinator.TryQueuePendingMove(
                new MoveRequest
                {
                    X = 1,
                    Y = 1,
                    VerifyMove = false,
                    MoveVerifyMaxAttempts = 5
                },
                19,
                19));
            Task<bool> waitTask = StartPendingMoveWait(coordinator);

            Assert.True(coordinator.TryTakePendingMove(out MoveRequest attempt));
            Assert.Equal(1, attempt.X);
            Assert.Equal(1, attempt.Y);
            coordinator.HandlePendingMovePlacementResult(false);

            bool result = await VerificationCompletion.WaitAsync(
                waitTask,
                "Unverified pending move result did not complete.");

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

        private static async Task AssertVerifiedPendingMoveFailsAfterTotalPlacementAttempts(
            int? configuredMaxAttempts,
            int expectedPlacementAttempts)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            using SyncSessionCoordinator coordinator = CreateActiveBidirectionalCoordinator(clock);

            Assert.True(coordinator.TryQueuePendingMove(
                new MoveRequest
                {
                    X = 1,
                    Y = 1,
                    VerifyMove = true,
                    MoveVerifyMaxAttempts = configuredMaxAttempts
                },
                19,
                19));
            Task<bool> waitTask = StartPendingMoveWait(coordinator);

            for (int attemptNumber = 0; attemptNumber < expectedPlacementAttempts; attemptNumber++)
            {
                Assert.True(coordinator.TryTakePendingMove(out MoveRequest attempt));
                Assert.Equal(1, attempt.X);
                Assert.Equal(1, attempt.Y);
                coordinator.HandlePendingMovePlacementResult(true);
                coordinator.ResolvePendingMove(CreateEmptySnapshot(19, 19), 19);
                Assert.False(coordinator.TryTakePendingMove(out _));
                clock.AdvanceMilliseconds(500);
                coordinator.ResolvePendingMove(CreateEmptySnapshot(19, 19), 19);
            }

            bool result = await VerificationCompletion.WaitAsync(
                waitTask,
                "Verified pending move result did not complete.");

            Assert.False(result);
            Assert.False(coordinator.TryTakePendingMove(out _));
        }

        private static SyncSessionCoordinator CreateActiveBidirectionalCoordinator(TimeProvider clock = null)
        {
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(
                new RecordingTransport(), new LegacyProtocolAdapter(), clock ?? TimeProvider.System);
            coordinator.BeginKeepSync();
            coordinator.SetSyncBoth(true);
            return coordinator;
        }

        private static Task<bool> StartPendingMoveWait(SyncSessionCoordinator coordinator)
        {
            return Task.Factory.StartNew(
                () => coordinator.WaitForPendingMoveResult(),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
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

        private sealed class ManualTimeProvider : TimeProvider
        {
            private long timestamp;

            public override long TimestampFrequency => 1000;

            public override long GetTimestamp() => Interlocked.Read(ref timestamp);

            public void AdvanceMilliseconds(long milliseconds)
            {
                Interlocked.Add(ref timestamp, milliseconds);
            }
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
