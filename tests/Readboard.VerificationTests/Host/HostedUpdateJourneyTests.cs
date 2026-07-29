using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class HostedUpdateJourneyTests
    {
        private static readonly HostedUpdateRequest Request = new HostedUpdateRequest(
            "v3.1.0",
            "readboard-webview2-v3.1.0.zip",
            "https://github.com/qiyi71w/readboard/releases/download/v3.1.0/readboard-webview2-v3.1.0.zip",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

        [Fact]
        public async Task CancelDuringDownload_RequestsCancellationCleansCandidateAndNeverHandsOff()
        {
            var downloader = new ControlledDownloader();
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                observations.Add);

            Task run = journey.StartAsync(Request);

            Assert.Equal(HostedUpdateStage.Downloading, observations[0].Stage);
            int downloadGeneration = observations[0].Generation;

            Assert.True(journey.Cancel());

            Assert.True(downloader.CancellationToken.IsCancellationRequested);
            Assert.Null(downloader.CleanedRequest);
            Assert.Null(downloader.CleanedPackagePath);
            Assert.Empty(host.ReadyMessages);
            Assert.Equal(HostedUpdateStage.Cancelled, observations[1].Stage);
            Assert.True(observations[1].Generation > downloadGeneration);

            downloader.Complete("candidate.zip");
            await run;

            Assert.Equal(2, observations.Count);
            Assert.Equal(Request.VersionTag, downloader.CleanedRequest.VersionTag);
            Assert.Equal(Request.AssetName, downloader.CleanedRequest.AssetName);
            Assert.Equal("candidate.zip", downloader.CleanedPackagePath);
            Assert.Empty(host.ReadyMessages);
            Assert.Equal("Update_downloadingPackage", observations[0].Message.Key);
            Assert.Equal("Update_cancelled", observations[1].Message.Key);
        }

        [Fact]
        public async Task CancelDuringVerification_InvalidatesLateSuccessAndCleansFinalPackage()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "candidate.zip" };
            var verifier = new BlockingVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                observations.Add);

            Task run = journey.StartAsync(Request);
            await verifier.Started;

            Assert.Equal(HostedUpdateStage.Verifying, observations[1].Stage);
            Assert.True(journey.Cancel());

            Assert.True(verifier.CancellationToken.IsCancellationRequested);
            Assert.Null(downloader.CleanedRequest);
            Assert.Null(downloader.CleanedPackagePath);
            Assert.Empty(host.ReadyMessages);

            verifier.Complete();
            await run;

            Assert.Equal(3, observations.Count);
            Assert.Equal(Request.VersionTag, downloader.CleanedRequest.VersionTag);
            Assert.Equal(Request.AssetName, downloader.CleanedRequest.AssetName);
            Assert.Equal("candidate.zip", downloader.CleanedPackagePath);
            Assert.Equal(HostedUpdateStage.Cancelled, observations[2].Stage);
            Assert.DoesNotContain(observations, observation => observation.Stage == HostedUpdateStage.NotifyingHost);
        }

        [Fact]
        public async Task LateDownloadFailureAfterCancel_IsIgnoredWithoutFailureSnapshot()
        {
            var downloader = new ControlledDownloader();
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                observations.Add);

            Task run = journey.StartAsync(Request);
            Assert.True(journey.Cancel());
            downloader.Fail(new InvalidOperationException("late failure"));
            await run;

            Assert.Equal(2, observations.Count);
            Assert.Equal(HostedUpdateStage.Cancelled, observations[1].Stage);
            Assert.Empty(host.ReadyMessages);
            Assert.False(verifier.WasCalled);
        }

        [Fact]
        public async Task SuccessfulPreparation_OrdersSemanticStagesBeforeSingleHostHandoff()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "candidate.zip" };
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                observations.Add);

            await journey.StartAsync(Request);

            Assert.Equal(
                new[]
                {
                    HostedUpdateStage.Downloading,
                    HostedUpdateStage.Verifying,
                    HostedUpdateStage.NotifyingHost,
                    HostedUpdateStage.WaitingForHostInstall
                },
                observations.ConvertAll(observation => observation.Stage));
            Assert.Single(host.ReadyMessages);
            Assert.Equal(Request.VersionTag, host.ReadyMessages[0].VersionTag);
            Assert.Equal("candidate.zip", host.ReadyMessages[0].PackagePath);
            Assert.True(journey.HandoffSent);
        }

        [Fact]
        public async Task PreparationFailure_UsesPreparationSemanticMessageAndCleansCandidate()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "candidate.zip" };
            var verifier = new FailingVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                observations.Add);

            await journey.StartAsync(Request);

            Assert.Equal(HostedUpdateStage.Failed, observations[2].Stage);
            Assert.Equal("Update_prepareFailed", observations[2].Message.Key);
            Assert.Equal("package is invalid", observations[2].Message.DiagnosticDetail);
            Assert.Equal(Request, downloader.CleanedRequest);
            Assert.Equal("candidate.zip", downloader.CleanedPackagePath);
            Assert.Empty(host.ReadyMessages);
        }

        [Fact]
        public async Task HandoffSendFailure_UsesHandoffSemanticMessageAndCleansUnsentCandidate()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "candidate.zip" };
            var verifier = new RecordingVerifier();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                delegate
                {
                    throw new InvalidOperationException("pipe closed");
                },
                observations.Add);

            await journey.StartAsync(Request);

            Assert.Equal(HostedUpdateStage.Failed, observations[3].Stage);
            Assert.Equal("Update_handoffFailed", observations[3].Message.Key);
            Assert.Equal("pipe closed", observations[3].Message.DiagnosticDetail);
            Assert.Equal(Request, downloader.CleanedRequest);
            Assert.Equal("candidate.zip", downloader.CleanedPackagePath);
            Assert.False(journey.HandoffSent);
        }

        [Fact]
        public async Task StartWhileOwned_EmitsOneRejectedAuthoritativeObservation()
        {
            var downloader = new ControlledDownloader();
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                observations.Add);

            Task first = journey.StartAsync(Request);
            Task<bool> second = journey.StartAsync(Request);

            Assert.False(await second);
            Assert.Equal(HostedUpdateStage.Rejected, observations[1].Stage);

            journey.Cancel();
            downloader.Complete("candidate.zip");
            await first;
        }

        private sealed class ControlledDownloader : IHostedUpdatePackageDownloader
        {
            private readonly TaskCompletionSource<string> completion =
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            public string ImmediateResult { get; set; }

            public CancellationToken CancellationToken { get; private set; }

            public HostedUpdateRequest CleanedRequest { get; private set; }

            public string CleanedPackagePath { get; private set; }

            public Task<string> DownloadAsync(HostedUpdateRequest request, CancellationToken cancellationToken)
            {
                CancellationToken = cancellationToken;
                if (ImmediateResult != null)
                    return Task.FromResult(ImmediateResult);
                return completion.Task;
            }

            public void Cleanup(HostedUpdateRequest request, string packagePath)
            {
                CleanedRequest = request;
                CleanedPackagePath = packagePath;
            }

            public void Complete(string packagePath)
            {
                completion.TrySetResult(packagePath);
            }

            public void Fail(Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private sealed class RecordingVerifier : IHostedUpdatePackageVerifier
        {
            public bool WasCalled { get; private set; }

            public void Verify(HostedUpdateRequest request, string packagePath, CancellationToken cancellationToken)
            {
                WasCalled = true;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private sealed class FailingVerifier : IHostedUpdatePackageVerifier
        {
            public void Verify(HostedUpdateRequest request, string packagePath, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("package is invalid");
            }
        }

        private sealed class BlockingVerifier : IHostedUpdatePackageVerifier
        {
            private readonly TaskCompletionSource<object> started =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<object> completed =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task Started { get { return started.Task; } }

            public CancellationToken CancellationToken { get; private set; }

            public void Verify(HostedUpdateRequest request, string packagePath, CancellationToken cancellationToken)
            {
                CancellationToken = cancellationToken;
                started.TrySetResult(null);
                completed.Task.GetAwaiter().GetResult();
                cancellationToken.ThrowIfCancellationRequested();
            }

            public void Complete()
            {
                completed.TrySetResult(null);
            }
        }

        private sealed class RecordingHost
        {
            public List<ReadyMessage> ReadyMessages { get; } = new List<ReadyMessage>();

            public void SendReady(string versionTag, string packagePath)
            {
                ReadyMessages.Add(new ReadyMessage(versionTag, packagePath));
            }
        }

        private sealed class ReadyMessage
        {
            public ReadyMessage(string versionTag, string packagePath)
            {
                VersionTag = versionTag;
                PackagePath = packagePath;
            }

            public string VersionTag { get; }

            public string PackagePath { get; }
        }
    }
}
