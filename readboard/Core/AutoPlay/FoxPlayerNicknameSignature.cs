using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace readboard
{
    internal sealed class FoxPlayerNicknameSignature
    {
        private const string Prefix = "v1";
        private const int MinimumGlyphPixels = 12;
        private const double ReliableScoreThreshold = 0.70d;
        private const double ReliableMarginThreshold = 0.20d;
        private const int MaxShift = 3;
        private const int NormalizedWidth = 96;
        private const int NormalizedHeight = 24;
        private const int NormalizedPadding = 2;
        private const int MaximumSerializedPixels = 1000000;

        private readonly bool[] mask;

        private FoxPlayerNicknameSignature(int width, int height, bool[] mask, int glyphPixels)
        {
            Width = width;
            Height = height;
            this.mask = mask;
            GlyphPixels = glyphPixels;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int GlyphPixels { get; private set; }
        public bool IsValid
        {
            get { return mask != null && GlyphPixels >= MinimumGlyphPixels; }
        }

        public static FoxPlayerNicknameSignature FromBitmap(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                return Invalid();

            bool[] pixels = new bool[bitmap.Width * bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (!IsGlyphPixel(bitmap.GetPixel(x, y)))
                        continue;

                    pixels[y * bitmap.Width + x] = true;
                }
            }

            return CreateNormalized(pixels, bitmap.Width, bitmap.Height);
        }

        public static FoxPlayerNicknameSignature FromString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Invalid();

            string[] parts = value.Split(':');
            if (parts.Length != 4 || parts[0] != Prefix)
                return Invalid();

            int width;
            int height;
            if (!int.TryParse(parts[1], out width) || !int.TryParse(parts[2], out height) || width <= 0 || height <= 0)
                return Invalid();

            try
            {
                int bitCount = checked(width * height);
                if (bitCount > MaximumSerializedPixels)
                    return Invalid();

                byte[] bytes = Convert.FromBase64String(parts[3]);
                int expectedBytes = (bitCount + 7) / 8;
                if (bytes.Length < expectedBytes)
                    return Invalid();

                bool[] pixels = UnpackBits(bytes, bitCount);
                return CreateNormalized(pixels, width, height);
            }
            catch (FormatException)
            {
                return Invalid();
            }
            catch (OverflowException)
            {
                return Invalid();
            }
            catch (ArgumentException)
            {
                return Invalid();
            }
        }

        public string Serialize()
        {
            if (!IsValid)
                return string.Empty;

            return string.Concat(Prefix, ":", Width, ":", Height, ":", Convert.ToBase64String(PackBits(mask)));
        }

        public FoxPlayerNicknameMatch Match(IList<Bitmap> candidates)
        {
            if (!IsValid || candidates == null || candidates.Count == 0)
                return FoxPlayerNicknameMatch.None();

            int bestIndex = -1;
            double bestScore = 0d;
            double secondScore = 0d;
            for (int i = 0; i < candidates.Count; i++)
            {
                double score = Compare(candidates[i]);
                if (score > bestScore)
                {
                    secondScore = bestScore;
                    bestScore = score;
                    bestIndex = i;
                }
                else if (score > secondScore)
                {
                    secondScore = score;
                }
            }

            double margin = bestScore - secondScore;
            bool reliable = bestIndex >= 0
                && bestScore >= ReliableScoreThreshold
                && margin >= ReliableMarginThreshold;
            return new FoxPlayerNicknameMatch(bestIndex, bestScore, secondScore, reliable);
        }

        public double Compare(Bitmap candidate)
        {
            if (!IsValid || candidate == null)
                return 0d;

            FoxPlayerNicknameSignature candidateSignature = FromBitmap(candidate);
            if (!candidateSignature.IsValid || candidateSignature.Width != Width || candidateSignature.Height != Height)
                return 0d;

            double best = 0d;
            for (int dy = -MaxShift; dy <= MaxShift; dy++)
            {
                for (int dx = -MaxShift; dx <= MaxShift; dx++)
                    best = Math.Max(best, Score(mask, candidateSignature.mask, Width, Height, dx, dy));
            }

            return best;
        }

        private static FoxPlayerNicknameSignature Invalid()
        {
            return new FoxPlayerNicknameSignature(0, 0, null, 0);
        }

        private static FoxPlayerNicknameSignature CreateNormalized(bool[] pixels, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 0 || pixels.Length != width * height)
                return Invalid();

            int left;
            int top;
            int right;
            int bottom;
            int sourceGlyphPixels;
            if (!TryGetGlyphBounds(pixels, width, height, out left, out top, out right, out bottom, out sourceGlyphPixels))
                return Invalid();

            if (sourceGlyphPixels < MinimumGlyphPixels)
                return Invalid();

            bool[] normalized = new bool[NormalizedWidth * NormalizedHeight];
            int sourceWidth = right - left + 1;
            int sourceHeight = bottom - top + 1;
            int targetMaxWidth = NormalizedWidth - NormalizedPadding * 2;
            int targetMaxHeight = NormalizedHeight - NormalizedPadding * 2;
            double scale = Math.Min(targetMaxWidth / (double)sourceWidth, targetMaxHeight / (double)sourceHeight);
            int targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            int targetLeft = (NormalizedWidth - targetWidth) / 2;
            int targetTop = (NormalizedHeight - targetHeight) / 2;

            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    if (!pixels[y * width + x])
                        continue;

                    int x0 = targetLeft + (int)Math.Floor((x - left) * scale);
                    int x1 = targetLeft + (int)Math.Ceiling((x - left + 1) * scale) - 1;
                    int y0 = targetTop + (int)Math.Floor((y - top) * scale);
                    int y1 = targetTop + (int)Math.Ceiling((y - top + 1) * scale) - 1;
                    SetNormalizedPixels(normalized, x0, y0, x1, y1);
                }
            }

            int glyphPixels = CountTrue(normalized);
            return new FoxPlayerNicknameSignature(NormalizedWidth, NormalizedHeight, normalized, glyphPixels);
        }

        private static bool TryGetGlyphBounds(
            bool[] pixels,
            int width,
            int height,
            out int left,
            out int top,
            out int right,
            out int bottom,
            out int glyphPixels)
        {
            left = width;
            top = height;
            right = -1;
            bottom = -1;
            glyphPixels = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!pixels[y * width + x])
                        continue;

                    glyphPixels++;
                    if (x < left)
                        left = x;
                    if (x > right)
                        right = x;
                    if (y < top)
                        top = y;
                    if (y > bottom)
                        bottom = y;
                }
            }

            return right >= left && bottom >= top;
        }

        private static void SetNormalizedPixels(bool[] pixels, int x0, int y0, int x1, int y1)
        {
            int left = Math.Max(0, Math.Min(x0, x1));
            int right = Math.Min(NormalizedWidth - 1, Math.Max(x0, x1));
            int top = Math.Max(0, Math.Min(y0, y1));
            int bottom = Math.Min(NormalizedHeight - 1, Math.Max(y0, y1));
            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                    pixels[y * NormalizedWidth + x] = true;
            }
        }

        private static bool IsGlyphPixel(Color color)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));
            int saturation = max - min;
            return max < 80
                || (saturation >= 45 && max >= 80)
                || (max <= 120 && saturation >= 18);
        }

        private static double Score(bool[] source, bool[] candidate, int width, int height, int dx, int dy)
        {
            int intersection = 0;
            int union = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool sourceOn = source[y * width + x];
                    int candidateX = x + dx;
                    int candidateY = y + dy;
                    bool candidateOn = candidateX >= 0
                        && candidateX < width
                        && candidateY >= 0
                        && candidateY < height
                        && candidate[candidateY * width + candidateX];

                    if (sourceOn || candidateOn)
                        union++;
                    if (sourceOn && candidateOn)
                        intersection++;
                }
            }

            return union == 0 ? 0d : intersection / (double)union;
        }

        private static byte[] PackBits(bool[] pixels)
        {
            byte[] bytes = new byte[(pixels.Length + 7) / 8];
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i])
                    bytes[i / 8] |= (byte)(1 << (i % 8));
            }
            return bytes;
        }

        private static bool[] UnpackBits(byte[] bytes, int bitCount)
        {
            bool[] pixels = new bool[bitCount];
            for (int i = 0; i < bitCount; i++)
                pixels[i] = (bytes[i / 8] & (1 << (i % 8))) != 0;
            return pixels;
        }

        private static int CountTrue(bool[] pixels)
        {
            int count = 0;
            foreach (bool pixel in pixels)
            {
                if (pixel)
                    count++;
            }
            return count;
        }
    }

    internal sealed class FoxPlayerNicknameMatch
    {
        public FoxPlayerNicknameMatch(int index, double score, double secondScore, bool isReliable)
        {
            Index = index;
            Score = score;
            SecondScore = secondScore;
            IsReliable = isReliable;
        }

        public int Index { get; private set; }
        public double Score { get; private set; }
        public double SecondScore { get; private set; }
        public double Margin
        {
            get { return Score - SecondScore; }
        }

        public bool IsReliable { get; private set; }

        public static FoxPlayerNicknameMatch None()
        {
            return new FoxPlayerNicknameMatch(-1, 0d, 0d, false);
        }
    }
}
