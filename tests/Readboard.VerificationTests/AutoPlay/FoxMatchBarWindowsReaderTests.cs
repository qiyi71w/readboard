using System.Collections.Generic;
using Xunit;
using readboard;
namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxMatchBarWindowsReaderTests
    {
        [Fact]
        public void IsPlayerNickname_RejectsRanksAndListChrome()
        {
            Assert.True(FoxMatchBarSeatResolver.IsPlayerNickname("鳕鱼の让子"));
            Assert.False(FoxMatchBarSeatResolver.IsPlayerNickname("8段"));
            Assert.False(FoxMatchBarSeatResolver.IsPlayerNickname("18级"));
            Assert.False(FoxMatchBarSeatResolver.IsPlayerNickname("132"));
            Assert.False(FoxMatchBarSeatResolver.IsPlayerNickname("用户名(16)"));
            Assert.False(FoxMatchBarSeatResolver.IsPlayerNickname("Rich Edit Object"));
        }

        [Fact]
        public void SelectNicknamesFollowedByRank_TakesUsernameColumnFromFlatUiaDump()
        {
            IList<string> names = FoxMatchBarSeatResolver.SelectNicknamesFollowedByRank(
                new[]
                {
                    "标题", "用户名(16)", "棋力", "胜", "负", "财富",
                    "垂直滚动条", "苹果天使", "9段", "132", "56", "布衣",
                    "阿珐莉娅", "9段", "109", "37", "碉堡",
                    "鳕鱼の让子", "8段", "62", "14", "月光族"
                });

            Assert.Equal(new[] { "苹果天使", "阿珐莉娅", "鳕鱼の让子" }, names);
        }
    }
}
