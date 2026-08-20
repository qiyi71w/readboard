using System;
using System.Collections.Generic;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxMatchBarSeatResolverTests
    {
        [Fact]
        public void Resolve_SameNicknameMovingFromBlackStoneToWhiteStone_RecognizesAndAuthorizesPlay()
        {
            const string saved = "鳕鱼の让子";

            AutoPlayColorResolution blackRoom = ResolvePlaying(
                saved,
                Black("对手甲"),
                Black(saved),
                Spectator("观众甲"));
            AutoPlayColorResolution whiteRoom = ResolvePlaying(
                saved,
                White(saved),
                Black("对手乙"),
                Spectator("观众甲"));

            AssertRecognized(blackRoom, "black", AutoPlayColorStatus.RecognizedBlack);
            AssertRecognized(whiteRoom, "white", AutoPlayColorStatus.RecognizedWhite);
            Assert.Equal(new[] { "play>black>5 1000 0" }, IssuePlay(blackRoom));
            Assert.Equal(new[] { "play>white>5 1000 0" }, IssuePlay(whiteRoom));
        }

        [Fact]
        public void Resolve_SavedNicknameMissingFromList_StaysUnknownAndDoesNotAuthorizePlay()
        {
            const string saved = "鳕鱼の让子";
            AutoPlayColorResolution resolution = ResolvePlaying(
                saved,
                White("绝艺指导F"),
                Black("无聊的BX"),
                Spectator("观众甲"));

            AssertUnknown(resolution, AutoPlayColorStatus.NicknameNotMatched);
            Assert.Empty(IssuePlay(resolution));
        }

        [Fact]
        public void Resolve_WatchingTitleEvenWhenListContainsNickname_DoesNotAuthorizePlay()
        {
            const string saved = "鳕鱼の让子";
            AutoPlayColorResolution detected = FoxMatchBarSeatResolver.Resolve(
                saved,
                new[] { White("对手甲"), Black(saved), Spectator("观众甲") });
            AutoPlayColorResolution resolution = Authorize(
                detected,
                saved,
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Watching
                });

            AssertUnknown(resolution, AutoPlayColorStatus.Spectating);
            Assert.Empty(IssuePlay(resolution));
        }

        [Fact]
        public void Resolve_EmptyList_StaysUnknownAndDoesNotAuthorizePlay()
        {
            AutoPlayColorResolution resolution = ResolvePlaying("鳕鱼の让子");

            AssertUnknown(resolution, AutoPlayColorStatus.NicknameNotMatched);
            Assert.Empty(IssuePlay(resolution));
        }

        [Fact]
        public void Resolve_NicknameInListWithoutStone_StaysUnknownAndDoesNotAuthorizePlay()
        {
            const string saved = "鳕鱼の让子";
            AutoPlayColorResolution resolution = ResolvePlaying(
                saved,
                White("苹果天使"),
                Black("阿珐莉娅"),
                Spectator(saved));

            AssertUnknown(resolution, AutoPlayColorStatus.NicknameNotMatched);
            Assert.Empty(IssuePlay(resolution));
        }

        [Fact]
        public void Resolve_TwoPlayersSecondRow_IsBlackRegardlessOfStonePixels()
        {
            const string saved = "鳕鱼の让子";
            AutoPlayColorResolution resolution = ResolvePlaying(
                saved,
                Spectator("野狐启蒙级"),
                Spectator(saved));

            AssertRecognized(resolution, "black", AutoPlayColorStatus.RecognizedBlack);
            Assert.Equal(new[] { "play>black>5 1000 0" }, IssuePlay(resolution));
        }

        [Fact]
        public void Resolve_SavedAndListEqualAfterStrippingDecorations_RecognizesAndAuthorizesPlay()
        {
            AutoPlayColorResolution resolution = ResolvePlaying(
                "♟晓舟·让子",
                White("晓舟让子"),
                Black("对手丁"));

            AssertRecognized(resolution, "white", AutoPlayColorStatus.RecognizedWhite);
            Assert.Equal(new[] { "play>white>5 1000 0" }, IssuePlay(resolution));
        }

        private static AutoPlayColorResolution ResolvePlaying(
            string savedFoxNickname,
            params FoxPlayerListEntry[] players)
        {
            return Authorize(
                FoxMatchBarSeatResolver.Resolve(savedFoxNickname, players),
                savedFoxNickname,
                PlayingContext());
        }

        private static AutoPlayColorResolution Authorize(
            AutoPlayColorResolution detected,
            string savedFoxNickname,
            FoxWindowContext context)
        {
            return FoxAutoPlayColorResolver.Resolve(
                AutoPlayColorMode.FoxAuto,
                SyncMode.Fox,
                savedFoxNickname,
                context,
                detected);
        }

        private static FoxPlayerListEntry White(string name)
        {
            return new FoxPlayerListEntry(
                name,
                AutoPlayColorResolution.Known("white", AutoPlayColorStatus.RecognizedWhite));
        }

        private static FoxPlayerListEntry Black(string name)
        {
            return new FoxPlayerListEntry(
                name,
                AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack));
        }

        private static FoxPlayerListEntry Spectator(string name)
        {
            return new FoxPlayerListEntry(
                name,
                AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown));
        }

        private static void AssertRecognized(
            AutoPlayColorResolution resolution,
            string playColor,
            AutoPlayColorStatus status)
        {
            Assert.True(resolution.IsKnown);
            Assert.Equal(playColor, resolution.PlayColor);
            Assert.Equal(status, resolution.Status);
        }

        private static void AssertUnknown(AutoPlayColorResolution resolution, AutoPlayColorStatus status)
        {
            Assert.False(resolution.IsKnown);
            Assert.Null(resolution.PlayColor);
            Assert.Equal(status, resolution.Status);
        }

        private static IList<string> IssuePlay(AutoPlayColorResolution resolution)
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(
                transport,
                new LegacyProtocolAdapter());
            ControlCenterRuntimeSnapshot snapshot = new ControlCenterRuntimeSnapshot
            {
                TwoWaySync = true,
                AutoPlayEnabled = true,
                AutoPlayColorMode = AutoPlayColorMode.FoxAuto,
                AutoPlayMoveMode = AutoPlayMoveMode.FirstCandidate,
                AutoPlayColorResolution = resolution,
                PlayColor = resolution.PlayColor,
                AiTimeValue = "5",
                PlayoutsValue = "1000",
                FirstPolicyValue = "0"
            };

            AutoPlayWireIssuer.IssueIfAuthorized(snapshot, keepSync: true, coordinator);
            return transport.SentLines;
        }

        private static FoxWindowContext PlayingContext()
        {
            return new FoxWindowContext
            {
                Kind = FoxWindowKind.LiveRoom,
                LiveRoomState = FoxLiveRoomState.Playing
            };
        }

        private sealed class RecordingTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived
            {
                add { }
                remove { }
            }

            public List<string> SentLines { get; } = new List<string>();

            public bool IsConnected { get; private set; }

            public void Dispose()
            {
            }

            public void Send(string line)
            {
                SentLines.Add(line);
            }

            public void SendError(string message)
            {
            }

            public void Start()
            {
                IsConnected = true;
            }

            public void Stop()
            {
                IsConnected = false;
            }
        }
    }
}
