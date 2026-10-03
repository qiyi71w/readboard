using System;
using System.Linq;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Protocol
{
    public sealed partial class AutoPlayFaultSequenceTests
    {
        [Fact]
        public void TargetSwitch_RetiresOldRecognitionAndRejectsQueuedObservations_ThenAuthorizesCurrentRoom()
        {
            RunSequence(h =>
            {
                h.EnableAndStart();
                h.DeferBoardObservations();
                h.Samples.Release(1);
                h.Samples.Wait(2);
                Assert.Equal("room-1", h.Runtime.Snapshot.FoxWindowContext.RoomToken);
                Assert.Equal("black", h.Runtime.Snapshot.PlayColor);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);
                Assert.Contains("re=step-1", h.Wire);
                long oldGeneration = h.Runtime.CaptureSessionObservationGeneration();

                // Step 2 still holds the old room's actual recognition result.
                h.AtUi("invalidate old target and room evidence", () =>
                {
                    h.Environment.Handle = IntPtr.Zero;
                    h.Environment.TargetWindowValid = false;
                    h.Environment.BindingInvalidated = true;
                    h.Environment.Players = FoxMatchBarReading.Empty;
                    h.Runtime.RefreshAutoPlayColor(out _);
                });
                Assert.Null(h.Runtime.Snapshot.PlayColor);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);

                h.Coordinator.StopSyncSession();
                h.Samples.Release(2);
                // Flag-idle is not retirement: platform changes require the real host callback.
                h.WaitForKeepSyncStopped();
                Assert.False(h.Environment.HasActiveSyncOperation);
                Assert.DoesNotContain("re=step-2", h.Wire);
                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);

                h.AtUi("switch to foreground platform", () =>
                {
                    Assert.Equal(ControlCenterApplyOutcome.Changed,
                        h.Runtime.Apply(ControlCenterIntent.SetPlatform(SyncMode.Foreground)).Outcome);
                });
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Null(h.Runtime.Snapshot.PlayColor);

                h.AtUi("bind new Fox target and current white players", () =>
                {
                    h.Environment.Handle = new IntPtr(84);
                    h.Environment.TargetWindowValid = true;
                    h.Environment.BindingInvalidated = true;
                    h.Environment.Context = new FoxWindowContext
                    {
                        Kind = FoxWindowKind.LiveRoom,
                        LiveRoomState = FoxLiveRoomState.Playing,
                        RoomToken = "room-2"
                    };
                    h.Environment.Players = AutoPlayFaultSequenceHarness.Players("white");
                    Assert.Equal(ControlCenterApplyOutcome.Changed,
                        h.Runtime.Apply(ControlCenterIntent.SetPlatform(SyncMode.Fox)).Outcome);
                    h.Runtime.RefreshAutoPlayColor(out _);
                });
                long newGeneration = h.Runtime.CaptureSessionObservationGeneration();
                Assert.True(newGeneration > oldGeneration);
                Assert.Equal("room-2", h.Runtime.Snapshot.FoxWindowContext.RoomToken);
                Assert.Equal("white", h.Runtime.Snapshot.PlayColor);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.False(h.Runtime.Snapshot.BoardRegionRecognized);

                // Deliver accepted old sample callbacks only after the new room and
                // generation are established, before generating new callbacks.
                h.DeliverDeferredObservations();
                Assert.Contains(ControlCenterSessionObservationApplyOutcome.Stale, h.DeferredOutcomes);
                Assert.All(h.DeferredOutcomes,
                    outcome => Assert.Equal(ControlCenterSessionObservationApplyOutcome.Stale, outcome));
                Assert.Equal(newGeneration, h.Runtime.CaptureSessionObservationGeneration());
                Assert.False(h.Runtime.Snapshot.BoardRegionRecognized);
                Assert.Equal("room-2", h.Runtime.Snapshot.FoxWindowContext.RoomToken);
                Assert.Equal("white", h.Runtime.Snapshot.PlayColor);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
                Assert.Equal(new[] { "play>black>0 0 0" }, h.AutoPlayWire);

                Assert.True(h.StartKeepSync());
                h.Samples.Wait(3);
                h.Samples.Release(3);
                h.Samples.Wait(4);
                Assert.True(h.Runtime.Snapshot.BoardRegionRecognized);
                Assert.Equal("room-2", h.Runtime.Snapshot.FoxWindowContext.RoomToken);
                Assert.Equal("white", h.Runtime.Snapshot.PlayColor);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);

                h.Coordinator.StopSyncSession();
                h.Samples.Release(4);
                h.WaitForKeepSyncStopped();
                Assert.Equal(new[] { "play>black>0 0 0", "play>white>0 0 0" }, h.AutoPlayWire);
                Assert.Equal(new[] { "re=step-1", "re=step-3" },
                    h.Wire.Where(line => line.StartsWith("re=", StringComparison.Ordinal)));
                Assert.Equal(new[] { "roomToken room-1", "roomToken room-2" },
                    h.Wire.Where(line => line.StartsWith("roomToken ", StringComparison.Ordinal)));
                Assert.Equal(new[]
                {
                    "play>black>0 0 0", "stopsync", "play>white>0 0 0", "stopsync"
                }, h.Wire.Where(line => line.StartsWith("play>", StringComparison.Ordinal)
                    || line == "stopAutoPlay" || line == "stopsync"));
                Assert.Equal("room-2", h.Runtime.Snapshot.FoxWindowContext.RoomToken);
                Assert.Equal("white", h.Runtime.Snapshot.PlayColor);
                Assert.True(h.Runtime.Snapshot.AutoPlayEnabled);
            });
        }
    }
}
