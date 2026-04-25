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
    }
}
