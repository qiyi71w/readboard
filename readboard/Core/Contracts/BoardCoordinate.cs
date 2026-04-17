namespace readboard
{
    internal sealed class BoardCoordinate
    {
        public BoardCoordinate()
        {
        }

        public BoardCoordinate(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; set; }
        public int Y { get; set; }
    }
}
