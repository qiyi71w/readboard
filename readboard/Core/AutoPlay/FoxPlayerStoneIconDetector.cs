using System;
using System.Drawing;

namespace readboard
{
    internal static class FoxPlayerStoneIconDetector
    {
        private const double MinimumCenterStoneRatio = 0.45d;
        private const double MaximumOppositeCenterRatio = 0.25d;
        private const double MaximumOuterLightRatioForWhite = 0.70d;

        public static AutoPlayColorResolution Detect(Bitmap iconBitmap)
        {
            if (iconBitmap == null || iconBitmap.Width <= 0 || iconBitmap.Height <= 0)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);

            int centerTotal = 0;
            int centerDark = 0;
            int centerLight = 0;
            int outerTotal = 0;
            int outerLight = 0;
            double centerX = (iconBitmap.Width - 1) / 2d;
            double centerY = (iconBitmap.Height - 1) / 2d;
            double radiusX = Math.Max(1d, iconBitmap.Width * 0.34d);
            double radiusY = Math.Max(1d, iconBitmap.Height * 0.34d);

            for (int y = 0; y < iconBitmap.Height; y++)
            {
                for (int x = 0; x < iconBitmap.Width; x++)
                {
                    Color color = iconBitmap.GetPixel(x, y);
                    bool inCenter = IsInsideEllipse(x, y, centerX, centerY, radiusX, radiusY);
                    if (inCenter)
                    {
                        centerTotal++;
                        if (IsDark(color))
                            centerDark++;
                        else if (IsLight(color))
                            centerLight++;
                    }
                    else
                    {
                        outerTotal++;
                        if (IsLight(color))
                            outerLight++;
                    }
                }
            }

            if (centerTotal == 0)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);

            double centerDarkRatio = centerDark / (double)centerTotal;
            double centerLightRatio = centerLight / (double)centerTotal;
            double outerLightRatio = outerTotal == 0 ? 0d : outerLight / (double)outerTotal;
            bool black = centerDarkRatio >= MinimumCenterStoneRatio
                && centerLightRatio <= MaximumOppositeCenterRatio;
            bool white = centerLightRatio >= MinimumCenterStoneRatio
                && centerDarkRatio <= MaximumOppositeCenterRatio
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
