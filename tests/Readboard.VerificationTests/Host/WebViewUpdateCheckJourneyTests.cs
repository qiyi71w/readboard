using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class WebViewUpdateCheckJourneyTests
    {
        [Fact]
        public async Task CancelledCheck_IgnoresLateSuccessAndAllowsNextGeneration()
        {
            var observations = new List<WebViewUpdateCheckObservation>();
            var journey = new WebViewUpdateCheckJourney(observations.Add);
            var firstCompletion = new TaskCompletionSource<UpdateCheckResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task first = journey.StartAsync(
                token => firstCompletion.Task,
                result => false);
            Assert.Single(observations);
            Assert.Equal(WebViewUpdateCheckObservationKind.Started, observations[0].Kind);

            Assert.True(journey.Cancel());
            firstCompletion.SetResult(new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate });
            await first;
            Assert.Single(observations);

            var currentResult = new UpdateCheckResult { Status = UpdateCheckStatus.UpdateAvailable };
            await journey.StartAsync(
                token => Task.FromResult(currentResult),
                result => true);

            Assert.Equal(3, observations.Count);
            Assert.Equal(WebViewUpdateCheckObservationKind.Started, observations[1].Kind);
            Assert.Equal(WebViewUpdateCheckObservationKind.Completed, observations[2].Kind);
            Assert.Same(currentResult, observations[2].Result);
            Assert.True(observations[2].HostedInstallAvailable);
        }

        [Fact]
        public async Task CancelledCheck_IgnoresLateFailure()
        {
            var observations = new List<WebViewUpdateCheckObservation>();
            var journey = new WebViewUpdateCheckJourney(observations.Add);
            var completion = new TaskCompletionSource<UpdateCheckResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task check = journey.StartAsync(token => completion.Task, result => false);
            Assert.True(journey.Cancel());
            completion.SetException(new InvalidOperationException("late failure"));

            await check;

            Assert.Single(observations);
            Assert.Equal(WebViewUpdateCheckObservationKind.Started, observations[0].Kind);
        }

        [Fact]
        public async Task CancelledCheck_CancelsOwnedRequest()
        {
            var observations = new List<WebViewUpdateCheckObservation>();
            var journey = new WebViewUpdateCheckJourney(observations.Add);
            CancellationToken requestToken = default;

            Task check = journey.StartAsync(
                async token =>
                {
                    requestToken = token;
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return new UpdateCheckResult();
                },
                result => false);

            Assert.True(journey.Cancel());
            await check;

            Assert.True(requestToken.IsCancellationRequested);
            Assert.Single(observations);
        }

        [Fact]
        public async Task StartedObserverFailure_ReleasesJourneyOwnership()
        {
            int checkCount = 0;
            var journey = new WebViewUpdateCheckJourney(
                observation => throw new InvalidOperationException("observer failed"));

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => journey.StartAsync(
                    token =>
                    {
                        checkCount++;
                        return Task.FromResult(new UpdateCheckResult());
                    },
                    result => false));

            Assert.Equal("observer failed", failure.Message);
            Assert.Equal(0, checkCount);
            Assert.False(journey.IsRunning);
            Assert.False(journey.Cancel());
        }

        [Fact]
        public async Task CurrentCheck_ReportsFailureAndStopsRunning()
        {
            var observations = new List<WebViewUpdateCheckObservation>();
            var journey = new WebViewUpdateCheckJourney(observations.Add);
            var failure = new InvalidOperationException("checker failed");

            await journey.StartAsync(
                token => Task.FromException<UpdateCheckResult>(failure),
                result => false);

            Assert.Equal(2, observations.Count);
            Assert.Equal(WebViewUpdateCheckObservationKind.Failed, observations[1].Kind);
            Assert.Same(failure, observations[1].Exception);
            Assert.False(journey.IsRunning);
            Assert.False(journey.Cancel());
        }
    }
}
