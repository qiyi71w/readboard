using System;
using System.Diagnostics;

namespace readboard
{
    internal static class UpdateDialogLayoutMetrics
    {
        private const int MinimumContentHeight = 12;
        private const int VerticalMargin = 6;
        private const int ExtraPadding = 2;
        private const int RequiredInfoRowCount = 3;

        internal static int CalculateInfoRowHeight(int labelHeight, int valueHeight)
        {
            int contentHeight = Math.Max(labelHeight, valueHeight);
            int normalizedContentHeight = Math.Max(contentHeight, MinimumContentHeight);
            return normalizedContentHeight + VerticalMargin * 2 + ExtraPadding;
        }

        internal static void EnsureInfoRowCapacity(int rowStyleCount)
        {
            if (rowStyleCount < RequiredInfoRowCount)
            {
                throw new InvalidOperationException(
                    "infoPanel.RowStyles must contain at least 3 entries before applying update row layout.");
            }

            Debug.Assert(
                rowStyleCount >= RequiredInfoRowCount,
                "infoPanel.RowStyles should expose one entry per update metadata row.");
        }
    }
}
