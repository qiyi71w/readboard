using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class PendingMoveStateTests
    {
        public static IEnumerable<object[]> BoardSizeCases()
        {
            yield return new object[] { 19, 19 };
            yield return new object[] { 13, 13 };
            yield return new object[] { 9, 9 };
            yield return new object[] { 17, 11 };
        }

        [Theory]
        [MemberData(nameof(BoardSizeCases))]
        public void PendingMove_PreservesRectangularCoordinatesAndConfirmsDimensions(
            int boardWidth,
            int boardHeight)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = CreateMove(boardWidth, boardHeight);

            Assert.True(state.TryQueue(move, clock));
            Assert.True(state.TryBeginPlacement(out MoveRequest dispatched));
            Assert.Equal(move.X, dispatched.X);
            Assert.Equal(move.Y, dispatched.Y);
            Assert.True(dispatched.VerifyMove);

            state.CompletePlacement(success: true, keepSync: true);
            state.Observe(CreateSnapshot(boardWidth, boardHeight, move), boardWidth);

            Assert.True(state.HasCompletedResult);
            Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
            Assert.True(success);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void VerifiedPendingMove_WaitsForDelayedSnapshotWithoutAnotherClick(int maxAttempts)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = CreateMove(19, 19);
            move.MoveVerifyMaxAttempts = maxAttempts;

            Assert.True(state.TryQueue(move, clock));
            Assert.True(state.TryBeginPlacement(out _));
            state.CompletePlacement(success: true, keepSync: true);

            state.Observe(CreateEmptySnapshot(19, 19), 19);
            Assert.False(state.IsPlacementAvailable);
            Assert.False(state.TryBeginPlacement(out _));

            clock.AdvanceMilliseconds(250);
            state.Observe(CreateSnapshot(19, 19, move), 19);

            Assert.True(state.HasCompletedResult);
            Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
            Assert.True(success);
            Assert.False(state.IsPlacementAvailable);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        public void VerifiedPendingMove_UsesConfiguredTotalAttemptsBeforeFailing(
            int? configuredMaxAttempts,
            int expectedPlacementAttempts)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = new MoveRequest
            {
                X = 1,
                Y = 1,
                VerifyMove = true,
                MoveVerifyMaxAttempts = configuredMaxAttempts
            };
            Assert.True(state.TryQueue(move, clock));

            for (int attempt = 0; attempt < expectedPlacementAttempts; attempt++)
            {
                Assert.True(state.TryBeginPlacement(out MoveRequest dispatched));
                Assert.Equal(1, dispatched.X);
                Assert.Equal(1, dispatched.Y);
                state.CompletePlacement(success: true, keepSync: true);
                state.Observe(CreateEmptySnapshot(19, 19), 19);
                Assert.False(state.IsPlacementAvailable);

                clock.AdvanceMilliseconds(500);
                state.Observe(CreateEmptySnapshot(19, 19), 19);
            }

            Assert.False(state.IsPlacementAvailable);
            Assert.False(state.TryBeginPlacement(out _));
            Assert.True(state.HasCompletedResult);
            Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
            Assert.False(success);
        }

        [Fact]
        public void VerifiedPendingMove_RetriesOnlyAfterAnUnconfirmedObservationWindow()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = CreateMove(19, 19);
            move.MoveVerifyMaxAttempts = 2;

            Assert.True(state.TryQueue(move, clock));
            Assert.True(state.TryBeginPlacement(out _));
            state.CompletePlacement(success: true, keepSync: true);

            clock.AdvanceMilliseconds(499);
            state.Observe(CreateEmptySnapshot(19, 19), 19);
            Assert.False(state.IsPlacementAvailable);
            Assert.False(state.TryBeginPlacement(out _));

            clock.AdvanceMilliseconds(1);
            Assert.False(state.IsPlacementAvailable);
            state.Observe(CreateEmptySnapshot(19, 19), 19);
            Assert.True(state.IsPlacementAvailable);
            Assert.True(state.TryBeginPlacement(out _));
            state.CompletePlacement(success: true, keepSync: true);

            state.Observe(CreateEmptySnapshot(19, 19), 19);
            clock.AdvanceMilliseconds(500);
            state.Observe(CreateSnapshot(19, 19, move), 19);

            Assert.True(state.HasCompletedResult);
            Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
            Assert.True(success);
            Assert.False(state.IsPlacementAvailable);
        }

        [Fact]
        public void VerifiedPendingMove_WithoutUsableSnapshotsDoesNotRetryAndStillTimesOut()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = CreateMove(19, 19);
            move.MoveVerifyMaxAttempts = 10;

            Assert.True(state.TryQueue(move, clock));
            Assert.True(state.TryBeginPlacement(out _));
            state.CompletePlacement(success: true, keepSync: true);

            clock.AdvanceMilliseconds(500);
            state.Observe(new BoardSnapshot { IsValid = false }, 19);
            Assert.False(state.IsPlacementAvailable);
            Assert.False(state.TryBeginPlacement(out _));

            clock.AdvanceMilliseconds(1500);
            Assert.False(state.IsPlacementAvailable);
            Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
            Assert.False(success);
            Assert.False(state.IsPlacementAvailable);
        }

        [Fact]
        public void VerifiedPendingMove_TotalDeadlineBoundsRepeatedUnconfirmedClicks()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = CreateMove(19, 19);
            move.MoveVerifyMaxAttempts = 10;

            Assert.True(state.TryQueue(move, clock));

            for (int attempt = 0; attempt < 4; attempt++)
            {
                Assert.True(state.TryBeginPlacement(out _));
                state.CompletePlacement(success: true, keepSync: true);
                clock.AdvanceMilliseconds(500);
                state.Observe(CreateEmptySnapshot(19, 19), 19);
            }

            Assert.False(state.IsPlacementAvailable);
            Assert.False(state.TryBeginPlacement(out _));
            Assert.True(state.HasCompletedResult);
            Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
            Assert.False(success);
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
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = CreateMove(19, 19);

            Assert.True(state.TryQueue(move, clock));
            Assert.True(state.TryBeginPlacement(out _));
            if (!placementFinishesAtSnapshot)
                state.CompletePlacement(success: true, keepSync: true);

            clock.AdvanceMilliseconds(elapsedMilliseconds);
            if (placementFinishesAtSnapshot)
                state.CompletePlacement(success: true, keepSync: true);

            state.Observe(CreateSnapshot(19, 19, move), 19);

            Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
            Assert.Equal(expectedSuccess, success);
            Assert.False(state.IsPlacementAvailable);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void VerifiedPendingMove_StopOrCancelRetiresObservationBeforeNextRequest(bool stopSync)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = CreateMove(19, 19);

            Assert.True(state.TryQueue(move, clock));
            Assert.True(state.TryBeginPlacement(out _));
            state.CompletePlacement(success: true, keepSync: true);

            if (stopSync)
            {
                Assert.True(state.TryConsumeResult(keepSync: false, out bool earlySuccess));
                Assert.False(earlySuccess);
            }
            else
            {
                state.Cancel();
                Assert.True(state.TryConsumeResult(keepSync: true, out bool earlySuccess));
                Assert.False(earlySuccess);
            }

            state.Observe(CreateSnapshot(19, 19, move), 19);
            Assert.False(state.HasCompletedResult);

            clock.AdvanceMilliseconds(3000);
            Assert.True(state.TryQueue(move, clock));
            Assert.True(state.TryBeginPlacement(out _));
            state.CompletePlacement(success: true, keepSync: true);
            state.Observe(CreateSnapshot(19, 19, move), 19);
            Assert.True(state.TryConsumeResult(keepSync: true, out bool nextSuccess));
            Assert.True(nextSuccess);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UnverifiedPendingMove_IgnoresConfiguredMaxAttempts(bool physicalPlacementSuccess)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = new MoveRequest
            {
                X = 1,
                Y = 1,
                VerifyMove = false,
                MoveVerifyMaxAttempts = 5
            };

            Assert.True(state.TryQueue(move, clock));
            Assert.True(state.TryBeginPlacement(out MoveRequest attempt));
            Assert.Equal(1, attempt.X);
            Assert.Equal(1, attempt.Y);
            Assert.False(attempt.VerifyMove);

            state.CompletePlacement(physicalPlacementSuccess, keepSync: true);

            Assert.True(state.HasCompletedResult);
            Assert.False(state.IsPlacementAvailable);
            Assert.False(state.TryBeginPlacement(out _));

            Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
            Assert.Equal(physicalPlacementSuccess, success);
            Assert.False(state.IsPlacementAvailable);
        }

        [Fact]
        public void TryQueue_RejectsNewMoveUntilPreviousResultIsConsumed()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest firstMove = new MoveRequest { X = 1, Y = 1, VerifyMove = false };
            MoveRequest secondMove = new MoveRequest { X = 2, Y = 2, VerifyMove = false };

            Assert.False(state.TryQueue(null, clock));
            Assert.True(state.TryQueue(firstMove, clock));
            Assert.True(state.TryBeginPlacement(out _));
            state.CompletePlacement(success: true, keepSync: true);
            Assert.True(state.HasCompletedResult);

            // Cannot queue while result is unconsumed
            Assert.False(state.TryQueue(secondMove, clock));

            // Consume completed result
            Assert.True(state.TryConsumeResult(keepSync: true, out bool firstSuccess));
            Assert.True(firstSuccess);
            Assert.False(state.HasCompletedResult);

            // Now second move can be queued
            Assert.True(state.TryQueue(secondMove, clock));
            Assert.True(state.TryBeginPlacement(out MoveRequest dispatched));
            Assert.Equal(2, dispatched.X);
            Assert.Equal(2, dispatched.Y);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Cancel_WhilePlacementInProgress_DefersOutcomeToPlacementCompletion(bool physicalSuccess)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = CreateMove(19, 19);

            Assert.True(state.TryQueue(move, clock));
            Assert.True(state.TryBeginPlacement(out _));

            // Cancel while physical placement is in progress
            state.Cancel();

            // Cancel does not abort or complete the in-flight placing operation
            Assert.False(state.HasCompletedResult);
            Assert.False(state.IsPlacementAvailable);
            Assert.False(state.TryConsumeResult(keepSync: true, out _));

            // Physical placement finishes
            state.CompletePlacement(physicalSuccess, keepSync: true);

            if (physicalSuccess)
            {
                state.Observe(CreateSnapshot(19, 19, move), 19);
                Assert.True(state.HasCompletedResult);
                Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
                Assert.True(success);
            }
            else
            {
                Assert.True(state.HasCompletedResult);
                Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
                Assert.False(success);
            }
        }

        [Fact]
        public void CompletePlacement_WhenSyncStopped_PreservesPhysicalSuccess()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = CreateMove(19, 19);

            Assert.True(state.TryQueue(move, clock));
            Assert.True(state.TryBeginPlacement(out _));

            // Physical placement succeeds, but sync was stopped
            state.CompletePlacement(success: true, keepSync: false);

            Assert.True(state.HasCompletedResult);
            Assert.True(state.TryConsumeResult(keepSync: false, out bool success));
            Assert.True(success);
        }

        [Fact]
        public void Observe_EarlyLateOrDuplicateSnapshots_CannotOverwriteCompletedOutcome()
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = CreateMove(19, 19);

            Assert.True(state.TryQueue(move, clock));

            // Early observation while Ready: no-op
            state.Observe(CreateSnapshot(19, 19, move), 19);
            Assert.True(state.IsPlacementAvailable);
            Assert.False(state.HasCompletedResult);

            Assert.True(state.TryBeginPlacement(out _));

            // Early observation while Placing: no-op
            state.Observe(CreateSnapshot(19, 19, move), 19);
            Assert.False(state.HasCompletedResult);

            state.CompletePlacement(success: true, keepSync: true);

            // Confirming observation completes move with success
            state.Observe(CreateSnapshot(19, 19, move), 19);
            Assert.True(state.HasCompletedResult);

            // Late / duplicate observations cannot overwrite completed outcome
            state.Observe(CreateEmptySnapshot(19, 19), 19);
            state.Observe(new BoardSnapshot { IsValid = false }, 19);
            clock.AdvanceMilliseconds(5000);
            state.Observe(CreateEmptySnapshot(19, 19), 19);

            Assert.True(state.HasCompletedResult);
            Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
            Assert.True(success);
        }

        [Theory]
        [InlineData((int)BoardCellState.Black, true)]
        [InlineData((int)BoardCellState.White, true)]
        [InlineData((int)BoardCellState.Empty, false)]
        public void Observe_TargetNonempty_TreatsBothBlackAndWhiteAsConfirmed(
            int cellState,
            bool expectedConfirmed)
        {
            ManualTimeProvider clock = new ManualTimeProvider();
            PendingMoveState state = new PendingMoveState();
            MoveRequest move = CreateMove(19, 19);

            Assert.True(state.TryQueue(move, clock));
            Assert.True(state.TryBeginPlacement(out _));
            state.CompletePlacement(success: true, keepSync: true);

            BoardCellState[] boardState = new BoardCellState[19 * 19];
            boardState[(move.Y * 19) + move.X] = (BoardCellState)cellState;
            BoardSnapshot snapshot = new BoardSnapshot
            {
                Width = 19,
                Height = 19,
                IsValid = true,
                BoardState = boardState,
                Payload = "matrix-payload",
                ProtocolLines = new[] { "re=matrix" }
            };

            state.Observe(snapshot, 19);

            if (expectedConfirmed)
            {
                Assert.True(state.HasCompletedResult);
                Assert.True(state.TryConsumeResult(keepSync: true, out bool success));
                Assert.True(success);
            }
            else
            {
                Assert.False(state.HasCompletedResult);
                Assert.False(state.IsPlacementAvailable);
            }
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
    }
}
