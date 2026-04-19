using System;
using System.Collections.Generic;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using CvPoint = OpenCvSharp.Point;

namespace readboard
{
    internal interface ILegacySelectionCalibrationService
    {
        LegacySelectionCalibrationResult Calibrate(Rectangle selectionBounds, BoardDimensions boardSize);
    }

    internal sealed class LegacySelectionCalibrationResult
    {
        private LegacySelectionCalibrationResult()
        {
        }

        public bool Success { get; private set; }

        public Bitmap CapturedBitmap { get; private set; }

        public Rectangle SelectionBounds { get; private set; }

        public string FailureReason { get; private set; }

        public static LegacySelectionCalibrationResult CreateSuccess(Bitmap capturedBitmap, Rectangle selectionBounds)
        {
            return new LegacySelectionCalibrationResult
            {
                Success = true,
                CapturedBitmap = capturedBitmap,
                SelectionBounds = selectionBounds
            };
        }

        public static LegacySelectionCalibrationResult CreateFailure(Bitmap capturedBitmap, string failureReason)
        {
            return new LegacySelectionCalibrationResult
            {
                Success = false,
                CapturedBitmap = capturedBitmap,
                FailureReason = failureReason
            };
        }
    }

    internal sealed class LegacySelectionCalibrationService : ILegacySelectionCalibrationService
    {
        private const double InitialLowCanny = 200d;
        private const double InitialHighCanny = 550d;
        private const double CannyRelaxFactor = 0.9d;
        private const int MinimumDetectedLineCount = 10;
        private const int MaxCannyRelaxAttempts = 10;
        private const int NeighborMergeTolerance = 4;
        private const int GapTolerance = 3;
        private const double LineLengthFactor = 1.5d;
        private const double HoughAngleDivisor = 180d;
        private const int HoughThresholdDivisor = 1;
        private const int HoughMinimumLineGap = 1;
        private const int ValidationGapDivisor = 8;
        private const int OffsetGapDivisor = 3;
        private const int OffsetPaddingDivisor = 2;
        private const double HalfGapDivisor = 2d;

        public LegacySelectionCalibrationResult Calibrate(Rectangle selectionBounds, BoardDimensions boardSize)
        {
            if (!HasValidSelection(selectionBounds))
                return LegacySelectionCalibrationResult.CreateFailure(null, "Selection bounds are invalid.");
            if (!HasValidBoardSize(boardSize))
                return LegacySelectionCalibrationResult.CreateFailure(null, "Board size is invalid.");

            Bitmap capturedBitmap = null;
            try
            {
                capturedBitmap = CaptureSelection(selectionBounds);
                Rectangle adjustedBounds = ResolveAdjustedBounds(capturedBitmap, selectionBounds, boardSize);
                return LegacySelectionCalibrationResult.CreateSuccess(capturedBitmap, adjustedBounds);
            }
            catch (Exception ex)
            {
                return LegacySelectionCalibrationResult.CreateFailure(capturedBitmap, ex.ToString());
            }
        }

        private static bool HasValidSelection(Rectangle selectionBounds)
        {
            return selectionBounds.Width > 0 && selectionBounds.Height > 0;
        }

        private static bool HasValidBoardSize(BoardDimensions boardSize)
        {
            return boardSize != null && boardSize.Width > 1 && boardSize.Height > 1;
        }

        private static Bitmap CaptureSelection(Rectangle selectionBounds)
        {
            Bitmap capturedBitmap = new Bitmap(selectionBounds.Width, selectionBounds.Height);
            try
            {
                using (Graphics graphics = Graphics.FromImage(capturedBitmap))
                    graphics.CopyFromScreen(selectionBounds.Location, System.Drawing.Point.Empty, selectionBounds.Size);
                return capturedBitmap;
            }
            catch
            {
                capturedBitmap.Dispose();
                throw;
            }
        }

        private static Rectangle ResolveAdjustedBounds(Bitmap capturedBitmap, Rectangle selectionBounds, BoardDimensions boardSize)
        {
            using (Mat sourceImage = BitmapConverter.ToMat(capturedBitmap))
            {
                LegacyDetectedLines detectedLines = DetectBoardLines(sourceImage, boardSize);
                int verticalGap = ResolveDominantGap(BuildGapCounts(GetLineGaps(detectedLines.VerticalLines)), "vertical");
                int horizontalGap = ResolveDominantGap(BuildGapCounts(GetLineGaps(detectedLines.HorizontalLines)), "horizontal");
                MarkValidatedLines(detectedLines.VerticalLines, detectedLines.HorizontalLines, verticalGap, horizontalGap);

                int boardPixelWidth = verticalGap * (boardSize.Width - 1);
                int boardPixelHeight = horizontalGap * (boardSize.Height - 1);
                List<LegacyWholeOffset> verticalOffsets = BuildVerticalOffsets(detectedLines.VerticalLines, boardPixelWidth);
                List<LegacyWholeOffset> horizontalOffsets = BuildHorizontalOffsets(detectedLines.HorizontalLines, boardPixelHeight);
                TrimVerticalOffsets(verticalOffsets, detectedLines.HorizontalLines, verticalGap);
                TrimHorizontalOffsets(horizontalOffsets, detectedLines.VerticalLines, horizontalGap);
                return BuildAdjustedBounds(selectionBounds, verticalOffsets[0], horizontalOffsets[0], verticalGap, horizontalGap);
            }
        }

        private static LegacyDetectedLines DetectBoardLines(Mat sourceImage, BoardDimensions boardSize)
        {
            double lowCanny = InitialLowCanny;
            double highCanny = InitialHighCanny;
            LegacyDetectedLines detectedLines = CollectDetectedLines(sourceImage, boardSize, (int)lowCanny, (int)highCanny);
            int attempts = MaxCannyRelaxAttempts;
            while (detectedLines.TotalCount < MinimumDetectedLineCount && attempts > 0)
            {
                lowCanny *= CannyRelaxFactor;
                highCanny *= CannyRelaxFactor;
                detectedLines = CollectDetectedLines(sourceImage, boardSize, (int)lowCanny, (int)highCanny);
                attempts--;
            }

            EnsureDetectedLines(detectedLines.VerticalLines, "vertical");
            EnsureDetectedLines(detectedLines.HorizontalLines, "horizontal");
            return detectedLines;
        }

        private static LegacyDetectedLines CollectDetectedLines(Mat sourceImage, BoardDimensions boardSize, int lowCanny, int highCanny)
        {
            return CreateDetectedLines(FindLines(sourceImage, boardSize, lowCanny, highCanny));
        }

        private static LineSegmentPoint[] FindLines(Mat sourceImage, BoardDimensions boardSize, int lowCanny, int highCanny)
        {
            using (Mat contours = new Mat())
            {
                Cv2.Canny(sourceImage, contours, lowCanny, highCanny);
                int imageLength = Math.Min(sourceImage.Width, sourceImage.Height);
                int boardLength = Math.Min(boardSize.Width, boardSize.Height);
                int minimumLineLength = (int)(imageLength / ((boardLength + 1) * LineLengthFactor));
                return Cv2.HoughLinesP(
                    contours,
                    HoughThresholdDivisor,
                    Cv2.PI / HoughAngleDivisor,
                    minimumLineLength,
                    minimumLineLength,
                    HoughMinimumLineGap);
            }
        }

        private static LegacyDetectedLines CreateDetectedLines(LineSegmentPoint[] lines)
        {
            LegacyDetectedLines detectedLines = new LegacyDetectedLines();
            if (lines == null || lines.Length == 0)
                return detectedLines;

            foreach (LineSegmentPoint line in lines)
                AddDetectedLine(detectedLines, line);

            MergeDetectedLines(detectedLines.VerticalLines, MergeVerticalLines);
            MergeDetectedLines(detectedLines.HorizontalLines, MergeHorizontalLines);
            return detectedLines;
        }

        private static void AddDetectedLine(LegacyDetectedLines detectedLines, LineSegmentPoint line)
        {
            int x1 = line.P1.X;
            int x2 = line.P2.X;
            int y1 = line.P1.Y;
            int y2 = line.P2.Y;
            if (Math.Abs(x1 - x2) >= 2 && Math.Abs(y1 - y2) >= 2)
                return;

            if (Math.Abs(y1 - y2) >= 2)
            {
                int position = (x1 + x2) / 2;
                detectedLines.VerticalLines.Add(new LegacyVerticalLine(position, Math.Min(y1, y2), Math.Max(y1, y2)));
                return;
            }

            int horizontalPosition = (y1 + y2) / 2;
            detectedLines.HorizontalLines.Add(new LegacyHorizontalLine(horizontalPosition, Math.Min(x1, x2), Math.Max(x1, x2)));
        }

        private static void MergeDetectedLines<TLine>(List<TLine> lines, Action<List<TLine>> merge)
            where TLine : LegacyLine
        {
            lines.Sort((left, right) => left.Position.CompareTo(right.Position));
            merge(lines);
            lines.RemoveAll(line => line.NeedDelete);
        }

        private static void MergeVerticalLines(List<LegacyVerticalLine> lines)
        {
            MergeVerticalLinesFromStart(lines);
            MergeVerticalLinesFromEnd(lines);
        }

        private static void MergeVerticalLinesFromStart(List<LegacyVerticalLine> lines)
        {
            for (int index = 0; index < lines.Count / 2; index++)
                TryMergeVerticalPair(lines[index], lines[index + 1]);
        }

        private static void MergeVerticalLinesFromEnd(List<LegacyVerticalLine> lines)
        {
            if (lines.Count < 2)
                return;

            for (int index = lines.Count; index > lines.Count / 2; index--)
            {
                LegacyVerticalLine current = lines[index - 1];
                LegacyVerticalLine previous = lines[index - 2];
                if (!previous.NeedDelete)
                    TryMergeVerticalPair(previous, current);
            }
        }

        private static void TryMergeVerticalPair(LegacyVerticalLine first, LegacyVerticalLine second)
        {
            if (Math.Abs(first.Position - second.Position) > NeighborMergeTolerance)
                return;

            second.Position = (first.Position + second.Position) / 2;
            second.StartPoint = new CvPoint(second.Position, Math.Min(first.StartPoint.Y, second.StartPoint.Y));
            second.EndPoint = new CvPoint(second.Position, Math.Max(first.EndPoint.Y, second.EndPoint.Y));
            first.NeedDelete = true;
        }

        private static void MergeHorizontalLines(List<LegacyHorizontalLine> lines)
        {
            MergeHorizontalLinesFromStart(lines);
            MergeHorizontalLinesFromEnd(lines);
        }

        private static void MergeHorizontalLinesFromStart(List<LegacyHorizontalLine> lines)
        {
            for (int index = 0; index < lines.Count / 2; index++)
                TryMergeHorizontalPair(lines[index], lines[index + 1]);
        }

        private static void MergeHorizontalLinesFromEnd(List<LegacyHorizontalLine> lines)
        {
            if (lines.Count < 2)
                return;

            for (int index = lines.Count; index > lines.Count / 2; index--)
            {
                LegacyHorizontalLine current = lines[index - 1];
                LegacyHorizontalLine previous = lines[index - 2];
                if (!previous.NeedDelete)
                    TryMergeHorizontalPair(previous, current);
            }
        }

        private static void TryMergeHorizontalPair(LegacyHorizontalLine first, LegacyHorizontalLine second)
        {
            if (Math.Abs(first.Position - second.Position) > NeighborMergeTolerance)
                return;

            second.Position = (first.Position + second.Position) / 2;
            second.StartPoint = new CvPoint(Math.Min(first.StartPoint.X, second.StartPoint.X), second.Position);
            second.EndPoint = new CvPoint(Math.Max(first.EndPoint.X, second.EndPoint.X), second.Position);
            first.NeedDelete = true;
        }

        private static void EnsureDetectedLines<TLine>(List<TLine> lines, string axis)
            where TLine : LegacyLine
        {
            if (lines == null || lines.Count < 2)
                throw new InvalidOperationException("Unable to detect enough " + axis + " board lines.");
        }

        private static List<int> GetLineGaps<TLine>(List<TLine> lines)
            where TLine : LegacyLine
        {
            List<int> gaps = new List<int>();
            for (int index = 0; index < lines.Count - 1; index++)
                gaps.Add(lines[index + 1].Position - lines[index].Position);
            return gaps;
        }

        private static List<LegacyGapCount> BuildGapCounts(List<int> gaps)
        {
            List<LegacyGapCount> gapCounts = new List<LegacyGapCount>();
            foreach (int gap in gaps)
                AddGapCountIfMissing(gapCounts, gap);

            foreach (int gap in gaps)
                UpdateGapCounts(gapCounts, gap);
            gapCounts.Sort(CompareGapCounts);
            return gapCounts;
        }

        private static void AddGapCountIfMissing(List<LegacyGapCount> gapCounts, int gap)
        {
            foreach (LegacyGapCount gapCount in gapCounts)
            {
                if (gapCount.Gap == gap)
                    return;
            }

            gapCounts.Add(new LegacyGapCount(gap));
        }

        private static void UpdateGapCounts(List<LegacyGapCount> gapCounts, int gap)
        {
            foreach (LegacyGapCount gapCount in gapCounts)
            {
                if (gap == gapCount.Gap)
                {
                    gapCount.LooseCounts++;
                    gapCount.UniqueCounts++;
                }
                else if (Math.Abs(gap - gapCount.Gap) <= GapTolerance)
                {
                    gapCount.LooseCounts++;
                }
            }
        }

        private static int CompareGapCounts(LegacyGapCount left, LegacyGapCount right)
        {
            if (left == null && right == null)
                return 0;
            if (left != null && right == null)
                return 1;
            if (left == null)
                return -1;
            if (left.UniqueCounts > right.UniqueCounts)
                return -1;
            if (left.UniqueCounts == right.UniqueCounts)
                return left.LooseCounts > right.LooseCounts ? -1 : 1;
            return 1;
        }

        private static int ResolveDominantGap(List<LegacyGapCount> gapCounts, string axis)
        {
            if (gapCounts == null || gapCounts.Count == 0)
                throw new InvalidOperationException("Unable to resolve " + axis + " board spacing.");

            int dominantGap = gapCounts[0].Gap;
            if (gapCounts.Count < 3)
                return dominantGap;

            LegacyGapCount first = gapCounts[0];
            LegacyGapCount second = gapCounts[1];
            LegacyGapCount third = gapCounts[2];
            if (Math.Abs(first.Gap - second.Gap) > 2 || Math.Abs(first.Gap - third.Gap) > 2)
                return dominantGap;
            if ((second.UniqueCounts / (float)first.UniqueCounts) <= 0.5f || (third.UniqueCounts / (float)first.UniqueCounts) <= 0.5f)
                return dominantGap;
            return SelectMedianGap(first.Gap, second.Gap, third.Gap);
        }

        private static int SelectMedianGap(int first, int second, int third)
        {
            if ((first > second && second > third) || (third > second && second > first))
                return second;
            if ((second > first && first > third) || (third > first && first > second))
                return first;
            return third;
        }

        private static void MarkValidatedLines(
            List<LegacyVerticalLine> verticalLines,
            List<LegacyHorizontalLine> horizontalLines,
            int verticalGap,
            int horizontalGap)
        {
            MarkValidatedVerticalLines(verticalLines, verticalGap);
            MarkValidatedHorizontalLines(horizontalLines, horizontalGap);
        }

        private static void MarkValidatedVerticalLines(List<LegacyVerticalLine> verticalLines, int verticalGap)
        {
            for (int firstIndex = 0; firstIndex < verticalLines.Count; firstIndex++)
            {
                LegacyVerticalLine first = verticalLines[firstIndex];
                for (int secondIndex = firstIndex + 1; secondIndex < verticalLines.Count; secondIndex++)
                {
                    LegacyVerticalLine second = verticalLines[secondIndex];
                    if (Math.Abs(second.Position - first.Position - verticalGap) <= Math.Max(1, verticalGap / ValidationGapDivisor))
                    {
                        first.Validate = true;
                        second.Validate = true;
                    }
                }
            }
        }

        private static void MarkValidatedHorizontalLines(List<LegacyHorizontalLine> horizontalLines, int horizontalGap)
        {
            for (int firstIndex = 0; firstIndex < horizontalLines.Count; firstIndex++)
            {
                LegacyHorizontalLine first = horizontalLines[firstIndex];
                for (int secondIndex = firstIndex + 1; secondIndex < horizontalLines.Count; secondIndex++)
                {
                    LegacyHorizontalLine second = horizontalLines[secondIndex];
                    if (Math.Abs(second.Position - first.Position - horizontalGap) <= Math.Max(1, horizontalGap / ValidationGapDivisor))
                    {
                        first.Validate = true;
                        second.Validate = true;
                    }
                }
            }
        }

        private static List<LegacyWholeOffset> BuildVerticalOffsets(List<LegacyVerticalLine> verticalLines, int boardPixelWidth)
        {
            List<LegacyWholeOffset> offsets = new List<LegacyWholeOffset>();
            for (int startIndex = 0; startIndex < verticalLines.Count; startIndex++)
            {
                LegacyVerticalLine start = verticalLines[startIndex];
                for (int endIndex = startIndex + 1; endIndex < verticalLines.Count; endIndex++)
                {
                    LegacyVerticalLine end = verticalLines[endIndex];
                    offsets.Add(new LegacyWholeOffset(start.Position, end.Position, Math.Abs(end.Position - start.Position - boardPixelWidth)));
                }
            }

            offsets.Sort((left, right) => left.Offset.CompareTo(right.Offset));
            EnsureOffsets(offsets, "vertical");
            return offsets;
        }

        private static List<LegacyWholeOffset> BuildHorizontalOffsets(List<LegacyHorizontalLine> horizontalLines, int boardPixelHeight)
        {
            List<LegacyWholeOffset> offsets = new List<LegacyWholeOffset>();
            for (int startIndex = 0; startIndex < horizontalLines.Count; startIndex++)
            {
                LegacyHorizontalLine start = horizontalLines[startIndex];
                for (int endIndex = startIndex + 1; endIndex < horizontalLines.Count; endIndex++)
                {
                    LegacyHorizontalLine end = horizontalLines[endIndex];
                    offsets.Add(new LegacyWholeOffset(start.Position, end.Position, Math.Abs(end.Position - start.Position - boardPixelHeight)));
                }
            }

            offsets.Sort((left, right) => left.Offset.CompareTo(right.Offset));
            EnsureOffsets(offsets, "horizontal");
            return offsets;
        }

        private static void TrimVerticalOffsets(List<LegacyWholeOffset> offsets, List<LegacyHorizontalLine> horizontalLines, int verticalGap)
        {
            while (CanTrimOffsets(offsets, verticalGap))
            {
                int firstScore = CountHorizontalOffsetSupport(offsets[0], horizontalLines, verticalGap);
                int secondScore = CountHorizontalOffsetSupport(offsets[1], horizontalLines, verticalGap);
                offsets.RemoveAt(firstScore < secondScore ? 0 : 1);
            }

            EnsureOffsets(offsets, "vertical");
        }

        private static int CountHorizontalOffsetSupport(LegacyWholeOffset offset, List<LegacyHorizontalLine> horizontalLines, int verticalGap)
        {
            int minPosition = offset.MinPos + verticalGap / OffsetPaddingDivisor;
            int maxPosition = offset.MaxPos - verticalGap / OffsetPaddingDivisor;
            int minSupport = CountHorizontalCrossings(horizontalLines, minPosition);
            int maxSupport = CountHorizontalCrossings(horizontalLines, maxPosition);
            return Math.Min(minSupport, maxSupport);
        }

        private static int CountHorizontalCrossings(List<LegacyHorizontalLine> horizontalLines, int x)
        {
            int count = 0;
            foreach (LegacyHorizontalLine line in horizontalLines)
            {
                if (line.Validate && line.StartPoint.X < x && line.EndPoint.X > x)
                    count++;
            }
            return count;
        }

        private static void TrimHorizontalOffsets(List<LegacyWholeOffset> offsets, List<LegacyVerticalLine> verticalLines, int horizontalGap)
        {
            while (CanTrimOffsets(offsets, horizontalGap))
            {
                int firstScore = CountVerticalOffsetSupport(offsets[0], verticalLines, horizontalGap);
                int secondScore = CountLegacyVerticalOffsetSupport(offsets[1], verticalLines, horizontalGap, offsets[0]);
                offsets.RemoveAt(firstScore < secondScore ? 0 : 1);
            }

            EnsureOffsets(offsets, "horizontal");
        }

        private static int CountVerticalOffsetSupport(LegacyWholeOffset offset, List<LegacyVerticalLine> verticalLines, int horizontalGap)
        {
            int minPosition = offset.MinPos + horizontalGap / OffsetPaddingDivisor;
            int maxPosition = offset.MaxPos - horizontalGap / OffsetPaddingDivisor;
            int minSupport = CountVerticalCrossings(verticalLines, minPosition);
            int maxSupport = CountVerticalCrossings(verticalLines, maxPosition);
            return Math.Min(minSupport, maxSupport);
        }

        private static int CountLegacyVerticalOffsetSupport(
            LegacyWholeOffset offset,
            List<LegacyVerticalLine> verticalLines,
            int horizontalGap,
            LegacyWholeOffset firstOffset)
        {
            int minPosition = offset.MinPos + horizontalGap / OffsetPaddingDivisor;
            int maxPosition = offset.MaxPos - horizontalGap / OffsetPaddingDivisor;
            int minSupport = CountVerticalCrossings(verticalLines, minPosition);
            int maxSupport = CountVerticalCrossings(verticalLines, maxPosition, firstOffset.MaxPos - horizontalGap / OffsetPaddingDivisor);
            return Math.Min(minSupport, maxSupport);
        }

        private static int CountVerticalCrossings(List<LegacyVerticalLine> verticalLines, int y)
        {
            return CountVerticalCrossings(verticalLines, y, y);
        }

        private static int CountVerticalCrossings(List<LegacyVerticalLine> verticalLines, int y, int upperBoundExclusive)
        {
            int count = 0;
            foreach (LegacyVerticalLine line in verticalLines)
            {
                if (!line.Validate)
                    continue;
                if (line.StartPoint.Y < y && line.EndPoint.Y > y && line.StartPoint.Y < upperBoundExclusive)
                    count++;
            }
            return count;
        }

        private static bool CanTrimOffsets(List<LegacyWholeOffset> offsets, int gap)
        {
            return offsets.Count >= 2 && Math.Abs(offsets[0].Offset - offsets[1].Offset) <= gap / OffsetGapDivisor;
        }

        private static void EnsureOffsets(List<LegacyWholeOffset> offsets, string axis)
        {
            if (offsets == null || offsets.Count == 0)
                throw new InvalidOperationException("Unable to resolve " + axis + " board offsets.");
        }

        private static Rectangle BuildAdjustedBounds(
            Rectangle selectionBounds,
            LegacyWholeOffset verticalOffset,
            LegacyWholeOffset horizontalOffset,
            int verticalGap,
            int horizontalGap)
        {
            int verticalPadding = (int)Math.Ceiling(verticalGap / HalfGapDivisor);
            int horizontalPadding = (int)Math.Ceiling(horizontalGap / HalfGapDivisor);
            int left = selectionBounds.Left + verticalOffset.MinPos - horizontalPadding;
            int top = selectionBounds.Top + horizontalOffset.MinPos - verticalPadding;
            int right = selectionBounds.Left + verticalOffset.MaxPos + horizontalPadding;
            int bottom = selectionBounds.Top + horizontalOffset.MaxPos + verticalPadding;
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
    }

    internal sealed class LegacyDetectedLines
    {
        public LegacyDetectedLines()
        {
            VerticalLines = new List<LegacyVerticalLine>();
            HorizontalLines = new List<LegacyHorizontalLine>();
        }

        public List<LegacyVerticalLine> VerticalLines { get; private set; }

        public List<LegacyHorizontalLine> HorizontalLines { get; private set; }

        public int TotalCount
        {
            get { return VerticalLines.Count + HorizontalLines.Count; }
        }
    }

    internal abstract class LegacyLine
    {
        protected LegacyLine(int position)
        {
            Position = position;
        }

        public int Position { get; set; }

        public bool NeedDelete { get; set; }

        public bool Validate { get; set; }
    }

    internal sealed class LegacyVerticalLine : LegacyLine
    {
        public LegacyVerticalLine(int position, int startY, int endY)
            : base(position)
        {
            StartPoint = new CvPoint(position, startY);
            EndPoint = new CvPoint(position, endY);
        }

        public CvPoint StartPoint { get; set; }

        public CvPoint EndPoint { get; set; }
    }

    internal sealed class LegacyHorizontalLine : LegacyLine
    {
        public LegacyHorizontalLine(int position, int startX, int endX)
            : base(position)
        {
            StartPoint = new CvPoint(startX, position);
            EndPoint = new CvPoint(endX, position);
        }

        public CvPoint StartPoint { get; set; }

        public CvPoint EndPoint { get; set; }
    }

    internal sealed class LegacyGapCount
    {
        public LegacyGapCount(int gap)
        {
            Gap = gap;
            LooseCounts = 1;
            UniqueCounts = 1;
        }

        public int Gap { get; private set; }

        public int LooseCounts { get; set; }

        public int UniqueCounts { get; set; }
    }

    internal sealed class LegacyWholeOffset
    {
        public LegacyWholeOffset(int minPos, int maxPos, int offset)
        {
            MinPos = minPos;
            MaxPos = maxPos;
            Offset = offset;
        }

        public int MinPos { get; private set; }

        public int MaxPos { get; private set; }

        public int Offset { get; private set; }
    }
}
