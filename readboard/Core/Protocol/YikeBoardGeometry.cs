namespace readboard
{
    internal sealed class YikeBoardGeometry
    {
        public PixelRect Bounds { get; set; }
        public int BoardSize { get; set; }
        public double? FirstIntersectionX { get; set; }
        public double? FirstIntersectionY { get; set; }
        public double CellWidth { get; set; }
        public double CellHeight { get; set; }

        public bool IsUsable
        {
            get
            {
                return Bounds != null
                    && Bounds.Width > 0
                    && Bounds.Height > 0
                    && BoardSize > 0;
            }
        }

        public static YikeBoardGeometry CopyOf(YikeBoardGeometry geometry)
        {
            if (geometry == null)
                return null;

            return new YikeBoardGeometry
            {
                Bounds = geometry.Bounds == null
                    ? null
                    : new PixelRect(
                        geometry.Bounds.X,
                        geometry.Bounds.Y,
                        geometry.Bounds.Width,
                        geometry.Bounds.Height),
                BoardSize = geometry.BoardSize,
                FirstIntersectionX = geometry.FirstIntersectionX,
                FirstIntersectionY = geometry.FirstIntersectionY,
                CellWidth = geometry.CellWidth,
                CellHeight = geometry.CellHeight
            };
        }
    }
}
