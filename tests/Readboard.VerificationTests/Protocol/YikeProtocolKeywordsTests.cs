using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class YikeProtocolKeywordsTests
    {
        [Fact]
        public void exposes_yike_inbound_prefix()
        {
            Assert.Equal("yike", ProtocolKeywords.Yike);
        }

        [Fact]
        public void exposes_outbound_prefixes_parallel_to_fox()
        {
            Assert.Equal("yikeRoomToken ", ProtocolKeywords.YikeRoomTokenPrefix);
            Assert.Equal("yikeMoveNumber ", ProtocolKeywords.YikeMoveNumberPrefix);
        }
    }
}
