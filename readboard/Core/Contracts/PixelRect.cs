namespace readboard
{
    internal sealed class PixelRect
    {
        public PixelRect()
        {
        }

        public PixelRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public bool IsEmpty
        {
            get { return Width <= 0 || Height <= 0; }
        }
    }
}
