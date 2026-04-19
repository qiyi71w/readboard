namespace readboard
{
    internal sealed class BoardDimensions
    {
        public BoardDimensions()
        {
        }

        public BoardDimensions(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; set; }
        public int Height { get; set; }
    }
}
