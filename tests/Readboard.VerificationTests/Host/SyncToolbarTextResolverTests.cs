using Xunit;
using readboard;

namespace Readboard.VerificationTests.Host
{
    public sealed class SyncToolbarTextResolverTests
    {
        [Fact]
        public void ResolveFastSyncTextAfterContinuousStop_KeepsStopLabelWhileKeepSyncRemainsRunning()
        {
            string resolved = SyncToolbarTextResolver.ResolveFastSyncTextAfterContinuousStop(
                keepSyncActive: true,
                stopSyncText: "StopSync",
                fastSyncText: "FastSync");

            Assert.Equal("StopSync", resolved);
        }

        [Fact]
        public void ShouldRestoreIdleUiAfterKeepSyncStop_StaysLockedWhileContinuousSyncRemainsRunning()
        {
            Assert.False(SyncToolbarTextResolver.ShouldRestoreIdleUiAfterKeepSyncStop(true));
        }
    }
}
