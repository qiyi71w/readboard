namespace readboard
{
    internal enum MovePlacementFailureKind
    {
        None = 0,
        MissingFrame = 1,
        MissingMove = 2,
        UnsupportedPath = 3,
        PlacementFailed = 4
    }

    internal enum PlacementPathKind
    {
        Unknown = 0,
        Foreground = 1,
        BackgroundPost = 2,
        BackgroundSend = 3,
        LightweightInterop = 4
    }

    internal sealed class MovePlacementResult
    {
        public bool Success { get; set; }
        public PlacementPathKind PlacementPath { get; set; }
        public BoardCoordinate Coordinate { get; set; }
        public MovePlacementFailureKind FailureKind { get; set; }
        public string FailureReason { get; set; }
    }
}
