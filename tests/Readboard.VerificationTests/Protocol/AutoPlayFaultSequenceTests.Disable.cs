using System;
using System.Linq;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Protocol
{
    public sealed partial class AutoPlayFaultSequenceTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Disable_WhileWorkerInFlight_RejectsLateCapturedSnapshotAndQueuedObservations(bool deliverBeforeRetirement)
        {
            RunSequence(h =>
            {
                h.EnableAndStart();
                h.Samples.Release(1);
                h.Samples.Wait(2);

                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Equal("black", h.Runtime.Snapshot.PlayColor);

                h.DeferBoardObservations();
                h.AtUi("disable automatic while worker in flight", () =>
                {
                    h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(false));
                });

                Assert.False(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Null(h.Runtime.Snapshot.PlayColor);
                Assert.Equal(new[] { "play>black>0 0 0", "stopAutoPlay" }, h.AutoPlayWire);

                h.Samples.Release(2);
                h.Samples.Wait(3);

                Assert.False(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Null(h.Runtime.Snapshot.PlayColor);
                Assert.Equal(new[] { "play>black>0 0 0", "stopAutoPlay" }, h.AutoPlayWire);
                Assert.Contains("re=step-2", h.Wire);

                if (deliverBeforeRetirement)
                {
                    h.DeliverDeferredObservations();
                    Assert.NotEmpty(h.DeferredOutcomes);
                    Assert.DoesNotContain(ControlCenterSessionObservationApplyOutcome.Stale, h.DeferredOutcomes);
                }
                else
                {
                    h.Coordinator.StopSyncSession();
                    h.Samples.ReleaseAll();
                    h.WaitForKeepSyncStopped();

                    h.DeliverDeferredObservations();
                    Assert.NotEmpty(h.DeferredOutcomes);
                    Assert.All(h.DeferredOutcomes, outcome => Assert.Equal(ControlCenterSessionObservationApplyOutcome.Stale, outcome));
                }

                Assert.False(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Null(h.Runtime.Snapshot.PlayColor);
                Assert.Equal(new[] { "play>black>0 0 0", "stopAutoPlay" }, h.AutoPlayWire);
            });
        }

        [Fact]
        public void Disable_WhileWorkerInFlight_LiveRecognitionDoesNotReauthorizeOrSendPlay()
        {
            RunSequence(h =>
            {
                h.EnableAndStart();
                h.Samples.Release(1);
                h.Samples.Wait(2);

                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Equal("black", h.Runtime.Snapshot.PlayColor);

                h.AtUi("disable automatic while worker in flight", () =>
                {
                    h.Runtime.Apply(ControlCenterIntent.SetAutoPlayEnabled(false));
                });

                Assert.False(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Null(h.Runtime.Snapshot.PlayColor);
                Assert.Equal(new[] { "play>black>0 0 0", "stopAutoPlay" }, h.AutoPlayWire);

                h.Samples.Release(2);
                h.Samples.Wait(3);

                Assert.False(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Null(h.Runtime.Snapshot.PlayColor);
                Assert.Equal(new[] { "play>black>0 0 0", "stopAutoPlay" }, h.AutoPlayWire);
                Assert.Contains("re=step-2", h.Wire);

                h.AtUi("direct request while disabled", () => h.Runtime.RequestAutoPlay());
                Assert.False(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Null(h.Runtime.Snapshot.PlayColor);
                Assert.Equal(new[] { "play>black>0 0 0", "stopAutoPlay" }, h.AutoPlayWire);
            });
        }
    }
}
