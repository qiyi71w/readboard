using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class IndependentUpdateJourneyTests
    {
        private static readonly HostedUpdateRequest Request = new HostedUpdateRequest("v3.1.6",
            "readboard-webview2-v3.1.6.zip", "https://example.test/package.zip", new string('a', 64));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CancelledDownloadCannotCommitOrReopenDialog(bool failsLate)
        {
            var download = new Download();
            var commit = new Commit();
            var observations = new List<IndependentUpdateObservation>();
            using var journey = new IndependentUpdateJourney(download, new FakeVerifier(), commit, () => { }, observations.Add);
            Task run = journey.StartAsync(Request);
            Assert.True(journey.Cancel());
            int count = observations.Count;
            if (failsLate) download.Completion.SetException(new IOException("late network error"));
            else download.Completion.SetResult("package.zip");
            await run;
            Assert.Equal(count, observations.Count);
            Assert.Equal(IndependentUpdateStage.Cancelled, observations.Last().Stage);
            Assert.False(commit.Accepted);
            Assert.True(journey.CanStart);
        }

        [Fact]
        public async Task ConfigurationSaveFailureLeavesOriginalApplicationUsable()
        {
            var download = new Download();
            download.Completion.SetResult("package.zip");
            var commit = new Commit();
            var observations = new List<IndependentUpdateObservation>();
            using var journey = new IndependentUpdateJourney(download, new FakeVerifier(), commit,
                () => throw new IOException("disk denied"), observations.Add);
            await journey.StartAsync(Request);
            Assert.False(commit.Accepted);
            Assert.Equal(IndependentUpdateStage.Failed, observations.Last().Stage);
            Assert.True(journey.CanStart);
            Assert.True(download.Cleaned);
        }

        [Fact]
        public async Task PersistedAcceptanceSurvivesDisposeAndCannotBeCancelledOrRepeated()
        {
            var download = new Download();
            download.Completion.SetResult("package.zip");
            var commit = new Commit();
            bool saved = false;
            var observations = new List<IndependentUpdateObservation>();
            var journey = new IndependentUpdateJourney(download, new FakeVerifier(), commit, () => saved = true, observations.Add);
            await journey.StartAsync(Request);
            Assert.True(saved);
            Assert.True(commit.Accepted);
            Assert.False(journey.Cancel());
            await journey.StartAsync(Request);
            journey.Dispose();
            Assert.False(download.Cleaned);
            Assert.Single(observations, item => item.Stage == IndependentUpdateStage.Committed);
        }

        [Fact]
        public async Task CancellationDuringVerificationCannotCommit()
        {
            var download = new Download();
            download.Completion.SetResult("package.zip");
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var verifier = new FakeVerifier { Work = () => { entered.Set(); release.Wait(); } };
            var commit = new Commit();
            using var journey = new IndependentUpdateJourney(download, verifier, commit, () => { }, _ => { });
            Task run = journey.StartAsync(Request);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(journey.Cancel());
            release.Set();
            await run;
            Assert.False(commit.Accepted);
            Assert.True(download.Cleaned);
        }

        [Fact]
        public async Task RepeatedClickDoesNotStartSecondDownload()
        {
            var download = new Download();
            using var journey = new IndependentUpdateJourney(download, new FakeVerifier(), new Commit(), () => { }, _ => { });
            Task first = journey.StartAsync(Request);
            await journey.StartAsync(Request);
            Assert.Equal(1, download.Starts);
            journey.Cancel();
            download.Completion.SetResult("package.zip");
            await first;
        }

        [Fact]
        public async Task MissingRunnerFailsBeforeDownloadAndReleasesPreparation()
        {
            var download = new Download();
            var commit = new Commit { PrepareFailure = new FileNotFoundException("runner missing") };
            var observations = new List<IndependentUpdateObservation>();
            using var journey = new IndependentUpdateJourney(download, new FakeVerifier(), commit, () => { }, observations.Add);
            await journey.StartAsync(Request);
            Assert.Equal(0, download.Starts);
            Assert.False(commit.Accepted);
            Assert.True(commit.PreparationCleaned);
            Assert.True(journey.CanStart);
            Assert.Equal(IndependentUpdateStage.Failed, observations.Last().Stage);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CancellationDuringHandshakeWaitsForDurableDecision(bool accepted)
        {
            var download = new Download();
            download.Completion.SetResult("package.zip");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var decision = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var commit = new Commit { Work = async token =>
            {
                entered.SetResult();
                await decision.Task;
                if (!accepted) token.ThrowIfCancellationRequested();
            } };
            var observations = new List<IndependentUpdateObservation>();
            using var journey = new IndependentUpdateJourney(download, new FakeVerifier(), commit, () => { }, observations.Add);
            Task run = journey.StartAsync(Request);
            await entered.Task;
            Assert.True(journey.Cancel());
            Assert.DoesNotContain(observations, item => item.Stage == IndependentUpdateStage.Cancelled);
            Assert.False(journey.CanStart);
            decision.SetResult();
            await run;
            Assert.Equal(accepted ? IndependentUpdateStage.Committed : IndependentUpdateStage.Cancelled, observations.Last().Stage);
            Assert.Equal(!accepted, download.Cleaned);
        }

        private sealed class Download : IHostedUpdatePackageDownloader
        {
            internal readonly TaskCompletionSource<string> Completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal int Starts;
            internal bool Cleaned;
            public Task<string> DownloadAsync(HostedUpdateRequest request, CancellationToken token) { Starts++; return Completion.Task; }
            public void Cleanup(HostedUpdateRequest request, string path) { Cleaned = true; }
        }
        private sealed class FakeVerifier : IHostedUpdatePackageVerifier
        {
            internal Action Work;
            public void Verify(HostedUpdateRequest request, string path, CancellationToken token) { Work?.Invoke(); token.ThrowIfCancellationRequested(); }
        }
        private sealed class Commit : IIndependentUpdateCommitter
        {
            internal bool Accepted;
            internal Func<CancellationToken, Task> Work;
            internal Exception PrepareFailure;
            internal bool PreparationCleaned;
            public Task PrepareAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (PrepareFailure != null) throw PrepareFailure;
                return Task.CompletedTask;
            }
            public void CleanupPreparation() { PreparationCleaned = true; }
            public async Task CommitAsync(HostedUpdateRequest request, string path, Action persist, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                persist();
                if (Work != null) await Work(token);
                Accepted = true;
            }
        }
    }
}
