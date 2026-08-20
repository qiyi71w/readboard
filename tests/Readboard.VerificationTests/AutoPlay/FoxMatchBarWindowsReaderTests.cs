using System.Drawing;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxMatchBarWindowsReaderTests
    {
        [Fact]
        public void TryGetSeatBounds_SplitsInfoPanelAtPrototypePercents()
        {
            Rectangle left;
            Rectangle right;

            Assert.True(FoxMatchBarWindowsReader.TryGetSeatBounds(new Size(800, 200), out left, out right));

            Assert.Equal(new Rectangle(0, 110, 400, 44), left);
            Assert.Equal(new Rectangle(400, 110, 400, 44), right);
        }
    }
}
