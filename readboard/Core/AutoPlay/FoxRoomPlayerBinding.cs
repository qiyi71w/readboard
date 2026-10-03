using System;

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

        public static bool MatchesRoom(FoxWindowContext expected, FoxWindowContext actual)
        {
            return expected != null && actual != null
                && expected.Kind == FoxWindowKind.LiveRoom && actual.Kind == FoxWindowKind.LiveRoom
                && expected.LiveRoomState == FoxLiveRoomState.Playing
                && actual.LiveRoomState == FoxLiveRoomState.Playing
                && !string.IsNullOrWhiteSpace(expected.RoomToken)
                && string.Equals(expected.RoomToken, actual.RoomToken, StringComparison.Ordinal);
        }
    }
}
