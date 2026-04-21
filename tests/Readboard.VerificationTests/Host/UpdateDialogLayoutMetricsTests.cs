using Xunit;
using System;

namespace readboard
{
    public sealed class UpdateDialogLayoutMetricsTests
    {
        [Theory]
        [InlineData(0, 0, 26)]
        [InlineData(5, 10, 26)]
        [InlineData(12, 12, 26)]
        [InlineData(12, 15, 29)]
        [InlineData(18, 16, 32)]
        [InlineData(18, 40, 54)]
        public void CalculateInfoRowHeight_UsesTallestContentHeight(int labelHeight, int valueHeight, int expected)
        {
            int rowHeight = UpdateDialogLayoutMetrics.CalculateInfoRowHeight(labelHeight, valueHeight);

            Assert.Equal(expected, rowHeight);
        }

        [Fact]
        public void CalculateInfoRowHeight_ExpandsPastLegacyFixedRowHeight_WhenFontNeedsMoreSpace()
        {
            int rowHeight = UpdateDialogLayoutMetrics.CalculateInfoRowHeight(15, 15);

            Assert.True(rowHeight > 24);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void EnsureInfoRowCapacity_ThrowsWhenRowStylesAreMissing(int rowStyleCount)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => UpdateDialogLayoutMetrics.EnsureInfoRowCapacity(rowStyleCount));

            Assert.Contains("infoPanel.RowStyles", exception.Message);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        public void EnsureInfoRowCapacity_AllowsExpectedOrGreaterRowStyles(int rowStyleCount)
        {
            UpdateDialogLayoutMetrics.EnsureInfoRowCapacity(rowStyleCount);
        }
    }
}
