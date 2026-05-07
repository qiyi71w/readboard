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

        [Fact]
        public void exposes_yike_geometry_inbound_prefix()
        {
            Assert.Equal("yikeGeometry", ProtocolKeywords.YikeGeometry);
        }

        [Fact]
        public void exposes_yike_sync_control_commands()
        {
            Assert.Equal("yikeSyncStart", ProtocolKeywords.YikeSyncStart);
            Assert.Equal("yikeSyncStop", ProtocolKeywords.YikeSyncStop);
            Assert.Equal("yikeBrowserSyncStop", ProtocolKeywords.YikeBrowserSyncStop);
        }
    }
}
