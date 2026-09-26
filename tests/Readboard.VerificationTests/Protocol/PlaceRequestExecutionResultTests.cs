using System.Threading.Tasks;
using Xunit;
using Readboard.VerificationTests.Support;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class PlaceRequestExecutionResultTests
    {
        [Fact]
        public void HandlePlaceRequest_NullRequestReturnsNoResponse()
        {
            using PendingMoveRequestHarness harness = new PendingMoveRequestHarness();
            Assert.False(harness.Coordinator.HandlePlaceRequest(null).ShouldSendResponse);
            Assert.Equal(0, harness.PlacementCount);
        }

        [Fact]
        public void HandlePlaceRequest_WithoutActiveSyncReturnsNoResponse()
        {
            using PendingMoveRequestHarness harness = new PendingMoveRequestHarness();
            PlaceRequestExecutionResult result = harness.Coordinator.HandlePlaceRequest(
                new MoveRequest { X = 1, Y = 1, VerifyMove = false });
            Assert.False(result.ShouldSendResponse);
            Assert.Equal(0, harness.PlacementCount);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task HandlePlaceRequest_UnverifiedMoveReturnsPhysicalOutcome(bool success)
        {
            using PendingMoveRequestHarness harness = new PendingMoveRequestHarness();
            harness.PlacementSuccess = success;
            harness.Start();
            PlaceRequestExecutionResult result = await VerificationCompletion.WaitAsync(
                harness.Request(verify: false, maxAttempts: 5), "Physical placement did not complete.");
            Assert.True(result.ShouldSendResponse);
            Assert.Equal(success, result.Success);
            Assert.Equal(1, harness.PlacementCount);
            Assert.Equal(1, harness.LastMove.X);
            Assert.Equal(1, harness.LastMove.Y);
        }

        [Fact]
        public async Task HandlePlaceRequest_DelayedVisibleSnapshotConfirmsWithoutAnotherClick()
        {
            using PendingMoveRequestHarness harness = new PendingMoveRequestHarness();
            harness.Start();
            Task<PlaceRequestExecutionResult> request = harness.Request(maxAttempts: 2);
            harness.WaitForObservation();
            Assert.False(request.IsCompleted);
            harness.Observe(occupied: false);
            harness.WaitForObservation();
            Assert.False(request.IsCompleted);
            Assert.Equal(1, harness.PlacementCount);

            harness.AdvanceMilliseconds(250);
            harness.Observe(occupied: true);
            PlaceRequestExecutionResult result = await VerificationCompletion.WaitAsync(
                request, "Visible stone did not complete the request.");
            Assert.True(result.ShouldSendResponse);
            Assert.True(result.Success);
            Assert.Equal(1, harness.PlacementCount);
        }

        [Fact]
        public async Task HandlePlaceRequest_UnconfirmedObservationEnablesNextPhysicalAttempt()
        {
            using PendingMoveRequestHarness harness = new PendingMoveRequestHarness();
            harness.Start();
            Task<PlaceRequestExecutionResult> request = harness.Request(maxAttempts: 2);
            harness.WaitForObservation();
            harness.AdvanceMilliseconds(500);
            harness.Observe(occupied: false);
            harness.WaitForObservation();
            Assert.False(request.IsCompleted);
            Assert.Equal(2, harness.PlacementCount);

            harness.Observe(occupied: true);
            PlaceRequestExecutionResult result = await VerificationCompletion.WaitAsync(
                request, "Second attempt was not confirmed.");
            Assert.True(result.ShouldSendResponse);
            Assert.True(result.Success);
            Assert.Equal(2, harness.PlacementCount);
        }

        [Fact]
        public async Task HandlePlaceRequest_InvalidSnapshotsDoNotRetryAndWaiterTimesOut()
        {
            using PendingMoveRequestHarness harness = new PendingMoveRequestHarness();
            harness.Start();
            Task<PlaceRequestExecutionResult> request = harness.Request(maxAttempts: 10);
            harness.WaitForObservation();
            harness.AdvanceMilliseconds(500);
            harness.Observe(occupied: false, valid: false);
            harness.WaitForObservation();
            Assert.False(request.IsCompleted);
            Assert.Equal(1, harness.PlacementCount);

            harness.AdvanceMilliseconds(1500);
            // Leave capture blocked: the request waiter must enforce the total deadline itself.
            PlaceRequestExecutionResult result = await VerificationCompletion.WaitAsync(
                request, "Missing snapshots left the request waiting after its deadline.");
            Assert.True(result.ShouldSendResponse);
            Assert.False(result.Success);
            Assert.Equal(1, harness.PlacementCount);
        }

        [Fact]
        public async Task HandlePlaceRequest_StopWhileAwaitingObservationRetiresWithoutFailureResponse()
        {
            using PendingMoveRequestHarness harness = new PendingMoveRequestHarness();
            harness.Start();
            Task<PlaceRequestExecutionResult> request = harness.Request();
            harness.WaitForObservation();

            // Capture remains blocked, so the worker cannot emit its deferred stopsync yet.
            harness.Coordinator.StopSyncSession();

            PlaceRequestExecutionResult result = await VerificationCompletion.WaitAsync(
                request, "Stop did not retire the pending confirmation.");
            Assert.False(result.ShouldSendResponse);
            Assert.Equal(1, harness.PlacementCount);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task HandlePlaceRequest_StopDuringPlacementWaitsForPhysicalOutcome(bool success)
        {
            using PendingMoveRequestHarness harness = new PendingMoveRequestHarness();
            harness.PlacementSuccess = success;
            harness.BlockPlacement();
            harness.Start();
            Task<PlaceRequestExecutionResult> request = harness.Request();
            harness.WaitForPlacement();
            harness.Coordinator.Stop();
            Assert.False(request.IsCompleted);
            harness.ReleasePlacement();

            PlaceRequestExecutionResult result = await VerificationCompletion.WaitAsync(
                request, "Stopped request did not receive its in-flight placement outcome.");
            Assert.True(result.ShouldSendResponse);
            Assert.Equal(success, result.Success);
            Assert.Equal(1, harness.PlacementCount);
        }
    }
}
