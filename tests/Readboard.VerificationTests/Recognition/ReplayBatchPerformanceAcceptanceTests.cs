using Xunit;

namespace Readboard.VerificationTests.Recognition
{
    public sealed class ReplayBatchPerformanceAcceptanceTests
    {
        [Fact]
        public void ReplayBatch_CachedRecognitionMeetsLatencyAllocationAndAccuracyAcceptance()
        {
            RecognitionPerformanceAcceptanceReport report = RecognitionPerformanceAcceptanceHarness.MeasureDefaultAcceptance();

            Assert.True(report.Cached.CachedSnapshotCount > 0, "Acceptance replay must exercise cached snapshots.");
            Assert.True(report.MeetsAllocationAcceptance, report.DescribeAllocationFailure());
            Assert.True(report.MeetsAccuracyAcceptance, report.DescribeAccuracyFailure());
        }
    }
}
