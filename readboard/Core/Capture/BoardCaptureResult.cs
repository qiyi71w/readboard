namespace readboard
{
    internal enum CapturePathKind
    {
        Unknown = 0,
        ScreenCopy = 1,
        WindowBitmap = 2,
        PixelBuffer = 3
    }

    internal enum BoardCaptureFailureKind
    {
        None = 0,
        InvalidSelection = 1,
        WindowUnavailable = 2,
        CaptureFailed = 3,
        UnsupportedWindow = 4
    }

    internal sealed class BoardCaptureResult
    {
        public bool Success { get; set; }
        public BoardFrame Frame { get; set; }
        public CapturePathKind CapturePath { get; set; }
        public BoardCaptureFailureKind FailureKind { get; set; }
        public string FailureReason { get; set; }

        public static BoardCaptureResult CreateFailure(BoardCaptureFailureKind failureKind, string failureReason)
        {
            return new BoardCaptureResult
            {
                Success = false,
                CapturePath = CapturePathKind.Unknown,
                FailureKind = failureKind,
                FailureReason = failureReason
            };
        }

        public static BoardCaptureResult CreateSuccess(BoardFrame frame, CapturePathKind capturePath)
        {
            return new BoardCaptureResult
            {
                Success = true,
                Frame = frame,
                CapturePath = capturePath,
                FailureKind = BoardCaptureFailureKind.None
            };
        }
    }

    internal sealed class BoardCapturePlan
    {
        public WindowDescriptor Window { get; set; }
        public PixelRect SourceBounds { get; set; }
        public PixelRect ScreenBounds { get; set; }
        public CapturePathKind CapturePath { get; set; }
        public bool UsePrintWindow { get; set; }
    }

    internal sealed class CropResolution
    {
        public PixelRect SourceBounds { get; set; }
        public PixelRect ScreenBounds { get; set; }
    }

    internal static class BoardContentHash
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        private const int ByteMask = 0xFF;
        private const int BitsPerByte = 8;
        private const int IntByteCount = 4;

        public static ulong Start()
        {
            return FnvOffsetBasis;
        }

        public static ulong AppendByte(ulong current, byte value)
        {
            return (current ^ value) * FnvPrime;
        }

        public static ulong AppendInt(ulong current, int value)
        {
            ulong next = current;
            for (int shift = 0; shift < IntByteCount * BitsPerByte; shift += BitsPerByte)
            {
                byte nextByte = (byte)((value >> shift) & ByteMask);
                next = AppendByte(next, nextByte);
            }

            return next;
        }

        public static ulong Finish(ulong current, int first, int second, int third)
        {
            ulong next = AppendInt(current, first);
            next = AppendInt(next, second);
            return AppendInt(next, third);
        }

        public static ulong Compute(PixelBuffer buffer)
        {
            if (buffer == null || buffer.Pixels == null)
                return 0UL;

            int length = buffer.Stride * buffer.Height;
            if (buffer.Width <= 0 || buffer.Height <= 0 || buffer.Stride <= 0)
                return 0UL;
            if (buffer.Pixels.Length < length)
                return 0UL;

            ulong hash = Start();
            for (int i = 0; i < length; i++)
                hash = AppendByte(hash, buffer.Pixels[i]);

            return Finish(hash, buffer.Width, buffer.Height, buffer.Stride);
        }
    }
}
