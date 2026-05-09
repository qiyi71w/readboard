using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class YikeProtocolInboundParsingTests
    {
        [Fact]
        public void parses_full_yike_line()
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().ParseInbound("yike room=65191829 move=16");

            Assert.Equal(ProtocolMessageKind.YikeContext, msg.Kind);
            Assert.Equal("65191829", msg.YikeRoomToken);
            Assert.Equal(16, msg.YikeMoveNumber);
        }

        [Fact]
        public void parses_yike_without_room()
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().ParseInbound("yike move=42");

            Assert.Equal(ProtocolMessageKind.YikeContext, msg.Kind);
            Assert.Null(msg.YikeRoomToken);
            Assert.Equal(42, msg.YikeMoveNumber);
        }

        [Fact]
        public void parses_yike_without_move()
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().ParseInbound("yike room=65191829");

            Assert.Equal(ProtocolMessageKind.YikeContext, msg.Kind);
            Assert.Equal("65191829", msg.YikeRoomToken);
            Assert.Null(msg.YikeMoveNumber);
        }

        [Fact]
        public void parses_bare_yike_as_unknown()
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().ParseInbound("yike");

            Assert.Equal(ProtocolMessageKind.YikeContext, msg.Kind);
            Assert.Null(msg.YikeRoomToken);
            Assert.Null(msg.YikeMoveNumber);
        }

        [Fact]
        public void ignores_garbage_in_yike_payload()
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().ParseInbound("yike room=65191829 move=abc");

            Assert.Equal(ProtocolMessageKind.YikeContext, msg.Kind);
            Assert.Equal("65191829", msg.YikeRoomToken);
            Assert.Null(msg.YikeMoveNumber);
        }

        [Theory]
        [InlineData("yike move=0")]
        [InlineData("yike move=-1")]
        public void treats_non_positive_move_as_unknown(string rawLine)
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().ParseInbound(rawLine);

            Assert.Equal(ProtocolMessageKind.YikeContext, msg.Kind);
            Assert.Null(msg.YikeMoveNumber);
        }

        [Fact]
        public void parses_yike_geometry_line()
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().ParseInbound(
                "yikeGeometry left=13 top=60 width=613 height=613 board=19");

            Assert.Equal(ProtocolMessageKind.YikeGeometry, msg.Kind);
            Assert.NotNull(msg.YikeGeometry);
            Assert.Equal(new PixelRect(13, 60, 613, 613).X, msg.YikeGeometry.Bounds.X);
            Assert.Equal(new PixelRect(13, 60, 613, 613).Y, msg.YikeGeometry.Bounds.Y);
            Assert.Equal(new PixelRect(13, 60, 613, 613).Width, msg.YikeGeometry.Bounds.Width);
            Assert.Equal(new PixelRect(13, 60, 613, 613).Height, msg.YikeGeometry.Bounds.Height);
            Assert.Equal(19, msg.YikeGeometry.BoardSize);
            Assert.Null(msg.YikeGeometry.FirstIntersectionX);
            Assert.Null(msg.YikeGeometry.FirstIntersectionY);
        }

        [Fact]
        public void parses_yike_geometry_line_with_explicit_grid()
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().ParseInbound(
                "yikeGeometry left=45 top=60 width=640 height=640 board=19 firstX=81.1075 firstY=97.12 cellX=32.4602777778 cellY=31.4311111111");

            Assert.Equal(ProtocolMessageKind.YikeGeometry, msg.Kind);
            Assert.NotNull(msg.YikeGeometry);
            Assert.Equal(45, msg.YikeGeometry.Bounds.X);
            Assert.Equal(60, msg.YikeGeometry.Bounds.Y);
            Assert.Equal(640, msg.YikeGeometry.Bounds.Width);
            Assert.Equal(640, msg.YikeGeometry.Bounds.Height);
            Assert.Equal(19, msg.YikeGeometry.BoardSize);
            Assert.Equal(81.1075d, msg.YikeGeometry.FirstIntersectionX.Value, 10);
            Assert.Equal(97.12d, msg.YikeGeometry.FirstIntersectionY.Value, 10);
            Assert.Equal(32.4602777778d, msg.YikeGeometry.CellWidth, 10);
            Assert.Equal(31.4311111111d, msg.YikeGeometry.CellHeight, 10);
        }

        [Fact]
        public void parses_bare_yike_geometry_as_clear_signal()
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().ParseInbound("yikeGeometry");

            Assert.Equal(ProtocolMessageKind.YikeGeometry, msg.Kind);
            Assert.Null(msg.YikeGeometry);
        }

        [Fact]
        public void parses_yike_browser_sync_stop_request()
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().ParseInbound("yikeBrowserSyncStop");

            Assert.Equal(ProtocolMessageKind.YikeBrowserSyncStop, msg.Kind);
        }
    }
}
