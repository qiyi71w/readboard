namespace readboard
{
    internal enum ProtocolMessageKind
    {
        LegacyLine = 0,
        PlaceMove = 1,
        LossFocus = 2,
        StopInBoard = 3,
        VersionRequest = 4,
        Quit = 5,
        ForceRebuild = 6,
        ReadboardUpdateSupported = 7,
        ReadboardUpdateInstalling = 8,
        ReadboardUpdateCancelled = 9,
        ReadboardUpdateFailed = 10,
        YikeContext = 11,
        YikeGeometry = 12,
        YikeBrowserSyncStop = 13
    }

    internal sealed class ProtocolMessage
    {
        public ProtocolMessageKind Kind { get; set; }
        public string RawText { get; set; }
        public MoveRequest MoveRequest { get; set; }
        public string YikeRoomToken { get; set; }
        public int? YikeMoveNumber { get; set; }
        public YikeBoardGeometry YikeGeometry { get; set; }

        public static ProtocolMessage CreateLegacyLine(string rawText)
        {
            return new ProtocolMessage { Kind = ProtocolMessageKind.LegacyLine, RawText = rawText };
        }

        public static ProtocolMessage CreateForceRebuildLine(string rawText)
        {
            return new ProtocolMessage { Kind = ProtocolMessageKind.ForceRebuild, RawText = rawText };
        }
    }
}
