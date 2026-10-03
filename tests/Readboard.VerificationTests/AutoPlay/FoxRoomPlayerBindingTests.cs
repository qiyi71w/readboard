using System;
using System.Collections.Generic;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxRoomPlayerBindingTests
    {
        [Theory]
        [InlineData("45471号", 46359, true)]
        [InlineData("45024号", 46359, false)]
        [InlineData("45024号", 45912, true)]
        [InlineData("45399号", 46287, true)]
        [InlineData("44998号", 46287, false)]
        [InlineData("44998号", 45886, true)]
        [InlineData("44998号", 0, false)]
        [InlineData("2147483647号", 887, false)]
        [InlineData("room-1", 889, false)]
        public void RoomPanelMustMatchExpectedTitle(string room, int controlId, bool matches)
        {
            var expected = Playing(room);
            Assert.Equal(matches, FoxRoomPlayerBinding.MatchesRoom(expected, Playing(room), controlId));
        }

        [Theory]
        [InlineData("44998号", (int)FoxWindowKind.LiveRoom, (int)FoxLiveRoomState.Playing)]
        [InlineData("45399号", (int)FoxWindowKind.LiveRoom, (int)FoxLiveRoomState.Watching)]
        [InlineData("45399号", (int)FoxWindowKind.LiveRoom, (int)FoxLiveRoomState.Unknown)]
        [InlineData("45399号", (int)FoxWindowKind.RecordView, (int)FoxLiveRoomState.Playing)]
        public void DifferentTitleOrNonPlayingStateCannotBind(string room, int kind, int state)
        {
            var actual = new FoxWindowContext
            {
                RoomToken = room, Kind = (FoxWindowKind)kind, LiveRoomState = (FoxLiveRoomState)state
            };
            Assert.False(FoxRoomPlayerBinding.MatchesRoom(Playing("45399号"), actual, 46287));
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("room")]
        [InlineData("list")]
        [InlineData("title")]
        [InlineData("control-id")]
        public void BindingChangeDuringUiaRead_DiscardsSeats(string changed)
        {
            var before = new FoxRoomPlayerBinding(new IntPtr(10), new IntPtr(11), new IntPtr(12), 46287);
            FoxRoomPlayerBinding? after = changed == "missing" ? (FoxRoomPlayerBinding?)null
                : new FoxRoomPlayerBinding(new IntPtr(changed == "room" ? 20 : 10),
                    new IntPtr(changed == "list" ? 21 : 11), new IntPtr(changed == "title" ? 22 : 12),
                    changed == "control-id" ? 45886 : 46287);
            int reads = 0;
            FoxMatchBarReading reading = FoxMatchBarWindowsReader.ReadBoundPlayers(new IntPtr(42), Playing("45399号"),
                (board, expected) => ++reads == 1 ? before : after, list => Seats());
            Assert.False(FoxMatchBarSeatResolver.Resolve("self", reading.Players).IsKnown);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void IncompleteNewRoomList_DoesNotAuthorizeFirstRow(int count)
        {
            var seats = new List<FoxPlayerListEntry>();
            if (count == 1) seats.Add(new FoxPlayerListEntry("self", null));
            var binding = new FoxRoomPlayerBinding(new IntPtr(10), new IntPtr(11), new IntPtr(12), 46287);
            var reading = FoxMatchBarWindowsReader.ReadBoundPlayers(new IntPtr(42), Playing("45399号"),
                (board, expected) => binding, list => seats);
            Assert.False(FoxMatchBarSeatResolver.Resolve("self", reading.Players).IsKnown);
        }

        [Fact]
        public void MatchingRoomWithTwoSeats_AutomaticallyResolvesWhite()
        {
            var binding = new FoxRoomPlayerBinding(new IntPtr(10), new IntPtr(11), new IntPtr(12), 46287);
            var reading = FoxMatchBarWindowsReader.ReadBoundPlayers(new IntPtr(42), Playing("45399号"),
                (board, expected) => binding, list => Seats());
            Assert.Equal("white", FoxMatchBarSeatResolver.Resolve("self", reading.Players).PlayColor);
        }

        private static FoxWindowContext Playing(string room)
        {
            return new FoxWindowContext
            {
                Kind = FoxWindowKind.LiveRoom, LiveRoomState = FoxLiveRoomState.Playing, RoomToken = room
            };
        }

        private static IList<FoxPlayerListEntry> Seats()
        {
            return new[] { new FoxPlayerListEntry("self", null), new FoxPlayerListEntry("other", null) };
        }
    }
}
