using System;
using System.Collections.Generic;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxMatchBarLiveRecognitionTests
    {
        [Fact]
        public void RecognizedRoom_FreezesUntilRoomChangeOrIdentityReselect()
        {
            FoxMatchBarLiveRecognition live = new FoxMatchBarLiveRecognition();
            DateTime t0 = new DateTime(2026, 8, 20, 7, 0, 0, DateTimeKind.Utc);
            IntPtr window = new IntPtr(1);

            AutoPlayColorResolution first = live.AcceptSample(
                window,
                "live|state=1|room=room-1",
                "鳕鱼の让子",
                t0,
                RightSeatReading("鳕鱼の让子"));

            AssertRecognized(Authorize(first, "鳕鱼の让子"), "black", AutoPlayColorStatus.RecognizedBlack);
            Assert.Equal(new[] { "play>black>5 1000 0" }, IssuePlay(Authorize(first, "鳕鱼の让子")));
            Assert.False(live.NeedsSample(
                window,
                "live|state=1|room=room-1",
                "鳕鱼の让子",
                t0.AddSeconds(5),
                false));

            FoxMatchBarReading oppositeSeat = LeftSeatReading("鳕鱼の让子");
            Assert.True(live.NeedsSample(
                window,
                "live|state=1|room=room-2",
                "鳕鱼の让子",
                t0.AddMilliseconds(100),
                false));
            AutoPlayColorResolution roomTwo = live.AcceptSample(
                window,
                "live|state=1|room=room-2",
                "鳕鱼の让子",
                t0.AddMilliseconds(100),
                oppositeSeat);
            AssertRecognized(Authorize(roomTwo, "鳕鱼の让子"), "white", AutoPlayColorStatus.RecognizedWhite);
            Assert.Equal(new[] { "play>white>5 1000 0" }, IssuePlay(Authorize(roomTwo, "鳕鱼の让子")));

            Assert.True(live.NeedsSample(
                window,
                "live|state=1|room=room-2",
                "鳕鱼の让子",
                t0.AddMilliseconds(100),
                true));
        }

        [Fact]
        public void UnknownRoom_RetriesOnExistingCadenceUntilRecognized()
        {
            FoxMatchBarLiveRecognition live = new FoxMatchBarLiveRecognition();
            DateTime t0 = new DateTime(2026, 8, 20, 7, 0, 0, DateTimeKind.Utc);
            IntPtr window = new IntPtr(1);
            const string room = "live|state=1|room=room-1";
            const string saved = "鳕鱼の让子";

            AutoPlayColorResolution unknown = live.AcceptSample(
                window,
                room,
                saved,
                t0,
                FoxMatchBarReading.Empty);
            AssertUnknown(Authorize(unknown, saved), AutoPlayColorStatus.NicknameNotMatched);
            Assert.Empty(IssuePlay(Authorize(unknown, saved)));
            Assert.False(live.NeedsSample(window, room, saved, t0.AddMilliseconds(999), false));
            Assert.True(live.NeedsSample(window, room, saved, t0.AddMilliseconds(1000), false));

            AutoPlayColorResolution recognized = live.AcceptSample(
                window,
                room,
                saved,
                t0.AddMilliseconds(1000),
                RightSeatReading(saved));
            AssertRecognized(Authorize(recognized, saved), "black", AutoPlayColorStatus.RecognizedBlack);
            Assert.False(live.NeedsSample(window, room, saved, t0.AddMilliseconds(5000), false));
        }

        [Fact]
        public void UnreadableOrEmptyDirectory_StaysUnknownAndDoesNotAuthorizePlay()
        {
            FoxMatchBarLiveRecognition live = new FoxMatchBarLiveRecognition();
            DateTime t0 = new DateTime(2026, 8, 20, 7, 0, 0, DateTimeKind.Utc);
            IntPtr window = new IntPtr(1);

            AutoPlayColorResolution minimized = live.AcceptSample(
                window,
                "live|state=1|room=room-1",
                "鳕鱼の让子",
                t0,
                FoxMatchBarReading.Empty);
            AutoPlayColorResolution emptyDirectory = live.AcceptSample(
                window,
                "live|state=1|room=room-1",
                "鳕鱼の让子",
                t0.AddMilliseconds(1000),
                new FoxMatchBarReading(Array.Empty<FoxPlayerListEntry>()));
            AutoPlayColorResolution nameOnlyInList = live.AcceptSample(
                window,
                "live|state=1|room=room-1",
                "鳕鱼の让子",
                t0.AddMilliseconds(2000),
                new FoxMatchBarReading(new[]
                {
                    new FoxPlayerListEntry("对手甲", AutoPlayColorResolution.Known("white", AutoPlayColorStatus.RecognizedWhite)),
                    new FoxPlayerListEntry("对手乙", AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack)),
                    new FoxPlayerListEntry("鳕鱼の让子", AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown))
                }));

            AssertUnknown(Authorize(minimized, "鳕鱼の让子"), AutoPlayColorStatus.NicknameNotMatched);
            AssertUnknown(Authorize(emptyDirectory, "鳕鱼の让子"), AutoPlayColorStatus.NicknameNotMatched);
            AssertUnknown(Authorize(nameOnlyInList, "鳕鱼の让子"), AutoPlayColorStatus.NicknameNotMatched);
            Assert.Empty(IssuePlay(Authorize(minimized, "鳕鱼の让子")));
            Assert.Empty(IssuePlay(Authorize(emptyDirectory, "鳕鱼の让子")));
            Assert.Empty(IssuePlay(Authorize(nameOnlyInList, "鳕鱼の让子")));
        }

        [Fact]
        public void SpectatingRoom_DoesNotAuthorizePlayEvenWhenMatchBarHasNickname()
        {
            AutoPlayColorResolution detected = new FoxMatchBarLiveRecognition().AcceptSample(
                new IntPtr(1),
                "live|state=2|room=room-1",
                "鳕鱼の让子",
                new DateTime(2026, 8, 20, 7, 0, 0, DateTimeKind.Utc),
                RightSeatReading("鳕鱼の让子"));

            AutoPlayColorResolution resolution = FoxAutoPlayColorResolver.Resolve(
                AutoPlayColorMode.FoxAuto,
                SyncMode.Fox,
                "鳕鱼の让子",
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Watching
                },
                detected);

            AssertUnknown(resolution, AutoPlayColorStatus.Spectating);
            Assert.Empty(IssuePlay(resolution));
        }

        private static FoxMatchBarReading RightSeatReading(string saved)
        {
            return new FoxMatchBarReading(new[]
            {
                new FoxPlayerListEntry("对手甲", AutoPlayColorResolution.Known("white", AutoPlayColorStatus.RecognizedWhite)),
                new FoxPlayerListEntry(saved, AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack)),
                new FoxPlayerListEntry("观众甲", AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown))
            });
        }

        private static FoxMatchBarReading LeftSeatReading(string saved)
        {
            return new FoxMatchBarReading(new[]
            {
                new FoxPlayerListEntry(saved, AutoPlayColorResolution.Known("white", AutoPlayColorStatus.RecognizedWhite)),
                new FoxPlayerListEntry("对手乙", AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack)),
                new FoxPlayerListEntry("观众甲", AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown))
            });
        }

        private static AutoPlayColorResolution Authorize(AutoPlayColorResolution detected, string saved)
        {
            return FoxAutoPlayColorResolver.Resolve(
                AutoPlayColorMode.FoxAuto,
                SyncMode.Fox,
                saved,
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Playing
                },
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
