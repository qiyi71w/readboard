namespace readboard
{
    internal enum PixelBufferFormat
    {
        Rgb24 = 0
    }

    internal sealed class PixelBuffer
    {
        public PixelBufferFormat Format { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
        public byte[] Pixels { get; set; }
    }
}
