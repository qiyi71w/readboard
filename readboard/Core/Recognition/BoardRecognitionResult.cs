using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace readboard
{
    internal enum BoardRecognitionFailureKind
    {
        None = 0,
        MissingFrame = 1,
        MissingImage = 2,
        InvalidBoardArea = 3,
        RecognitionFailed = 4
    }

    internal sealed class BoardRecognitionResult
    {
        public bool Success { get; set; }
        public BoardSnapshot Snapshot { get; set; }
        public BoardViewport Viewport { get; set; }
        public BoardRecognitionFailureKind FailureKind { get; set; }
        public string FailureReason { get; set; }
        public bool UsedCachedSnapshot { get; set; }

        public static BoardRecognitionResult CreateFailure(BoardRecognitionFailureKind failureKind, string reason)
        {
            return new BoardRecognitionResult
            {
                Success = false,
                FailureKind = failureKind,
                FailureReason = reason
            };
        }
    }

    internal static class LegacyBoardLocator
    {
        private const int TygemRedMin = 234;
        private const int TygemGreenMin = 204;
        private const int TygemBlueMin = 121;
        private const int TygemBlueMax = 179;

        public static bool TryResolveViewport(
            BoardRecognitionRequest request,
            LegacyPixelMap pixels,
            out BoardViewport viewport,
            out string failureReason)
        {
            failureReason = null;
            PixelRect sourceBounds = GetExistingBounds(request.Frame.Viewport, pixels);
            if (sourceBounds == null)
            {
                if (!TryResolveBounds(request.Frame.SyncMode, pixels, out sourceBounds))
                {
                    viewport = null;
                    failureReason = "Unable to resolve board bounds from the current frame.";
                    return false;
                }
            }

            BoardDimensions boardSize = request.Frame.BoardSize;
            if (sourceBounds.Width < boardSize.Width || sourceBounds.Height < boardSize.Height)
            {
                viewport = null;
                failureReason = "Board bounds are smaller than the requested board size.";
                return false;
            }

            viewport = CreateViewport(request.Frame.Viewport, sourceBounds, boardSize);
            return true;
        }

        private static PixelRect GetExistingBounds(BoardViewport viewport, LegacyPixelMap pixels)
        {
            if (viewport == null || viewport.SourceBounds == null)
                return null;

            return ClipBounds(viewport.SourceBounds, pixels.Width, pixels.Height);
        }

        private static BoardViewport CreateViewport(BoardViewport existing, PixelRect sourceBounds, BoardDimensions boardSize)
        {
            return new BoardViewport
            {
                SourceBounds = CopyRect(sourceBounds),
                ScreenBounds = existing != null ? CopyRect(existing.ScreenBounds) : null,
                CellWidth = sourceBounds.Width / (double)boardSize.Width,
                CellHeight = sourceBounds.Height / (double)boardSize.Height
            };
        }

        private static PixelRect CopyRect(PixelRect rect)
        {
            if (rect == null)
                return null;

            return new PixelRect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static PixelRect ClipBounds(PixelRect bounds, int totalWidth, int totalHeight)
        {
            if (bounds == null)
                return null;

            int startX = Math.Max(0, bounds.X);
            int startY = Math.Max(0, bounds.Y);
            int endX = Math.Min(totalWidth, bounds.X + bounds.Width);
            int endY = Math.Min(totalHeight, bounds.Y + bounds.Height);
            if (endX <= startX || endY <= startY)
                return null;

            return new PixelRect(startX, startY, endX - startX, endY - startY);
        }

        private static bool TryResolveBounds(SyncMode syncMode, LegacyPixelMap pixels, out PixelRect sourceBounds)
        {
            switch (syncMode)
            {
                case SyncMode.Fox:
                case SyncMode.FoxBackgroundPlace:
                    return TryResolveFoxBounds(pixels, out sourceBounds);
                case SyncMode.Sina:
                    return TryResolveSinaBounds(pixels, out sourceBounds);
                case SyncMode.Tygem:
                    return TryResolveTygemBounds(pixels, out sourceBounds);
                default:
                    sourceBounds = new PixelRect(0, 0, pixels.Width, pixels.Height);
                    return true;
            }
        }

        private static bool TryResolveTygemBounds(LegacyPixelMap pixels, out PixelRect sourceBounds)
        {
            sourceBounds = null;
            if (!pixels.IsWithinBounds(0, 0))
                return false;

            LegacyRgbInfo rgb = pixels.GetPixel(0, 0);
            bool isMatch = rgb.Red >= TygemRedMin
                && rgb.Green >= TygemGreenMin
                && rgb.Blue >= TygemBlueMin
                && rgb.Blue <= TygemBlueMax;
            if (!isMatch)
                return false;

            sourceBounds = new PixelRect(0, 0, pixels.Width, pixels.Height);
            return true;
        }

        private static bool TryResolveSinaBounds(LegacyPixelMap pixels, out PixelRect sourceBounds)
        {
            sourceBounds = null;
            ColorPattern[] patterns = CreateSinaPatterns();
            SearchPoint upLeft;
            SearchPoint downLeft;
            if (!TryFindPattern(pixels, patterns, SearchDirection.LeftTop, out upLeft))
                return false;
            if (!TryFindPattern(pixels, patterns, SearchDirection.LeftBottom, out downLeft))
                return false;

            int size = downLeft.Y - upLeft.Y + 4;
            if (size <= 0)
                return false;

            sourceBounds = ClipBounds(
                new PixelRect(upLeft.X - 1, upLeft.Y - 1, size, size),
                pixels.Width,
                pixels.Height);
            return sourceBounds != null;
        }

        private static bool TryResolveFoxBounds(LegacyPixelMap pixels, out PixelRect sourceBounds)
        {
            sourceBounds = null;
            SearchPoint upLeft;
            SearchPoint upRight;
            if (!TryFindPattern(pixels, CreateFoxLeftPatterns(), SearchDirection.LeftTop, out upLeft))
                return false;
            if (!TryFindPattern(pixels, CreateFoxRightPatterns(), SearchDirection.RightTop, out upRight))
                return false;

            int size = upRight.X - upLeft.X;
            if (size <= 0)
                return false;

            sourceBounds = ClipBounds(
                new PixelRect(upLeft.X, upLeft.Y, size, size),
                pixels.Width,
                pixels.Height);
            return sourceBounds != null;
        }

        private static bool TryFindPattern(
            LegacyPixelMap pixels,
            IList<ColorPattern> patterns,
            SearchDirection direction,
            out SearchPoint point)
        {
            SearchPlan plan = CreateSearchPlan(direction, pixels.Width, pixels.Height);
            if (plan.IsXOuterLoop)
            {
                return TryFindPatternByX(pixels, patterns, plan, out point);
            }

            return TryFindPatternByY(pixels, patterns, plan, out point);
        }

        private static bool TryFindPatternByX(
            LegacyPixelMap pixels,
            IList<ColorPattern> patterns,
            SearchPlan plan,
            out SearchPoint point)
        {
            for (int x = plan.XStart; x != plan.XEnd; x += plan.XStep)
            {
                for (int y = plan.YStart; y != plan.YEnd; y += plan.YStep)
                {
                    if (IsPatternMatch(pixels, x, y, patterns))
                    {
                        point = new SearchPoint(x, y);
                        return true;
                    }
                }
            }

            point = SearchPoint.NotFound;
            return false;
        }

        private static bool TryFindPatternByY(
            LegacyPixelMap pixels,
            IList<ColorPattern> patterns,
            SearchPlan plan,
            out SearchPoint point)
        {
            for (int y = plan.YStart; y != plan.YEnd; y += plan.YStep)
            {
                for (int x = plan.XStart; x != plan.XEnd; x += plan.XStep)
                {
                    if (IsPatternMatch(pixels, x, y, patterns))
                    {
                        point = new SearchPoint(x, y);
                        return true;
                    }
                }
            }

            point = SearchPoint.NotFound;
            return false;
        }

        private static SearchPlan CreateSearchPlan(SearchDirection direction, int width, int height)
        {
            switch (direction)
            {
                case SearchDirection.RightTop:
                    return new SearchPlan(width - 1, -1, -1, 0, height, 1, false);
                case SearchDirection.LeftBottom:
                    return new SearchPlan(0, width, 1, height - 1, -1, -1, true);
                case SearchDirection.RightBottom:
                    return new SearchPlan(width - 1, -1, -1, height - 1, -1, -1, true);
                default:
                    return new SearchPlan(0, width, 1, 0, height, 1, true);
            }
        }

        private static bool IsPatternMatch(LegacyPixelMap pixels, int startX, int startY, IList<ColorPattern> patterns)
        {
            if (!pixels.IsWithinBounds(startX, startY))
                return false;

            if (!patterns[0].Matches(pixels.GetPixel(startX, startY)))
                return false;

            for (int index = 0; index < patterns.Count; index++)
            {
                ColorPattern pattern = patterns[index];
                int x = startX + pattern.XOffset;
                int y = startY + pattern.YOffset;
                if (!pixels.IsWithinBounds(x, y))
                    return false;

                LegacyRgbInfo rgb = pixels.GetPixel(x, y);
                if (pattern.IsReverse)
                {
                    if (pattern.Matches(rgb))
                        return false;
                    continue;
                }

                if (!pattern.Matches(rgb))
                    return false;
            }

            return true;
        }

        private static ColorPattern[] CreateSinaPatterns()
        {
            return new[]
            {
                new ColorPattern(251, 218, 162, 5, 5, 5, 0, 0, false),
                new ColorPattern(251, 218, 162, 5, 5, 5, 0, 1, false),
                new ColorPattern(251, 218, 162, 5, 5, 5, 0, 2, false)
            };
        }

        private static ColorPattern[] CreateFoxLeftPatterns()
        {
            return new[]
            {
                new ColorPattern(49, 49, 49, 5, 5, 5, 0, 0, false),
                new ColorPattern(46, 46, 46, 5, 5, 5, 0, 1, false),
                new ColorPattern(46, 46, 46, 5, 5, 5, 0, 2, false),
                new ColorPattern(46, 46, 46, 5, 5, 5, 1, 0, false),
                new ColorPattern(46, 46, 46, 5, 5, 5, 2, 0, false),
                new ColorPattern(46, 46, 46, 5, 5, 5, 2, 1, true),
                new ColorPattern(46, 46, 46, 5, 5, 5, 1, 1, true),
                new ColorPattern(46, 46, 46, 5, 5, 5, 1, 2, true)
            };
        }

        private static ColorPattern[] CreateFoxRightPatterns()
        {
            return new[]
            {
                new ColorPattern(49, 49, 49, 5, 5, 5, 0, 0, false),
                new ColorPattern(46, 46, 46, 5, 5, 5, 0, 1, false),
                new ColorPattern(46, 46, 46, 5, 5, 5, 0, 2, false),
                new ColorPattern(46, 46, 46, 5, 5, 5, -1, 0, false),
                new ColorPattern(46, 46, 46, 5, 5, 5, -2, 0, false),
                new ColorPattern(46, 46, 46, 5, 5, 5, -2, 1, true),
                new ColorPattern(46, 46, 46, 5, 5, 5, -1, 1, true),
                new ColorPattern(46, 46, 46, 5, 5, 5, -1, 2, true)
            };
        }
    }

    internal sealed class LegacyPixelMap
    {
        private readonly byte[] pixels;

        private LegacyPixelMap(int width, int height, int stride, byte[] pixels)
        {
            Width = width;
            Height = height;
            Stride = stride;
            this.pixels = pixels;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Stride { get; private set; }

        public static bool TryCreate(
            BoardFrame frame,
            out LegacyPixelMap pixelMap,
            out BoardRecognitionFailureKind failureKind,
            out string failureReason)
        {
            pixelMap = null;
            failureKind = BoardRecognitionFailureKind.None;
            failureReason = null;

            if (frame == null)
            {
                failureKind = BoardRecognitionFailureKind.MissingFrame;
                failureReason = "Board frame is required.";
                return false;
            }

            if (TryCreateFromPixelBuffer(frame.PixelBuffer, out pixelMap))
                return true;

            if (TryCreateFromBitmap(frame.Image, out pixelMap, out failureReason))
                return true;

            failureKind = BoardRecognitionFailureKind.MissingImage;
            if (string.IsNullOrWhiteSpace(failureReason))
                failureReason = "Board frame does not contain a readable image.";
            return false;
        }

        public bool IsWithinBounds(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        public LegacyRgbInfo GetPixel(int x, int y)
        {
            int sourceIndex = (y * Stride) + (x * 3);
            return new LegacyRgbInfo(
                pixels[sourceIndex + 2],
                pixels[sourceIndex + 1],
                pixels[sourceIndex]);
        }

        private static bool TryCreateFromPixelBuffer(PixelBuffer buffer, out LegacyPixelMap pixelMap)
        {
            pixelMap = null;
            if (buffer == null || buffer.Pixels == null)
                return false;

            if (buffer.Format != PixelBufferFormat.Rgb24)
                return false;
            if (buffer.Width <= 0 || buffer.Height <= 0 || buffer.Stride < buffer.Width * 3)
                return false;
            if (buffer.Pixels.Length < buffer.Stride * buffer.Height)
                return false;

            pixelMap = new LegacyPixelMap(buffer.Width, buffer.Height, buffer.Stride, buffer.Pixels);
            return true;
        }

        private static bool TryCreateFromBitmap(Bitmap image, out LegacyPixelMap pixelMap, out string failureReason)
        {
            pixelMap = null;
            failureReason = null;
            if (image == null)
                return false;

            Bitmap source = null;
            try
            {
                source = ConvertTo24BppIfNeeded(image);
                Rectangle rect = new Rectangle(0, 0, source.Width, source.Height);
                BitmapData data = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    byte[] copiedPixels = new byte[data.Stride * data.Height];
                    Marshal.Copy(data.Scan0, copiedPixels, 0, copiedPixels.Length);
                    pixelMap = new LegacyPixelMap(data.Width, data.Height, data.Stride, copiedPixels);
                    return true;
                }
                finally
                {
                    source.UnlockBits(data);
                }
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }
            finally
            {
                if (!ReferenceEquals(source, image) && source != null)
                    source.Dispose();
            }
        }

        private static Bitmap ConvertTo24BppIfNeeded(Bitmap image)
        {
            if (image.PixelFormat == PixelFormat.Format24bppRgb)
                return image;

            Bitmap converted = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(converted))
            {
                graphics.DrawImage(image, new Rectangle(0, 0, converted.Width, converted.Height));
            }

            return converted;
        }
    }

    internal sealed class LegacyBoardAnalysis
    {
        public BoardCellState[] BoardState { get; set; }
        public int BlackStoneCount { get; set; }
        public int WhiteStoneCount { get; set; }
        public BoardCoordinate LastMove { get; set; }
    }

    internal sealed class StoneSummary
    {
        private const int MissingPercent = 200;

        public StoneSummary(BoardCellState normalState, BoardCellState lastMoveState)
        {
            NormalState = normalState;
            LastMoveState = lastMoveState;
            MinPercent = MissingPercent;
        }

        public BoardCellState NormalState { get; private set; }
        public BoardCellState LastMoveState { get; private set; }
        public int Count { get; private set; }
        public int TotalPercent { get; private set; }
        public int MinPercent { get; private set; }
        public BoardCoordinate MinCoordinate { get; private set; }

        public void Observe(int percent, int x, int y)
        {
            Count++;
            TotalPercent += percent;
            if (percent >= MinPercent)
                return;

            MinPercent = percent;
            MinCoordinate = new BoardCoordinate(x, y);
        }
    }

    internal sealed class MarkerSummary
    {
        public int RedCount { get; private set; }
        public int BlueCount { get; private set; }
        public BoardCoordinate Candidate { get; private set; }

        public void Observe(int redPercent, int bluePercent, int threshold, int x, int y)
        {
            if (bluePercent >= threshold)
            {
                BlueCount++;
                Candidate = new BoardCoordinate(x, y);
            }

            if (redPercent >= threshold)
            {
                RedCount++;
                Candidate = new BoardCoordinate(x, y);
            }
        }
    }

    internal sealed class ColorPattern
    {
        public ColorPattern(
            int red,
            int green,
            int blue,
            int redOffset,
            int greenOffset,
            int blueOffset,
            int xOffset,
            int yOffset,
            bool isReverse)
        {
            Red = red;
            Green = green;
            Blue = blue;
            RedOffset = redOffset;
            GreenOffset = greenOffset;
            BlueOffset = blueOffset;
            XOffset = xOffset;
            YOffset = yOffset;
            IsReverse = isReverse;
        }

        public int Red { get; private set; }
        public int Green { get; private set; }
        public int Blue { get; private set; }
        public int RedOffset { get; private set; }
        public int GreenOffset { get; private set; }
        public int BlueOffset { get; private set; }
        public int XOffset { get; private set; }
        public int YOffset { get; private set; }
        public bool IsReverse { get; private set; }

        public bool Matches(LegacyRgbInfo rgb)
        {
            return Math.Abs(Red - rgb.Red) < RedOffset
                && Math.Abs(Green - rgb.Green) < GreenOffset
                && Math.Abs(Blue - rgb.Blue) < BlueOffset;
        }
    }

    internal struct LegacyRgbInfo
    {
        public LegacyRgbInfo(byte red, byte green, byte blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
        }

        public byte Red;
        public byte Green;
        public byte Blue;
    }

    internal struct RegionMetrics
    {
        public RegionMetrics(
            int blackPercent,
            int whitePercent,
            int pureWhitePercent,
            int almostWhitePercent,
            int redPercent,
            int bluePercent,
            bool hasTrueWhiteEvidence)
        {
            BlackPercent = blackPercent;
            WhitePercent = whitePercent;
            PureWhitePercent = pureWhitePercent;
            AlmostWhitePercent = almostWhitePercent;
            RedPercent = redPercent;
            BluePercent = bluePercent;
            HasTrueWhiteEvidence = hasTrueWhiteEvidence;
        }

        public int BlackPercent;
        public int WhitePercent;
        public int PureWhitePercent;
        public int AlmostWhitePercent;
        public int RedPercent;
        public int BluePercent;
        public bool HasTrueWhiteEvidence;
    }

    internal struct SearchPoint
    {
        public static readonly SearchPoint NotFound = new SearchPoint(-1, -1);

        public SearchPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    internal struct SearchPlan
    {
        public SearchPlan(int xStart, int xEnd, int xStep, int yStart, int yEnd, int yStep, bool isXOuterLoop)
        {
            XStart = xStart;
            XEnd = xEnd;
            XStep = xStep;
            YStart = yStart;
            YEnd = yEnd;
            YStep = yStep;
            IsXOuterLoop = isXOuterLoop;
        }

        public int XStart;
        public int XEnd;
        public int XStep;
        public int YStart;
        public int YEnd;
        public int YStep;
        public bool IsXOuterLoop;
    }

    internal enum SearchDirection
    {
        LeftTop = 0,
        RightTop = 1,
        LeftBottom = 2,
        RightBottom = 3
    }
}
