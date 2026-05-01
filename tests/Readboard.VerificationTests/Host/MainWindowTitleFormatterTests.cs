using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class MainWindowTitleFormatterTests
    {
        [Theory]
        [InlineData("棋盘同步工具", "3.0.1", "棋盘同步工具 v3.0.1")]
        [InlineData("棋盘同步工具", "v3.0.1", "棋盘同步工具 v3.0.1")]
        [InlineData("棋盘同步工具", "  V3.0.1  ", "棋盘同步工具 v3.0.1")]
        [InlineData("棋盘同步工具", "", "棋盘同步工具")]
        public void FormatBaseTitle_AppendsVersionAfterBaseTitle(
            string baseTitle,
            string releaseVersion,
            string expected)
        {
            Assert.Equal(expected, MainWindowTitleFormatter.FormatBaseTitle(baseTitle, releaseVersion));
        }

        [Fact]
        public void Format_ReturnsBaseTitleWhenTitleModeHidden()
        {
            string title = MainWindowTitleFormatter.Format(
                "棋盘同步工具",
                MainWindowTitleDisplayMode.Hidden,
                true,
                FoxWindowContext.Unknown(),
                "野狐",
                "房间",
                "棋谱",
                "同步中",
                "未抓到标题信息",
                "末手",
                "第{0}手",
                "第{0}/{1}手");

            Assert.Equal("棋盘同步工具", title);
        }

        [Fact]
        public void Format_ReturnsLiveRoomTitle()
        {
            string title = MainWindowTitleFormatter.Format(
                "棋盘同步工具",
                MainWindowTitleDisplayMode.Syncing,
                true,
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    RoomToken = "43581号",
                    LiveTitleMove = 89
                },
                "野狐",
                "房间",
                "棋谱",
                "同步中",
                "未抓到标题信息",
                "末手",
                "第{0}手",
                "第{0}/{1}手");

            Assert.Equal("棋盘同步工具 [房间][43581号][第89手]", title);
        }

        [Fact]
        public void Format_ReturnsRecordTitle()
        {
            string title = MainWindowTitleFormatter.Format(
                "棋盘同步工具",
                MainWindowTitleDisplayMode.Syncing,
                true,
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.RecordView,
                    RecordCurrentMove = 120,
                    RecordTotalMove = 333
                },
                "野狐",
                "房间",
                "棋谱",
                "同步中",
                "未抓到标题信息",
                "末手",
                "第{0}手",
                "第{0}/{1}手");

            Assert.Equal("棋盘同步工具 [棋谱][第120/333手]", title);
        }

        [Fact]
        public void Format_AppendsRecordEndTagWhenAtEnd()
        {
            string title = MainWindowTitleFormatter.Format(
                "棋盘同步工具",
                MainWindowTitleDisplayMode.Syncing,
                true,
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.RecordView,
                    RecordCurrentMove = 333,
                    RecordTotalMove = 333,
                    RecordAtEnd = true
                },
                "野狐",
                "房间",
                "棋谱",
                "同步中",
                "未抓到标题信息",
                "末手",
                "第{0}手",
                "第{0}/{1}手");

            Assert.Equal("棋盘同步工具 [棋谱][第333/333手][末手]", title);
        }

        [Fact]
        public void Format_ReturnsSyncingFailureTitleWhenWindowOrContextMissing()
        {
            string title = MainWindowTitleFormatter.Format(
                "棋盘同步工具",
                MainWindowTitleDisplayMode.Syncing,
                false,
                FoxWindowContext.Unknown(),
                "野狐",
                "房间",
                "棋谱",
                "同步中",
                "未抓到标题信息",
                "末手",
                "第{0}手",
                "第{0}/{1}手");

            Assert.Equal("棋盘同步工具 [野狐][同步中][未抓到标题信息]", title);
        }

        [Fact]
        public void Format_ReturnsRetainedFailureTitleWithoutSyncingTag()
        {
            string title = MainWindowTitleFormatter.Format(
                "棋盘同步工具",
                MainWindowTitleDisplayMode.RetainedSnapshot,
                false,
                FoxWindowContext.Unknown(),
                "野狐",
                "房间",
                "棋谱",
                "同步中",
                "未抓到标题信息",
                "末手",
                "第{0}手",
                "第{0}/{1}手");

            Assert.Equal("棋盘同步工具 [野狐][未抓到标题信息]", title);
        }

        [Fact]
        public void Format_UsesEnglishMoveFormatForLiveRoom()
        {
            string title = MainWindowTitleFormatter.Format(
                "Board Synchronization Tool",
                MainWindowTitleDisplayMode.Syncing,
                true,
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    RoomToken = "43581号",
                    LiveTitleMove = 89
                },
                "Fox",
                "Room",
                "Record",
                "Syncing",
                "Title unavailable",
                "Last move",
                "Move {0}",
                "Move {0}/{1}");

            Assert.Equal("Board Synchronization Tool [Room][43581号][Move 89]", title);
        }

        [Fact]
        public void Format_UsesKoreanMoveFormatForRecordTitle()
        {
            string title = MainWindowTitleFormatter.Format(
                "바둑판 동기화 도구",
                MainWindowTitleDisplayMode.Syncing,
                true,
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.RecordView,
                    RecordCurrentMove = 120,
                    RecordTotalMove = 333
                },
                "야호",
                "방",
                "기보",
                "동기화 중",
                "제목 정보 없음",
                "마지막 수",
                "제{0}수",
                "제{0}/{1}수");

            Assert.Equal("바둑판 동기화 도구 [기보][제120/333수]", title);
        }
    }
}
