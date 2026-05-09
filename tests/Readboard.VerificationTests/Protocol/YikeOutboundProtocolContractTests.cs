using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class YikeOutboundProtocolContractTests
    {
        [Fact]
        public void room_token_message_format()
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().CreateYikeRoomTokenMessage("65191829");

            Assert.Equal("yikeRoomToken 65191829", msg.RawText);
        }

        [Fact]
        public void move_number_message_format()
        {
            ProtocolMessage msg = new LegacyProtocolAdapter().CreateYikeMoveNumberMessage(16);

            Assert.Equal("yikeMoveNumber 16", msg.RawText);
        }
    }
}
