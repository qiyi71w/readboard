using System;
using System.Globalization;

namespace readboard
{
    internal readonly struct FoxRoomPlayerBinding
    {
        public FoxRoomPlayerBinding(IntPtr roomHandle, IntPtr listHandle, IntPtr titleHandle, int controlId)
        {
            RoomHandle = roomHandle;
            ListHandle = listHandle;
            TitleHandle = titleHandle;
            ControlId = controlId;
        }

        public IntPtr RoomHandle { get; }
        public IntPtr ListHandle { get; }
        public IntPtr TitleHandle { get; }
        public int ControlId { get; }

        public bool IsSameBinding(FoxRoomPlayerBinding other)
        {
            return RoomHandle == other.RoomHandle && ListHandle == other.ListHandle
                && TitleHandle == other.TitleHandle && ControlId == other.ControlId;
        }

        public static bool MatchesRoom(FoxWindowContext expected, FoxWindowContext actual, int controlId)
        {
            if (expected == null || actual == null
                || expected.Kind != FoxWindowKind.LiveRoom || actual.Kind != FoxWindowKind.LiveRoom
                || expected.LiveRoomState != FoxLiveRoomState.Playing
                || actual.LiveRoomState != FoxLiveRoomState.Playing
                || !string.Equals(expected.RoomToken, actual.RoomToken, StringComparison.Ordinal))
                return false;

            string token = expected.RoomToken;
            if (string.IsNullOrEmpty(token) || !token.EndsWith("号", StringComparison.Ordinal))
                return false;
            int roomNumber;
            if (!int.TryParse(token.AsSpan(0, token.Length - 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out roomNumber) || roomNumber <= 0)
                return false;

            // Observed Fox native layout (four rooms, two processes), not a public Fox API.
            // Unsupported layouts must remain unknown rather than authorize another room's list.
            const int RoomControlIdOffset = 888;
            return roomNumber <= int.MaxValue - RoomControlIdOffset
                && controlId == roomNumber + RoomControlIdOffset;
        }
    }
}
