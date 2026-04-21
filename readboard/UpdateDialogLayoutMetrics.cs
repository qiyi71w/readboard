using System;

namespace readboard
{
    internal static class UpdateDialogLayoutMetrics
    {
        private const int MinimumContentHeight = 12;
        private const int VerticalMargin = 6;
        private const int ExtraPadding = 2;

        internal static int CalculateInfoRowHeight(int labelHeight, int valueHeight)
        {
            int contentHeight = Math.Max(labelHeight, valueHeight);
            int normalizedContentHeight = Math.Max(contentHeight, MinimumContentHeight);
            return normalizedContentHeight + VerticalMargin * 2 + ExtraPadding;
        }
    }
}
