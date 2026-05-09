namespace readboard
{
    internal sealed class BoardViewport
    {
        public PixelRect SourceBounds { get; set; }
        public PixelRect ScreenBounds { get; set; }
        public double? FirstIntersectionX { get; set; }
        public double? FirstIntersectionY { get; set; }
        public double CellWidth { get; set; }
        public double CellHeight { get; set; }
    }
}
