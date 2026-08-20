using System;
using System.Collections.Generic;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxMatchBarSeatResolverTests
    {
        [Fact]
        public void Resolve_SameNicknameMovingFromRightSeatToLeftSeat_RecognizesWhiteAndAuthorizesPlay()
        {
            const string saved = "鳕鱼の让子";

            AutoPlayColorResolution blackRoom = ResolvePlaying(
                saved,
                leftOcr: "对手甲",
                rightOcr: saved,
                directory: new[] { "对手甲", saved, "观众甲" });
            AutoPlayColorResolution whiteRoom = ResolvePlaying(
                saved,
                leftOcr: saved,
                rightOcr: "对手乙",
                directory: new[] { saved, "对手乙", "观众甲" });

            AssertRecognized(blackRoom, "black", AutoPlayColorStatus.RecognizedBlack);
            AssertRecognized(whiteRoom, "white", AutoPlayColorStatus.RecognizedWhite);
            Assert.Equal(new[] { "play>black>5 1000 0" }, IssuePlay(blackRoom));
            Assert.Equal(new[] { "play>white>5 1000 0" }, IssuePlay(whiteRoom));
        }

        [Fact]
        public void Resolve_SavedNicknameMissingFromDirectory_StaysUnknownAndDoesNotAuthorizePlay()
        {
            const string saved = "鳕鱼の让子";
            AutoPlayColorResolution resolution = ResolvePlaying(
                saved,
                leftOcr: saved,
                rightOcr: "无聊的BX",
                directory: new[] { "绝艺指导F", "无聊的BX", "观众甲" });

            AssertUnknown(resolution, AutoPlayColorStatus.NicknameNotMatched);
            Assert.Empty(IssuePlay(resolution));
        }

        [Fact]
        public void Resolve_WatchingTitleEvenWhenDirectoryContainsNickname_DoesNotAuthorizePlay()
        {
            const string saved = "鳕鱼の让子";
            AutoPlayColorResolution detected = FoxMatchBarSeatResolver.Resolve(
                saved,
                "对手甲",
                saved,
                new[] { "对手甲", saved, "观众甲" });
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
        public void Resolve_EmptyDirectory_StaysUnknownAndDoesNotAuthorizePlay()
        {
            const string saved = "鳕鱼の让子";
            AutoPlayColorResolution resolution = ResolvePlaying(
                saved,
                leftOcr: saved,
                rightOcr: "对手乙",
                directory: new string[0]);

            AssertUnknown(resolution, AutoPlayColorStatus.NicknameNotMatched);
            Assert.Empty(IssuePlay(resolution));
        }

        [Fact]
        public void Resolve_UniqueOcrFragmentForDecoratedDirectoryName_RecognizesAndAuthorizesPlay()
        {
            const string saved = "叶落メ让子";
            AutoPlayColorResolution resolution = ResolvePlaying(
                saved,
                leftOcr: "叶落让子",
                rightOcr: "真的不懂啊",
                directory: new[] { saved, "真的不懂啊", "观众甲" });

            AssertRecognized(resolution, "white", AutoPlayColorStatus.RecognizedWhite);
            Assert.Equal(new[] { "play>white>5 1000 0" }, IssuePlay(resolution));
        }

        [Fact]
        public void Resolve_TwoDirectoryNamesLookLikeOcrFragment_StaysUnknownAndDoesNotAuthorizePlay()
        {
            const string saved = "叶落メ让子";
            AutoPlayColorResolution resolution = ResolvePlaying(
                saved,
                leftOcr: "叶落让子",
                rightOcr: "真的不懂啊",
                directory: new[] { saved, "叶落让子", "真的不懂啊" });

            AssertUnknown(resolution, AutoPlayColorStatus.NicknameNotMatched);
            Assert.Empty(IssuePlay(resolution));
        }

        [Fact]
        public void Resolve_CorrectedDirectoryNameDiffersByOneCharacter_StaysUnknownAndDoesNotAuthorizePlay()
        {
            const string saved = "鳕鱼の让子";
            AutoPlayColorResolution resolution = ResolvePlaying(
                saved,
                leftOcr: "鳕鱼の让于",
                rightOcr: "对手丙",
                directory: new[] { "鳕鱼の让于", "对手丙" });

            AssertUnknown(resolution, AutoPlayColorStatus.NicknameNotMatched);
            Assert.Empty(IssuePlay(resolution));
        }

        [Fact]
        public void Resolve_SavedAndDirectoryEqualAfterStrippingDecorations_RecognizesAndAuthorizesPlay()
        {
            AutoPlayColorResolution resolution = ResolvePlaying(
                "♟晓舟·让子",
                leftOcr: "晓舟让子",
                rightOcr: "对手丁",
                directory: new[] { "晓舟让子", "对手丁" });

            AssertRecognized(resolution, "white", AutoPlayColorStatus.RecognizedWhite);
            Assert.Equal(new[] { "play>white>5 1000 0" }, IssuePlay(resolution));
        }

        private static AutoPlayColorResolution ResolvePlaying(
            string savedFoxNickname,
            string leftOcr,
            string rightOcr,
            IEnumerable<string> directory)
        {
            return Authorize(
                FoxMatchBarSeatResolver.Resolve(savedFoxNickname, leftOcr, rightOcr, directory),
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
