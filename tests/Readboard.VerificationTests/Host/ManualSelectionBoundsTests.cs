using System.Drawing;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class ManualSelectionBoundsTests
    {
        // Mouse-up adds one pixel: releasing one pixel left/up can collapse an axis.
        [Theory]
        [InlineData(0, 11)]
        [InlineData(11, 0)]
        [InlineData(0, 0)]
        public void DegenerateSelectionIsRejected(int width, int height)
        {
            Rectangle expanded;
            bool accepted = MainForm.TryExpandManualSelectionBounds(
                new Rectangle(100, 100, width, height), new BoardDimensions(19, 19), out expanded);

            Assert.False(accepted);
            Assert.Equal(Rectangle.Empty, expanded);
        }

        [Theory]
        [InlineData(19, 19, 360, 360, 90, 90, 380, 380)]
        [InlineData(9, 13, 160, 240, 90, 90, 180, 260)]
        [InlineData(19, 19, 1, 1, 100, 100, 1, 1)]
        public void PositiveSelectionExpandsByHalfGridSpacing(
            int columns, int rows, int width, int height,
            int expectedX, int expectedY, int expectedWidth, int expectedHeight)
        {
            Rectangle expanded;
            bool accepted = MainForm.TryExpandManualSelectionBounds(
                new Rectangle(100, 100, width, height), new BoardDimensions(columns, rows), out expanded);

            Assert.True(accepted);
            Assert.Equal(new Rectangle(expectedX, expectedY, expectedWidth, expectedHeight), expanded);
        }
    }
}
