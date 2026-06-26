using Xunit;
using readboard;

namespace Readboard.VerificationTests.Recognition
{
    public sealed class FoxCornerFlipSummaryTests
    {
        [Fact]
        public void Observe_AcceptsUniqueBlackCornerFlip()
        {
            FoxCornerFlipSummary summary = new FoxCornerFlipSummary();

            summary.Observe(BoardCellState.Black, innerBlackPercent: 70, innerWhitePercent: 0, blackOppositePercent: 18, whiteOppositePercent: 0, x: 2, y: 3);
            summary.Observe(BoardCellState.Black, innerBlackPercent: 70, innerWhitePercent: 0, blackOppositePercent: 3, whiteOppositePercent: 0, x: 4, y: 5);

            BoardCoordinate candidate;
            Assert.True(summary.TryGetUniqueCandidate(out candidate));
            Assert.Equal(2, candidate.X);
            Assert.Equal(3, candidate.Y);
        }

        [Fact]
        public void Observe_AcceptsUniqueWhiteCornerFlip()
        {
            FoxCornerFlipSummary summary = new FoxCornerFlipSummary();

            summary.Observe(BoardCellState.White, innerBlackPercent: 0, innerWhitePercent: 70, blackOppositePercent: 0, whiteOppositePercent: 17, x: 6, y: 7);
            summary.Observe(BoardCellState.White, innerBlackPercent: 0, innerWhitePercent: 70, blackOppositePercent: 0, whiteOppositePercent: 2, x: 8, y: 9);

            BoardCoordinate candidate;
            Assert.True(summary.TryGetUniqueCandidate(out candidate));
            Assert.Equal(6, candidate.X);
            Assert.Equal(7, candidate.Y);
        }

        [Fact]
        public void Observe_RejectsCloseCandidates()
        {
            FoxCornerFlipSummary summary = new FoxCornerFlipSummary();

            summary.Observe(BoardCellState.Black, innerBlackPercent: 70, innerWhitePercent: 0, blackOppositePercent: 18, whiteOppositePercent: 0, x: 0, y: 0);
            summary.Observe(BoardCellState.Black, innerBlackPercent: 70, innerWhitePercent: 0, blackOppositePercent: 15, whiteOppositePercent: 0, x: 1, y: 0);

            BoardCoordinate candidate;
            Assert.False(summary.TryGetUniqueCandidate(out candidate));
            Assert.Null(candidate);
        }

        [Fact]
        public void Observe_RejectsLowScore()
        {
            FoxCornerFlipSummary summary = new FoxCornerFlipSummary();

            summary.Observe(BoardCellState.Black, innerBlackPercent: 70, innerWhitePercent: 0, blackOppositePercent: 6, whiteOppositePercent: 0, x: 0, y: 0);

            BoardCoordinate candidate;
            Assert.False(summary.TryGetUniqueCandidate(out candidate));
            Assert.Null(candidate);
        }

        [Fact]
        public void Empty_ReturnsIndependentNoCandidateSummaries()
        {
            FoxCornerFlipSummary first = FoxCornerFlipSummary.Empty;
            FoxCornerFlipSummary second = FoxCornerFlipSummary.Empty;

            first.Observe(BoardCellState.Black, innerBlackPercent: 70, innerWhitePercent: 0, blackOppositePercent: 18, whiteOppositePercent: 0, x: 1, y: 2);

            BoardCoordinate candidate;
            Assert.NotSame(first, second);
            Assert.False(second.TryGetUniqueCandidate(out candidate));
            Assert.Null(candidate);
        }
    }
}
