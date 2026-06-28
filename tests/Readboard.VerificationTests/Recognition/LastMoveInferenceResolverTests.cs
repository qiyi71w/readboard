using Xunit;
using readboard;

namespace Readboard.VerificationTests.Recognition
{
    public sealed class LastMoveInferenceResolverTests
    {
        [Fact]
        public void Apply_PromotesRedOnlyMarkerAsRedBlueMarker()
        {
            BoardCellState[] state = { BoardCellState.Black, BoardCellState.White };
            StoneSummary black = new StoneSummary(BoardCellState.Black, BoardCellState.BlackLastMove);
            StoneSummary white = new StoneSummary(BoardCellState.White, BoardCellState.WhiteLastMove);
            black.Observe(80, 0, 0);
            white.Observe(80, 1, 0);
            MarkerSummary marker = new MarkerSummary();
            marker.Observe(redPercent: 5, bluePercent: 0, threshold: 1, x: 0, y: 0);

            LastMoveInference result = LastMoveInferenceResolver.Apply(state, 2, black, white, marker, new FoxCornerFlipSummary());

            Assert.Equal(LastMoveSource.RedBlueMarker, result.Source);
            AssertCoordinate(0, 0, result.Coordinate);
            Assert.Equal(BoardCellState.BlackLastMove, state[0]);
        }

        [Fact]
        public void Apply_PromotesBlackDeviationAsDeviation()
        {
            BoardCellState[] state =
            {
                BoardCellState.Black,
                BoardCellState.White,
                BoardCellState.Black,
                BoardCellState.White
            };
            StoneSummary black = new StoneSummary(BoardCellState.Black, BoardCellState.BlackLastMove);
            StoneSummary white = new StoneSummary(BoardCellState.White, BoardCellState.WhiteLastMove);
            black.Observe(60, 0, 0);
            black.Observe(90, 0, 1);
            white.Observe(80, 1, 0);
            white.Observe(82, 1, 1);
            MarkerSummary marker = new MarkerSummary();

            LastMoveInference result = LastMoveInferenceResolver.Apply(state, 2, black, white, marker, new FoxCornerFlipSummary());

            Assert.Equal(LastMoveSource.Deviation, result.Source);
            AssertCoordinate(0, 0, result.Coordinate);
            Assert.Equal(BoardCellState.BlackLastMove, state[0]);
        }

        [Fact]
        public void Apply_PromotesWhiteDeviationAsDeviation()
        {
            BoardCellState[] state =
            {
                BoardCellState.Black,
                BoardCellState.White,
                BoardCellState.Black,
                BoardCellState.White
            };
            StoneSummary black = new StoneSummary(BoardCellState.Black, BoardCellState.BlackLastMove);
            StoneSummary white = new StoneSummary(BoardCellState.White, BoardCellState.WhiteLastMove);
            black.Observe(80, 0, 0);
            black.Observe(82, 0, 1);
            white.Observe(60, 1, 0);
            white.Observe(90, 1, 1);
            MarkerSummary marker = new MarkerSummary();

            LastMoveInference result = LastMoveInferenceResolver.Apply(state, 2, black, white, marker, new FoxCornerFlipSummary());

            Assert.Equal(LastMoveSource.Deviation, result.Source);
            AssertCoordinate(1, 0, result.Coordinate);
            Assert.Equal(BoardCellState.WhiteLastMove, state[1]);
        }

        [Fact]
        public void Apply_PromotesBlackCountImbalanceAsStoneCount()
        {
            BoardCellState[] state =
            {
                BoardCellState.Black,
                BoardCellState.White,
                BoardCellState.Black
            };
            StoneSummary black = new StoneSummary(BoardCellState.Black, BoardCellState.BlackLastMove);
            StoneSummary white = new StoneSummary(BoardCellState.White, BoardCellState.WhiteLastMove);
            black.Observe(70, 0, 0);
            black.Observe(80, 2, 0);
            white.Observe(90, 1, 0);
            MarkerSummary marker = new MarkerSummary();

            LastMoveInference result = LastMoveInferenceResolver.Apply(state, 3, black, white, marker, new FoxCornerFlipSummary());

            Assert.Equal(LastMoveSource.StoneCount, result.Source);
            AssertCoordinate(0, 0, result.Coordinate);
            Assert.Equal(BoardCellState.BlackLastMove, state[0]);
        }

        [Fact]
        public void Apply_PromotesWhiteCountImbalanceAsStoneCount()
        {
            BoardCellState[] state =
            {
                BoardCellState.Black,
                BoardCellState.White,
                BoardCellState.White
            };
            StoneSummary black = new StoneSummary(BoardCellState.Black, BoardCellState.BlackLastMove);
            StoneSummary white = new StoneSummary(BoardCellState.White, BoardCellState.WhiteLastMove);
            black.Observe(90, 0, 0);
            white.Observe(70, 1, 0);
            white.Observe(80, 2, 0);
            MarkerSummary marker = new MarkerSummary();

            LastMoveInference result = LastMoveInferenceResolver.Apply(state, 3, black, white, marker, new FoxCornerFlipSummary());

            Assert.Equal(LastMoveSource.StoneCount, result.Source);
            AssertCoordinate(1, 0, result.Coordinate);
            Assert.Equal(BoardCellState.WhiteLastMove, state[1]);
        }

        [Fact]
        public void Apply_PromotesMarkerBeforeDeviationAndStoneCount()
        {
            BoardCellState[] state =
            {
                BoardCellState.Black,
                BoardCellState.White,
                BoardCellState.Black,
                BoardCellState.White,
                BoardCellState.Black
            };
            StoneSummary black = new StoneSummary(BoardCellState.Black, BoardCellState.BlackLastMove);
            StoneSummary white = new StoneSummary(BoardCellState.White, BoardCellState.WhiteLastMove);
            black.Observe(60, 0, 0);
            black.Observe(90, 2, 0);
            black.Observe(92, 4, 0);
            white.Observe(80, 1, 0);
            white.Observe(82, 3, 0);
            MarkerSummary marker = new MarkerSummary();
            marker.Observe(redPercent: 5, bluePercent: 0, threshold: 1, x: 1, y: 0);

            LastMoveInference result = LastMoveInferenceResolver.Apply(state, 5, black, white, marker, new FoxCornerFlipSummary());

            Assert.Equal(LastMoveSource.RedBlueMarker, result.Source);
            AssertCoordinate(1, 0, result.Coordinate);
            Assert.Equal(BoardCellState.WhiteLastMove, state[1]);
            Assert.Equal(BoardCellState.Black, state[0]);
        }

        [Fact]
        public void Apply_PromotesDeviationBeforeStoneCount()
        {
            BoardCellState[] state =
            {
                BoardCellState.Black,
                BoardCellState.White,
                BoardCellState.Black,
                BoardCellState.White,
                BoardCellState.Black
            };
            StoneSummary black = new StoneSummary(BoardCellState.Black, BoardCellState.BlackLastMove);
            StoneSummary white = new StoneSummary(BoardCellState.White, BoardCellState.WhiteLastMove);
            black.Observe(70, 0, 0);
            black.Observe(80, 2, 0);
            black.Observe(90, 4, 0);
            white.Observe(60, 1, 0);
            white.Observe(100, 3, 0);
            MarkerSummary marker = new MarkerSummary();

            LastMoveInference result = LastMoveInferenceResolver.Apply(state, 5, black, white, marker, new FoxCornerFlipSummary());

            Assert.Equal(LastMoveSource.Deviation, result.Source);
            AssertCoordinate(1, 0, result.Coordinate);
            Assert.Equal(BoardCellState.WhiteLastMove, state[1]);
            Assert.Equal(BoardCellState.Black, state[0]);
        }

        [Fact]
        public void Apply_ReturnsNoneWhenNoCandidates()
        {
            BoardCellState[] state = { BoardCellState.Black, BoardCellState.White };
            StoneSummary black = new StoneSummary(BoardCellState.Black, BoardCellState.BlackLastMove);
            StoneSummary white = new StoneSummary(BoardCellState.White, BoardCellState.WhiteLastMove);
            black.Observe(80, 0, 0);
            white.Observe(80, 1, 0);
            MarkerSummary marker = new MarkerSummary();

            LastMoveInference result = LastMoveInferenceResolver.Apply(state, 2, black, white, marker, new FoxCornerFlipSummary());

            Assert.Equal(LastMoveSource.None, result.Source);
            Assert.Null(result.Coordinate);
            Assert.Equal(BoardCellState.Black, state[0]);
            Assert.Equal(BoardCellState.White, state[1]);
        }

        private static void AssertCoordinate(int expectedX, int expectedY, BoardCoordinate coordinate)
        {
            Assert.NotNull(coordinate);
            Assert.Equal(expectedX, coordinate.X);
            Assert.Equal(expectedY, coordinate.Y);
        }
    }
}
