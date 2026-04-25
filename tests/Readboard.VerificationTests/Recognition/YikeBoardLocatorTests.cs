using System.Drawing;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Recognition
{
    public sealed class YikeBoardLocatorTests
    {
        [Fact]
        public void Yike_uses_auto_detected_bounds_when_seeded_with_full_window()
        {
            using (Bitmap bmp = new Bitmap(VerificationFixtureLocator.FixturePath("yike/sample-19road-room.png")))
            {
                BoardFrame frame = CreateFullWindowSeedFrame(bmp);

                BoardViewport viewport = ResolveViewport(frame);

                Assert.True(viewport.SourceBounds.Width < bmp.Width);
                Assert.True(viewport.SourceBounds.Height < bmp.Height);
            }
        }

        [Fact]
        public void resolves_yike_bounds_from_fixture()
        {
            using (Bitmap bmp = new Bitmap(VerificationFixtureLocator.FixturePath("yike/sample-19road-room.png")))
            {
                BoardFrame frame = new BoardFrame
                {
                    SyncMode = SyncMode.Yike,
                    BoardSize = new BoardDimensions(19, 19),
                    Image = bmp
                };

                BoardViewport viewport = ResolveViewport(frame);

                double ratio = viewport.SourceBounds.Width / (double)viewport.SourceBounds.Height;
                Assert.InRange(ratio, 0.95, 1.05);
                Assert.True(viewport.SourceBounds.Width < bmp.Width);
                Assert.True(viewport.SourceBounds.Height < bmp.Height);
            }
        }

        private static BoardFrame CreateFullWindowSeedFrame(Bitmap bitmap)
        {
            return new BoardFrame
            {
                SyncMode = SyncMode.Yike,
                BoardSize = new BoardDimensions(19, 19),
                Image = bitmap,
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height),
                    ScreenBounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height),
                    CellWidth = bitmap.Width / 19d,
                    CellHeight = bitmap.Height / 19d
                }
            };
        }

        private static BoardViewport ResolveViewport(BoardFrame frame)
        {
            var request = new BoardRecognitionRequest
            {
                Frame = frame,
                InferLastMove = false
            };

            bool created = LegacyPixelMap.TryCreate(
                frame,
                out LegacyPixelMap pixels,
                out BoardRecognitionFailureKind failureKind,
                out string failureReason);
            Assert.True(created, failureReason ?? failureKind.ToString());

            bool resolved = LegacyBoardLocator.TryResolveViewport(
                request,
                pixels,
                out BoardViewport viewport,
                out string locatorFailure);
            Assert.True(resolved, locatorFailure);
            return viewport;
        }
    }
}
