using System;
using System.Collections.Generic;
using System.Drawing;

namespace readboard
{
    internal static class FoxPlayerRowLocator
    {
        private const double MinimumRowLightRatio = 0.36d;

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
    }
}
