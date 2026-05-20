using System.Drawing;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxPlayerStoneIconDetectorTests
    {
        [Fact]
        public void Detect_ReturnsBlackForDarkStoneOnLightBackground()
        {
            using (Bitmap bitmap = CreateStoneIcon(Color.FromArgb(235, 235, 225), Color.FromArgb(20, 20, 20)))
            {
                AutoPlayColorResolution resolution = FoxPlayerStoneIconDetector.Detect(bitmap);

                Assert.True(resolution.IsKnown);
                Assert.Equal("black", resolution.PlayColor);
                Assert.Equal(AutoPlayColorStatus.RecognizedBlack, resolution.Status);
            }
        }

        [Fact]
        public void Detect_ReturnsWhiteForLightStoneOnColoredBackground()
        {
            using (Bitmap bitmap = CreateStoneIcon(Color.FromArgb(64, 104, 130), Color.FromArgb(245, 245, 238)))
            {
                AutoPlayColorResolution resolution = FoxPlayerStoneIconDetector.Detect(bitmap);

                Assert.True(resolution.IsKnown);
                Assert.Equal("white", resolution.PlayColor);
                Assert.Equal(AutoPlayColorStatus.RecognizedWhite, resolution.Status);
            }
        }

        [Fact]
        public void Detect_ReturnsUnknownForFlatBackground()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(235, 235, 225));

                AutoPlayColorResolution resolution = FoxPlayerStoneIconDetector.Detect(bitmap);

                Assert.False(resolution.IsKnown);
                Assert.Null(resolution.PlayColor);
                Assert.Equal(AutoPlayColorStatus.ColorUnknown, resolution.Status);
            }
        }

        [Fact]
        public void Detect_ReturnsUnknownForAmbiguousMixedSample()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(235, 235, 225));
                using (Brush darkBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
                using (Brush lightBrush = new SolidBrush(Color.FromArgb(245, 245, 238)))
                {
                    graphics.FillEllipse(darkBrush, 4, 4, 14, 20);
                    graphics.FillEllipse(lightBrush, 14, 4, 14, 20);
                }

                AutoPlayColorResolution resolution = FoxPlayerStoneIconDetector.Detect(bitmap);

                Assert.False(resolution.IsKnown);
                Assert.Null(resolution.PlayColor);
                Assert.Equal(AutoPlayColorStatus.ColorUnknown, resolution.Status);
            }
        }

        private static Bitmap CreateStoneIcon(Color background, Color stone)
        {
            Bitmap bitmap = new Bitmap(32, 32);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Brush brush = new SolidBrush(stone))
            {
                graphics.Clear(background);
                graphics.FillEllipse(brush, 6, 5, 20, 20);
            }
            return bitmap;
        }
    }
}
