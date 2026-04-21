namespace readboard
{
    internal enum FoxWindowKind
    {
        Unknown = 0,
        LiveRoom = 1,
        RecordView = 2
    }

    internal sealed class FoxWindowContext
    {
        public FoxWindowKind Kind { get; set; }
        public string RoomToken { get; set; }
        public int? LiveTitleMove { get; set; }
        public int? RecordCurrentMove { get; set; }
        public int? RecordTotalMove { get; set; }
        public bool RecordAtEnd { get; set; }
        public string TitleFingerprint { get; set; }

        public static FoxWindowContext Unknown()
        {
            return new FoxWindowContext { Kind = FoxWindowKind.Unknown };
        }

        public static FoxWindowContext CopyOf(FoxWindowContext context)
        {
            if (context == null)
                return Unknown();

            return new FoxWindowContext
            {
                Kind = context.Kind,
                RoomToken = context.RoomToken,
                LiveTitleMove = context.LiveTitleMove,
                RecordCurrentMove = context.RecordCurrentMove,
                RecordTotalMove = context.RecordTotalMove,
                RecordAtEnd = context.RecordAtEnd,
                TitleFingerprint = context.TitleFingerprint
            };
        }

        public int? ResolveDisplayedMoveNumber()
        {
            if (Kind == FoxWindowKind.LiveRoom)
                return LiveTitleMove;
            if (Kind == FoxWindowKind.RecordView)
                return RecordCurrentMove;
            return null;
        }
    }
}
