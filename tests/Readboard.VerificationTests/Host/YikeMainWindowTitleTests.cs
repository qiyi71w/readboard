using Xunit;
using readboard;

namespace Readboard.VerificationTests.Host
{
    public sealed class YikeMainWindowTitleTests
    {
        [Fact]
        public void renders_room_and_move()
        {
            string text = MainWindowTitleFormatter.FormatYike(
                "棋盘同步工具",
                MainWindowTitleDisplayMode.Syncing,
                true,
                new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 },
                "弈客",
                "号",
                "第{0}手",
                "未抓到上下文",
                "同步中");

            Assert.Equal("棋盘同步工具 [弈客][65191829号][第16手]", text);
        }

        [Fact]
        public void renders_move_only_when_no_room()
        {
            string text = MainWindowTitleFormatter.FormatYike(
                "棋盘同步工具",
                MainWindowTitleDisplayMode.Syncing,
                true,
                new YikeWindowContext { MoveNumber = 16 },
                "弈客",
                "号",
                "第{0}手",
                "未抓到上下文",
                "同步中");

            Assert.Equal("棋盘同步工具 [弈客][第16手]", text);
        }

        [Fact]
        public void renders_missing_when_no_handle()
        {
            string text = MainWindowTitleFormatter.FormatYike(
                "棋盘同步工具",
                MainWindowTitleDisplayMode.Syncing,
                false,
                YikeWindowContext.Unknown(),
                "弈客",
                "号",
                "第{0}手",
                "未抓到上下文",
                "同步中");

            Assert.Equal("棋盘同步工具 [弈客][同步中][未抓到上下文]", text);
        }

        [Fact]
        public void same_signature_yields_identical_output()
        {
            YikeWindowContext ctx = new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 };

            string a = MainWindowTitleFormatter.FormatYike("棋盘同步工具", MainWindowTitleDisplayMode.Syncing, true, ctx, "弈客", "号", "第{0}手", "未抓到上下文", "同步中");
            string b = MainWindowTitleFormatter.FormatYike("棋盘同步工具", MainWindowTitleDisplayMode.Syncing, true, ctx, "弈客", "号", "第{0}手", "未抓到上下文", "同步中");

            Assert.Equal(a, b);
        }
    }
}
