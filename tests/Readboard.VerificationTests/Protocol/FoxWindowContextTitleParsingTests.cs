using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class FoxWindowContextTitleParsingTests
    {
        [Theory]
        [InlineData("> [高级房1] > 43581号对弈房 观战中[第89手] - 升降级", "43581号", 89)]
        [InlineData("> [高级房1] > 23|890号房间 对弈中[第03手] - 友谊赛 - 数子规则", "23|890号", 3)]
        [InlineData("> [高级房1] > 43838号对弈房 对局结束 (白 中盘胜) - 升降级 - 数子规则 - [第308手]", "43838号", 308)]
        public void ParseLiveRoom_ExtractsRoomTokenAndDisplayedMove(
            string title,
            string expectedToken,
            int expectedMove)
        {
            FoxWindowContext context = FoxWindowContextParser.Parse(title);

            Assert.Equal(FoxWindowKind.LiveRoom, context.Kind);
            Assert.Equal(expectedToken, context.RoomToken);
            Assert.Equal(expectedMove, context.LiveTitleMove);
        }

        [Fact]
        public void ParseRecordView_UsesTotalMoveAsCurrentMoveWhenOnlyTotalMoveIsPresent()
        {
            FoxWindowContext context =
                FoxWindowContextParser.Parse(
                    "棋谱欣赏 - 黑 Ouuu12138 [2段] 对白 已吃2道 [2段] - 数子规则 - 分先 - 黑中盘胜 - [总333手]");

            Assert.Equal(FoxWindowKind.RecordView, context.Kind);
            Assert.Equal(333, context.RecordCurrentMove);
            Assert.Equal(333, context.RecordTotalMove);
            Assert.True(context.RecordAtEnd);
            Assert.False(string.IsNullOrWhiteSpace(context.TitleFingerprint));
        }

        [Fact]
        public void ParseRecordView_ExtractsCurrentAndTotalMoveFromObservedFoxRecordTitle()
        {
            FoxWindowContext context =
                FoxWindowContextParser.Parse(
                    "棋谱欣赏 - 黑 鳕鱼の让子 [8段] 对 白 野狐高段9D [9段] - 数子规则 - 分先 - 黑 超时负 - [第6手] - [总44手]");

            Assert.Equal(FoxWindowKind.RecordView, context.Kind);
            Assert.Equal(6, context.RecordCurrentMove);
            Assert.Equal(44, context.RecordTotalMove);
            Assert.False(context.RecordAtEnd);
        }
    }
}
