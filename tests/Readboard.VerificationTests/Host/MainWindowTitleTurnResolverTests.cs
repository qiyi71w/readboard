using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class MainWindowTitleTurnResolverTests
    {
        [Fact]
        public void Resolve_ReturnsWhiteAfterBlackLastMove()
        {
            BoardSnapshot snapshot = CreateSnapshot(BoardCellState.BlackLastMove);

            Assert.Equal(MainWindowTitleTurn.White, MainWindowTitleTurnResolver.Resolve(snapshot));
        }

        [Fact]
        public void Resolve_ReturnsBlackAfterWhiteLastMove()
        {
            BoardSnapshot snapshot = CreateSnapshot(BoardCellState.WhiteLastMove);

            Assert.Equal(MainWindowTitleTurn.Black, MainWindowTitleTurnResolver.Resolve(snapshot));
        }

        [Fact]
        public void Resolve_ReturnsUnknownWhenNoLastMoveMarkerExists()
        {
            BoardSnapshot snapshot = CreateSnapshot(BoardCellState.Black);

            Assert.Equal(MainWindowTitleTurn.Unknown, MainWindowTitleTurnResolver.Resolve(snapshot));
        }

        [Fact]
        public void Resolve_ReturnsUnknownWhenMultipleLastMoveMarkersExist()
        {
            BoardSnapshot snapshot = CreateSnapshot(
                BoardCellState.BlackLastMove,
                BoardCellState.WhiteLastMove);

            Assert.Equal(MainWindowTitleTurn.Unknown, MainWindowTitleTurnResolver.Resolve(snapshot));
        }

        private static BoardSnapshot CreateSnapshot(params BoardCellState[] states)
        {
            return new BoardSnapshot
            {
                Width = states.Length,
                Height = 1,
                IsValid = true,
                BoardState = states
            };
        }
    }
}
