using Xunit;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class SustainedSyncPerformanceAcceptanceTests
    {
        [Fact]
        public void SustainedTwoHundredMillisecondSync_UsesCoordinatorOrchestrationWithoutBacklog()
        {
            SustainedSyncAcceptanceReport report = SustainedSyncAcceptanceHarness.MeasureDefaultAcceptance();

            Assert.True(report.MeetsAcceptance, report.DescribeFailure());
        }
    }
}
