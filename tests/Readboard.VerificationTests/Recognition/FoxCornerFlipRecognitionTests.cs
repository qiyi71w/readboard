using System.Drawing;
using System.IO;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Recognition
{
    public sealed class FoxCornerFlipRecognitionTests
    {
        [Theory]
        [InlineData("fox-corner-flip-black-last-move.png", (int)BoardCellState.BlackLastMove, (int)LastMoveSource.FoxCornerFlip)]
        [InlineData("fox-corner-flip-white-last-move.png", (int)BoardCellState.WhiteLastMove, (int)LastMoveSource.FoxCornerFlip)]
        [InlineData("fox-numbered-red-marker-black-last-move.png", (int)BoardCellState.BlackLastMove, (int)LastMoveSource.RedBlueMarker)]
        [InlineData("fox-numbered-blue-marker-white-last-move.png", (int)BoardCellState.WhiteLastMove, (int)LastMoveSource.RedBlueMarker)]
        public void Recognize_FoxLastMoveFixture_ReportsExpectedStateAndSource(
            string fixtureName,
            int expectedState,
            int expectedSource)
        {
            using (Bitmap bitmap = new Bitmap(FixturePath(fixtureName)))
            {
                BoardRecognitionResult result = new LegacyBoardRecognitionService().Recognize(
                    new BoardRecognitionRequest
                    {
                        Frame = new BoardFrame
                        {
                            SyncMode = SyncMode.Background,
                            BoardSize = new BoardDimensions(1, 1),
                            Image = bitmap,
                            Viewport = new BoardViewport
                            {
                                SourceBounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height)
                            }
                        },
                        Thresholds = new RecognitionThresholds
                        {
                            BlackPercent = 20,
                            WhitePercent = 15,
                            BlackOffset = RecognitionThresholds.DefaultBlackOffset,
                            WhiteOffset = RecognitionThresholds.DefaultWhiteOffset,
                            GrayOffset = RecognitionThresholds.DefaultGrayOffset,
                            RedBlueMarkerThreshold = RecognitionThresholds.DefaultRedBlueMarkerThreshold
                        },
                        InferLastMove = true
                    });

                Assert.True(result.Success, result.FailureReason);
                Assert.Equal((BoardCellState)expectedState, result.Snapshot.BoardState[0]);
                Assert.Equal((LastMoveSource)expectedSource, result.Snapshot.LastMoveSource);
            }
        }

        private static string FixturePath(string fixtureName)
        {
            return Path.Combine(
                VerificationFixtureLocator.RepositoryRoot(),
                "tests",
                "Readboard.VerificationTests",
                "Recognition",
                "Fixtures",
                "LastMoveSource",
                fixtureName);
        }
    }
}
