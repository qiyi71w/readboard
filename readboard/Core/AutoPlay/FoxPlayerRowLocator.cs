using System;
using System.Collections.Generic;
using System.Drawing;

namespace readboard
{
    internal static class FoxPlayerRowLocator
    {
        private const double MinimumRowLightRatio = 0.36d;
        private const double MinimumPanelContentRatio = 0.02d;
        private const int PanelContentMergeGap = 8;
        private const int MinimumPanelRowHeight = 18;

        public static IList<FoxPlayerRowCandidate> Locate(Bitmap bitmap, SyncMode syncMode)
        {
            List<FoxPlayerRowCandidate> rows = new List<FoxPlayerRowCandidate>();
            if (!IsFoxMode(syncMode) || bitmap == null || bitmap.Width < 240 || bitmap.Height < 160)
                return rows;

            int scanLeft = bitmap.Width * 55 / 100;
            int scanRight = bitmap.Width * 97 / 100;
            int scanTop = bitmap.Height * 24 / 100;
            int scanBottom = bitmap.Height * 82 / 100;
            int minimumHeight = Math.Max(16, bitmap.Height / 40);
            int maximumHeight = Math.Max(minimumHeight + 1, bitmap.Height / 10);

            bool inRun = false;
            int runStart = 0;
            for (int y = scanTop; y < scanBottom; y++)
            {
                bool rowLike = GetLightRatio(bitmap, scanLeft, scanRight, y) >= MinimumRowLightRatio;
                if (rowLike && !inRun)
                {
                    inRun = true;
                    runStart = y;
                }
                else if (!rowLike && inRun)
                {
                    AddCandidateIfPlausible(bitmap, rows, scanLeft, scanRight, runStart, y - runStart, minimumHeight, maximumHeight);
                    inRun = false;
                }
            }

            if (inRun)
                AddCandidateIfPlausible(bitmap, rows, scanLeft, scanRight, runStart, scanBottom - runStart, minimumHeight, maximumHeight);

            return rows;
        }

        public static IList<FoxPlayerRowCandidate> LocatePlayerListPanel(Bitmap bitmap)
        {
            List<FoxPlayerRowCandidate> rows = new List<FoxPlayerRowCandidate>();
            if (bitmap == null || bitmap.Width < 200 || bitmap.Height < 80)
                return rows;

            int scanLeft = Math.Max(12, bitmap.Width * 8 / 100);
            int scanRight = Math.Min(bitmap.Width - 12, bitmap.Width * 67 / 100);
            int scanTop = Math.Max(18, Math.Min(bitmap.Height * 16 / 100, 28));
            int scanBottom = bitmap.Height;
            int minimumHeight = MinimumPanelRowHeight;
            int maximumHeight = Math.Max(minimumHeight + 1, Math.Min(72, bitmap.Height / 3));

            List<RowRun> runs = new List<RowRun>();
            bool inRun = false;
            int runStart = 0;
            for (int y = scanTop; y < scanBottom; y++)
            {
                bool rowLike = GetPanelContentRatio(bitmap, scanLeft, scanRight, y) >= MinimumPanelContentRatio;
                if (rowLike && !inRun)
                {
                    inRun = true;
                    runStart = y;
                }
                else if (!rowLike && inRun)
                {
                    runs.Add(new RowRun(runStart, y - 1));
                    inRun = false;
                }
            }

            if (inRun)
                runs.Add(new RowRun(runStart, scanBottom - 1));

            List<RowRun> mergedRuns = MergePanelRuns(runs);
            foreach (RowRun run in mergedRuns)
            {
                int expandedTop = Math.Max(0, run.Top - 7);
                int expandedBottom = Math.Min(bitmap.Height - 1, run.Bottom + 8);
                int height = expandedBottom - expandedTop + 1;
                if (height < minimumHeight || height > maximumHeight)
                    continue;

                PixelRect rowBounds = ResolvePanelRowBounds(bitmap, expandedTop, expandedBottom);
                if (rowBounds.IsEmpty || rowBounds.Width < bitmap.Width / 2)
                    continue;

                int rowHeight = rowBounds.Height;
                int nicknameLeft = rowBounds.X + Math.Max(28, rowBounds.Width / 10);
                int stoneLeft = Math.Min(rowBounds.X + rowBounds.Width - 28, rowBounds.X + rowBounds.Width * 40 / 100);
                PixelRect stoneBounds = ResolvePanelStoneBounds(bitmap, rowBounds, nicknameLeft, stoneLeft);
                int nicknameRight = stoneBounds.IsEmpty
                    ? rowBounds.X + Math.Min(rowBounds.Width * 35 / 100, rowBounds.Width - nicknameLeft - 12)
                    : Math.Max(nicknameLeft + 24, stoneBounds.X - 6);
                int nicknameWidth = Math.Min(rowBounds.X + rowBounds.Width - nicknameLeft - 12, nicknameRight - nicknameLeft);
                int nicknameTop = rowBounds.Y + Math.Max(2, rowHeight / 8);
                int nicknameHeight = Math.Max(10, rowHeight - Math.Max(4, rowHeight / 4));
                if (nicknameWidth <= 0)
                    continue;

                rows.Add(new FoxPlayerRowCandidate(
                    rowBounds,
                    new PixelRect(nicknameLeft, nicknameTop, nicknameWidth, Math.Min(nicknameHeight, rowBounds.Y + rowBounds.Height - nicknameTop)),
                    stoneBounds.IsEmpty
                        ? new PixelRect(stoneLeft, rowBounds.Y + Math.Max(3, rowHeight / 6), Math.Max(16, rowHeight - 4), Math.Max(16, rowHeight - 4))
                        : stoneBounds));
            }

            return rows;
        }

        private static bool IsFoxMode(SyncMode syncMode)
        {
            return syncMode == SyncMode.Fox || syncMode == SyncMode.FoxBackgroundPlace;
        }

        private static double GetLightRatio(Bitmap bitmap, int left, int right, int y)
        {
            int sampleCount = 0;
            int lightCount = 0;
            int step = Math.Max(1, (right - left) / 120);
            for (int x = left; x < right; x += step)
            {
                sampleCount++;
                if (IsPlayerRowBackground(bitmap.GetPixel(x, y)))
                    lightCount++;
            }

            return sampleCount == 0 ? 0d : lightCount / (double)sampleCount;
        }

        private static double GetPanelContentRatio(Bitmap bitmap, int left, int right, int y)
        {
            int sampleCount = 0;
            int contentCount = 0;
            int step = Math.Max(1, (right - left) / 100);
            for (int x = left; x < right; x += step)
            {
                sampleCount++;
                if (IsPanelContentPixel(bitmap.GetPixel(x, y)))
                    contentCount++;
            }

            return sampleCount == 0 ? 0d : contentCount / (double)sampleCount;
        }

        private static List<RowRun> MergePanelRuns(IList<RowRun> runs)
        {
            List<RowRun> merged = new List<RowRun>();
            if (runs == null || runs.Count == 0)
                return merged;

            RowRun current = runs[0];
            for (int i = 1; i < runs.Count; i++)
            {
                RowRun next = runs[i];
                if (next.Top - current.Bottom <= PanelContentMergeGap)
                {
                    current = new RowRun(current.Top, next.Bottom);
                    continue;
                }

                merged.Add(current);
                current = next;
            }

            merged.Add(current);
            return merged;
        }

        private static void AddCandidateIfPlausible(
            Bitmap bitmap,
            List<FoxPlayerRowCandidate> rows,
            int scanLeft,
            int scanRight,
            int y,
            int height,
            int minimumHeight,
            int maximumHeight)
        {
            if (height > maximumHeight)
            {
                SplitTallRun(bitmap, rows, scanLeft, scanRight, y, height, minimumHeight, maximumHeight);
                return;
            }

            AddSingleCandidateIfPlausible(bitmap, rows, scanLeft, scanRight, y, height, minimumHeight, maximumHeight);
        }

        private static void SplitTallRun(
            Bitmap bitmap,
            List<FoxPlayerRowCandidate> rows,
            int scanLeft,
            int scanRight,
            int y,
            int height,
            int minimumHeight,
            int maximumHeight)
        {
            int expectedHeight = Math.Max(minimumHeight, Math.Min(maximumHeight, bitmap.Height * 34 / 600));
            int rowCount = Math.Max(1, (int)Math.Round(height / (double)expectedHeight));
            if (rowCount > 8)
                return;

            for (int i = 0; i < rowCount; i++)
            {
                int rowTop = y + (height * i / rowCount);
                int rowBottom = y + (height * (i + 1) / rowCount);
                AddSingleCandidateIfPlausible(bitmap, rows, scanLeft, scanRight, rowTop, rowBottom - rowTop, minimumHeight, maximumHeight);
            }
        }

        private static void AddSingleCandidateIfPlausible(
            Bitmap bitmap,
            List<FoxPlayerRowCandidate> rows,
            int scanLeft,
            int scanRight,
            int y,
            int height,
            int minimumHeight,
            int maximumHeight)
        {
            if (height < minimumHeight || height > maximumHeight)
                return;

            PixelRect rowBounds = ResolveRowBounds(bitmap, scanLeft, scanRight, y, height);
            if (rowBounds.IsEmpty || rowBounds.Width < bitmap.Width / 5)
                return;

            int nicknameLeft = rowBounds.X + Math.Max(8, rowBounds.Height);
            int nicknameTop = rowBounds.Y + Math.Max(2, rowBounds.Height / 6);
            int nicknameWidth = Math.Min(rowBounds.Width * 44 / 100, rowBounds.Width - (nicknameLeft - rowBounds.X) - 8);
            int nicknameHeight = Math.Max(8, rowBounds.Height * 2 / 3);
            int iconSize = Math.Max(16, Math.Min(rowBounds.Height * 72 / 100, rowBounds.Width / 10));
            int iconLeft = Math.Min(rowBounds.X + rowBounds.Width * 63 / 100, rowBounds.X + rowBounds.Width - iconSize - 4);
            int iconTop = rowBounds.Y + (rowBounds.Height - iconSize) / 2;

            if (nicknameWidth <= 0)
                return;

            rows.Add(new FoxPlayerRowCandidate(
                rowBounds,
                new PixelRect(nicknameLeft, nicknameTop, nicknameWidth, Math.Min(nicknameHeight, rowBounds.Y + rowBounds.Height - nicknameTop)),
                new PixelRect(iconLeft, iconTop, iconSize, iconSize)));
        }

        private static PixelRect ResolvePanelRowBounds(Bitmap bitmap, int top, int bottom)
        {
            int midY = top + (bottom - top) / 2;
            int startX = Math.Max(0, bitmap.Width * 8 / 100);
            int endX = Math.Min(bitmap.Width - 1, bitmap.Width * 67 / 100);
            for (int x = startX; x <= endX; x++)
            {
                if (!IsPanelContentPixel(bitmap.GetPixel(x, midY)))
                    continue;
                return new PixelRect(0, top, bitmap.Width, bottom - top + 1);
            }

            return new PixelRect(0, 0, 0, 0);
        }

        private static PixelRect ResolvePanelStoneBounds(Bitmap bitmap, PixelRect rowBounds, int nicknameLeft, int stoneLeft)
        {
            int iconSize = Math.Max(16, Math.Min(rowBounds.Height - 2, bitmap.Width / 16));
            int scanLeft = Math.Max(nicknameLeft + 28, stoneLeft - 14);
            int scanRight = Math.Min(rowBounds.X + rowBounds.Width - iconSize, stoneLeft + 18);
            int scanTop = rowBounds.Y + Math.Max(2, (rowBounds.Height - iconSize) / 2);
            int scanBottom = Math.Min(rowBounds.Y + rowBounds.Height - iconSize, scanTop + 2);
            for (int y = scanTop; y <= scanBottom; y++)
            {
                for (int x = scanLeft; x <= scanRight; x++)
                {
                    PixelRect bounds = new PixelRect(x, y, iconSize, iconSize);
                    using (Bitmap icon = Crop(bitmap, bounds))
                    {
                        AutoPlayColorResolution resolution = FoxPlayerStoneIconDetector.Detect(icon);
                        if (resolution.IsKnown)
                            return bounds;
                    }
                }
            }

            return new PixelRect(stoneLeft, scanTop, iconSize, iconSize);
        }

        private static Bitmap Crop(Bitmap source, PixelRect bounds)
        {
            if (source == null || bounds == null || bounds.IsEmpty)
                return null;

            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, bounds.Width, bounds.Height),
                    new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    GraphicsUnit.Pixel);
            }
            return bitmap;
        }

        private static PixelRect ResolveRowBounds(Bitmap bitmap, int scanLeft, int scanRight, int y, int height)
        {
            int midY = y + height / 2;
            int left = -1;
            int right = -1;
            for (int x = scanLeft; x < scanRight; x++)
            {
                if (!IsPlayerRowBackground(bitmap.GetPixel(x, midY)))
                    continue;
                if (left < 0)
                    left = x;
                right = x;
            }

            if (left < 0 || right <= left)
                return new PixelRect(0, 0, 0, 0);

            return new PixelRect(left, y, right - left + 1, height);
        }

        private static bool IsPlayerRowBackground(Color color)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));
            return max >= 180 && max - min <= 34;
        }

        private static bool IsPanelContentPixel(Color color)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));
            int saturation = max - min;
            return max <= 150
                || (max <= 210 && saturation >= 18)
                || (saturation >= 45 && max >= 80);
        }

        private readonly struct RowRun
        {
            public RowRun(int top, int bottom)
            {
                Top = top;
                Bottom = bottom;
            }

            public int Top { get; }
            public int Bottom { get; }
        }
    }
}
