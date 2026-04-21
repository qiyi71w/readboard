using System.Drawing;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Display
{
    public sealed class DisplayScalingTests
    {
        [Fact]
        public void ScreenToClientBounds_ScalesDownForDpiUnawareWindows()
        {
            PixelRect result = DisplayScaling.ScreenToClientBounds(
                new PixelRect(320, 240, 50, 50),
                new PixelRect(300, 200, 100, 100),
                isDpiAware: false,
                scale: 1.5d);

            AssertRect(result, 13, 26, 33, 33);
        }

        [Fact]
        public void ClientToScreenBounds_ScalesUpForDpiUnawareWindows()
        {
            PixelRect result = DisplayScaling.ClientToScreenBounds(
                new PixelRect(20, 40, 30, 30),
                new PixelRect(300, 200, 100, 100),
                isDpiAware: false,
                scale: 1.5d);

            AssertRect(result, 330, 260, 45, 45);
        }

        [Fact]
        public void ProtocolBoundsFromScreen_ScalesPhysicalBoundsDownToProtocolSpace()
        {
            PixelRect result = DisplayScaling.ProtocolBoundsFromScreen(
                new PixelRect(320, 240, 50, 50),
                scale: 1.5d);

            AssertRect(result, 213, 160, 33, 33);
        }

        [Fact]
        public void ProtocolBoundsFromWindow_DpiAwareWindowUsesScaledWindowOffset()
        {
            PixelRect result = DisplayScaling.ProtocolBoundsFromWindow(
                new PixelRect(300, 200, 100, 100),
                new PixelRect(20, 40, 50, 50),
                isDpiAware: true,
                scale: 1.5d);

            AssertRect(result, 213, 160, 33, 33);
        }

        [Fact]
        public void ScaleClientCoordinateForPostMessage_ScalesOnlyDpiUnawareWindows()
        {
            Assert.Equal(23, DisplayScaling.ScaleClientCoordinateForPostMessage(23, isDpiAware: true, scale: 1.5d));
            Assert.Equal(34, DisplayScaling.ScaleClientCoordinateForPostMessage(23, isDpiAware: false, scale: 1.5d));
        }

        [Fact]
        public void ResolveWindowScale_UsesMonitorScaleForDpiUnawareWindows()
        {
            Assert.Equal(1.5d, DisplayScaling.ResolveWindowScale(isDpiAware: false, dpiAwareScale: 1d, monitorScale: 1.5d));
            Assert.Equal(1.25d, DisplayScaling.ResolveWindowScale(isDpiAware: true, dpiAwareScale: 1.25d, monitorScale: 1.5d));
        }

        [Fact]
        public void ResolveReferencePoint_PrefersSavedStartupLocationBeforeCurrentBoundsCenter()
        {
            Point point = DisplayScaling.ResolveReferencePoint(
                useCurrentBoundsCenter: true,
                currentBounds: new Rectangle(100, 200, 640, 480),
                currentLocation: new Point(12, 34),
                startupReferencePoint: new Point(1600, 900));

            Assert.Equal(new Point(1600, 900), point);
        }

        [Fact]
        public void ResolveReferencePoint_UsesCurrentBoundsCenterWhenNoSavedStartupLocationExists()
        {
            Point point = DisplayScaling.ResolveReferencePoint(
                useCurrentBoundsCenter: true,
                currentBounds: new Rectangle(100, 200, 640, 480),
                currentLocation: new Point(12, 34),
                startupReferencePoint: null);

            Assert.Equal(new Point(420, 440), point);
        }

        [Fact]
        public void ResolveReferencePoint_FallsBackToCurrentLocationBeforeHandleExists()
        {
            Point point = DisplayScaling.ResolveReferencePoint(
                useCurrentBoundsCenter: false,
                currentBounds: new Rectangle(100, 200, 640, 480),
                currentLocation: new Point(12, 34),
                startupReferencePoint: null);

            Assert.Equal(new Point(12, 34), point);
        }

        private static void AssertRect(PixelRect rect, int x, int y, int width, int height)
        {
            Assert.NotNull(rect);
            Assert.Equal(x, rect.X);
            Assert.Equal(y, rect.Y);
            Assert.Equal(width, rect.Width);
            Assert.Equal(height, rect.Height);
        }
    }
}
