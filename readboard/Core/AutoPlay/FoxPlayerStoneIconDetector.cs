using System;
using System.Drawing;

namespace readboard
{
    internal static class FoxPlayerStoneIconDetector
    {
        private const double MinimumCenterStoneRatio = 0.45d;
        private const double MaximumOppositeCenterRatio = 0.25d;
        private const double MinimumGlossyBlackDarkRatio = 0.06d;
        private const double MinimumGlossyBlackCenterDarkRatio = 0.04d;
        private const double MaximumGlossyBlackLightRatio = 0.75d;
        private const double MaximumGlossyBlackAverageBrightness = 205d;
        private const double MaximumDarkRatioForWhite = 0.05d;
        private const double MaximumOuterLightRatioForWhite = 0.95d;

        public static AutoPlayColorResolution Detect(Bitmap iconBitmap)
        {
            if (iconBitmap == null || iconBitmap.Width <= 0 || iconBitmap.Height <= 0)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);

            int centerTotal = 0;
            int centerDark = 0;
            int centerLight = 0;
            int outerTotal = 0;
            int outerLight = 0;
            int total = 0;
            int totalDark = 0;
            int totalLight = 0;
            int totalBrightness = 0;
            double centerX = (iconBitmap.Width - 1) / 2d;
            double centerY = (iconBitmap.Height - 1) / 2d;
            double radiusX = Math.Max(1d, iconBitmap.Width * 0.34d);
            double radiusY = Math.Max(1d, iconBitmap.Height * 0.34d);

            for (int y = 0; y < iconBitmap.Height; y++)
            {
                for (int x = 0; x < iconBitmap.Width; x++)
                {
                    Color color = iconBitmap.GetPixel(x, y);
                    total++;
                    totalBrightness += (color.R + color.G + color.B) / 3;
                    bool dark = IsDark(color);
                    bool light = IsLight(color);
                    if (dark)
                        totalDark++;
                    else if (light)
                        totalLight++;

                    bool inCenter = IsInsideEllipse(x, y, centerX, centerY, radiusX, radiusY);
                    if (inCenter)
                    {
                        centerTotal++;
                        if (dark)
                            centerDark++;
                        else if (light)
                            centerLight++;
                    }
                    else
                    {
                        outerTotal++;
                        if (light)
                            outerLight++;
                    }
                }
            }

            if (centerTotal == 0)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);

            double centerDarkRatio = centerDark / (double)centerTotal;
            double centerLightRatio = centerLight / (double)centerTotal;
            double outerLightRatio = outerTotal == 0 ? 0d : outerLight / (double)outerTotal;
            double totalDarkRatio = totalDark / (double)total;
            double totalLightRatio = totalLight / (double)total;
            double averageBrightness = totalBrightness / (double)total;
            bool matteBlack = centerDarkRatio >= MinimumCenterStoneRatio
                && centerLightRatio <= MaximumOppositeCenterRatio;
            bool glossyBlack = totalDarkRatio >= MinimumGlossyBlackDarkRatio
                && centerDarkRatio >= MinimumGlossyBlackCenterDarkRatio
                && totalLightRatio <= MaximumGlossyBlackLightRatio
                && averageBrightness <= MaximumGlossyBlackAverageBrightness;
            bool black = matteBlack || glossyBlack;
            bool white = centerLightRatio >= MinimumCenterStoneRatio
                && centerDarkRatio <= MaximumOppositeCenterRatio
                && totalDarkRatio <= MaximumDarkRatioForWhite
                && outerLightRatio <= MaximumOuterLightRatioForWhite;

            if (black == white)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);
            return black
                ? AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack)
                : AutoPlayColorResolution.Known("white", AutoPlayColorStatus.RecognizedWhite);
        }

        private static bool IsDark(Color color)
        {
            return color.R <= 70 && color.G <= 70 && color.B <= 70;
        }

        private static bool IsLight(Color color)
        {
            return color.R >= 220 && color.G >= 220 && color.B >= 215;
        }

        private static bool IsInsideEllipse(int x, int y, double centerX, double centerY, double radiusX, double radiusY)
        {
            double dx = (x - centerX) / radiusX;
            double dy = (y - centerY) / radiusY;
            return (dx * dx) + (dy * dy) <= 1d;
        }
    }
}
