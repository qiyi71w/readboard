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
        ForceRebuild = 6
    }

    internal sealed class ProtocolMessage
    {
        public ProtocolMessageKind Kind { get; set; }
        public string RawText { get; set; }
        public MoveRequest MoveRequest { get; set; }

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
