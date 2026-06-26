using System.Drawing;
using System.IO;
using System.Reflection;
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
                BoardRecognitionRequest request = CreateDefaultThresholdRequest(bitmap);

                BoardRecognitionResult result = new LegacyBoardRecognitionService().Recognize(request);

                Assert.True(result.Success, result.FailureReason);
                Assert.Equal((BoardCellState)expectedState, result.Snapshot.BoardState[0]);
                Assert.Equal((LastMoveSource)expectedSource, result.Snapshot.LastMoveSource);
            }
        }

        [Fact]
        public void Recognize_FoxLastMoveFixture_UsesDefaultThresholds()
        {
            using (Bitmap bitmap = new Bitmap(FixturePath("fox-corner-flip-black-last-move.png")))
            {
                BoardRecognitionRequest request = CreateDefaultThresholdRequest(bitmap);

                Assert.Equal(RecognitionThresholds.DefaultBlackPercent, request.Thresholds.BlackPercent);
                Assert.Equal(RecognitionThresholds.DefaultWhitePercent, request.Thresholds.WhitePercent);
                Assert.Equal(RecognitionThresholds.DefaultBlackOffset, request.Thresholds.BlackOffset);
                Assert.Equal(RecognitionThresholds.DefaultWhiteOffset, request.Thresholds.WhiteOffset);
                Assert.Equal(RecognitionThresholds.DefaultGrayOffset, request.Thresholds.GrayOffset);
                Assert.Equal(RecognitionThresholds.DefaultRedBlueMarkerThreshold, request.Thresholds.RedBlueMarkerThreshold);
            }
        }

        [Fact]
        public void IsLowerRightCornerSample_RequiresInnerTriangleSector()
        {
            MethodInfo method = typeof(LegacyBoardRecognitionService).GetMethod(
                "IsLowerRightCornerSample",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            Assert.False((bool)method.Invoke(null, new object[] { 25, 25, 48, 48 }));
            Assert.True((bool)method.Invoke(null, new object[] { 34, 34, 48, 48 }));
        }

        private static BoardRecognitionRequest CreateDefaultThresholdRequest(Bitmap bitmap)
        {
            const int fixtureInset = 6;
            return new BoardRecognitionRequest
            {
                Frame = new BoardFrame
                {
                    SyncMode = SyncMode.Background,
                    BoardSize = new BoardDimensions(1, 1),
                    Image = bitmap,
                    Viewport = new BoardViewport
                    {
                        SourceBounds = new PixelRect(
                            fixtureInset,
                            fixtureInset,
                            bitmap.Width - (fixtureInset * 2),
                            bitmap.Height - (fixtureInset * 2))
                    }
                },
                InferLastMove = true
            };
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
