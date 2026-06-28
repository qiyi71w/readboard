namespace readboard
{
    internal sealed class LastMoveInference
    {
        public LastMoveInference(BoardCoordinate coordinate, LastMoveSource source)
        {
            Coordinate = coordinate;
            Source = source;
        }

        public static LastMoveInference None { get; } = new LastMoveInference(null, LastMoveSource.None);

        public BoardCoordinate Coordinate { get; private set; }
        public LastMoveSource Source { get; private set; }
    }
}
