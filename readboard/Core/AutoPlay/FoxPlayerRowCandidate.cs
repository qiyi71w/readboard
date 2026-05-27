namespace readboard
{
    internal sealed class FoxPlayerRowCandidate
    {
        public FoxPlayerRowCandidate(PixelRect rowBounds, PixelRect nicknameBounds, PixelRect stoneIconBounds)
        {
            RowBounds = rowBounds;
            NicknameBounds = nicknameBounds;
            StoneIconBounds = stoneIconBounds;
        }

        public PixelRect RowBounds { get; private set; }
        public PixelRect NicknameBounds { get; private set; }
        public PixelRect StoneIconBounds { get; private set; }
    }
}
