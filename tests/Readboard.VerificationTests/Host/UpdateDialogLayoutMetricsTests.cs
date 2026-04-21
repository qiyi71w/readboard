using Xunit;

namespace readboard
{
    public sealed class UpdateDialogLayoutMetricsTests
    {
        [Theory]
        [InlineData(12, 12, 26)]
        [InlineData(12, 15, 29)]
        [InlineData(18, 16, 32)]
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
    }
}
