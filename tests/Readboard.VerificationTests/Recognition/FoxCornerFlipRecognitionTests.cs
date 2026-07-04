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
                Assert.Equal(0, request.Frame.Viewport.SourceBounds.X);
                Assert.Equal(0, request.Frame.Viewport.SourceBounds.Y);
                Assert.Equal(bitmap.Width, request.Frame.Viewport.SourceBounds.Width);
                Assert.Equal(bitmap.Height, request.Frame.Viewport.SourceBounds.Height);
            }
        }

        [Fact]
        public void Recognize_StoneClassificationUsesWholeRegionPercent()
        {
            using (Bitmap bitmap = new Bitmap(10, 10))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    graphics.Clear(Color.Lime);
                for (int y = 1; y < 9; y++)
                    for (int x = 1; x < 9; x++)
                        bitmap.SetPixel(x, y, Color.Black);

                BoardRecognitionRequest request = CreateDefaultThresholdRequest(bitmap);
                request.InferLastMove = false;
                request.Thresholds.BlackPercent = 80;

                BoardRecognitionResult result = new LegacyBoardRecognitionService().Recognize(request);

                Assert.True(result.Success, result.FailureReason);
                Assert.Equal(BoardCellState.Empty, result.Snapshot.BoardState[0]);
            }
        }

        [Theory]
        [InlineData((int)BoardCellState.Black, 40, 40, 40, 240, 240, 240)]
        [InlineData((int)BoardCellState.White, 240, 240, 240, 40, 40, 40)]
        public void Recognize_OrdinaryUnmarkedStoneWithLowerRightShading_DoesNotReportFoxCornerFlip(
            int expectedState,
            int red,
            int green,
            int blue,
            int oppositeRed,
            int oppositeGreen,
            int oppositeBlue)
        {
            using (Bitmap bitmap = new Bitmap(20, 20))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Lime);
                }
                Color stone = Color.FromArgb(red, green, blue);
                Color opposite = Color.FromArgb(oppositeRed, oppositeGreen, oppositeBlue);
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                        if (x < 3 || y < 3 || x >= bitmap.Width - 3 || y >= bitmap.Height - 3)
                            bitmap.SetPixel(x, y, stone);
                bitmap.SetPixel(13, 13, opposite);
                bitmap.SetPixel(14, 13, opposite);
                bitmap.SetPixel(13, 14, opposite);
                bitmap.SetPixel(14, 14, opposite);

                BoardRecognitionRequest request = CreateDefaultThresholdRequest(bitmap);

                BoardRecognitionResult result = new LegacyBoardRecognitionService().Recognize(request);

                Assert.True(result.Success, result.FailureReason);
                Assert.Equal((BoardCellState)expectedState, result.Snapshot.BoardState[0]);
                Assert.NotEqual(LastMoveSource.FoxCornerFlip, result.Snapshot.LastMoveSource);
                Assert.Equal(LastMoveSource.None, result.Snapshot.LastMoveSource);
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
            return new BoardRecognitionRequest
            {
                Frame = new BoardFrame
                {
                    SyncMode = SyncMode.Background,
                    BoardSize = new BoardDimensions(1, 1),
                    Image = bitmap,
                    Viewport = new BoardViewport
                    {
                        SourceBounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height),
                        ScreenBounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height),
                        CellWidth = bitmap.Width,
                        CellHeight = bitmap.Height
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
