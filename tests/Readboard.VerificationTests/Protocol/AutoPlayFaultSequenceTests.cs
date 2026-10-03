using System;
using readboard;
using Xunit;
using Xunit.Abstractions;

namespace Readboard.VerificationTests.Protocol
{
    public sealed partial class AutoPlayFaultSequenceTests
    {
        private readonly ITestOutputHelper output;
        public AutoPlayFaultSequenceTests(ITestOutputHelper output) { this.output = output; }

        private void RunSequence(Action<AutoPlayFaultSequenceHarness> sequence)
        {
            var h = new AutoPlayFaultSequenceHarness();
            try { sequence(h); }
            finally
            {
                try { h.Dispose(); }
                finally { output.WriteLine(h.Transcript); }
            }
        }

        [Fact]
        public void DirectRequest_UnknownAndKeepSyncJitterSuppressPlayWithoutEndingCycle_ThenRecover()
        {
            RunSequence(h =>
            {
                h.EnableAndStart();
                h.Samples.Release(1);
                h.Samples.Wait(2);
                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);
                long generation = h.Runtime.CaptureSessionObservationGeneration();

                h.AtUi("unknown external players", () =>
                {
                    h.Environment.BindingInvalidated = true;
                    h.Environment.Players = FoxMatchBarReading.Empty;
                    h.Runtime.RequestAutoPlay();
                });
                Assert.Null(h.Runtime.Snapshot.PlayColor);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);

                h.Coordinator.EndKeepSync();
                h.AtUi("direct request while keep sync false", () => h.Runtime.RequestAutoPlay());
                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);
                h.Coordinator.BeginKeepSync();
                h.AtUi("recover current room evidence", () =>
                {
                    h.Environment.UtcNow = h.Environment.UtcNow.AddMilliseconds(1000);
                    h.Environment.Players = AutoPlayFaultSequenceHarness.Players("black");
                    h.Runtime.RequestAutoPlay();
                });
                h.Samples.Release(2);
                h.Samples.Wait(3);

                Assert.Equal(new[] { "play>black>0 0 0", "play>black>0 0 0" }, h.AutoPlayWire);
                Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Equal(generation, h.Runtime.CaptureSessionObservationGeneration());
            });
        }

        [Fact]
        public void PeriodicSample_UnknownRevokesColorOnce_AndFreshEvidenceRearmsSameCycle()
        {
            RunSequence(h =>
            {
                h.EnableAndStart();
                h.Samples.Release(1);
                h.Samples.Wait(2);
                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);
                long generation = h.Runtime.CaptureSessionObservationGeneration();
                h.AtUi("invalidate evidence without disabling", () =>
                {
                    h.Environment.BindingInvalidated = true;
                    h.Environment.Players = FoxMatchBarReading.Empty;
                    h.Runtime.RequestAutoPlay();
                });
                // Direct request does not revoke; the next genuinely unknown periodic sample does.
                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);
                h.Samples.Release(2);
                h.Samples.Wait(3);
                h.Samples.Release(3);
                h.Samples.Wait(4);
                Assert.Equal(new[] { "play>black>0 0 0", "stopAutoPlay" }, h.AutoPlayWire);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Null(h.Runtime.Snapshot.PlayColor);

                h.AtUi("fresh current-room players", () =>
                {
                    h.Environment.UtcNow = h.Environment.UtcNow.AddMilliseconds(1000);
                    h.Environment.Players = AutoPlayFaultSequenceHarness.Players("black");
                });
                h.Samples.Release(4);
                h.Samples.Wait(5);
                h.Samples.Release(5);
                h.Samples.Wait(6);

                Assert.Equal(new[] { "play>black>0 0 0", "stopAutoPlay", "play>black>0 0 0" }, h.AutoPlayWire);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
                Assert.Equal(generation, h.Runtime.CaptureSessionObservationGeneration());
            });
        }
    }
}
