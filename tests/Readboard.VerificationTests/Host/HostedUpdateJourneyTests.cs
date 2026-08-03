using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using readboard;
using Xunit;
using Readboard.VerificationTests.Support;

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
                new ManualTimeoutScheduler(),
                observations.Add);

            Task run = journey.StartAsync(Request);
            await VerificationCompletion.WaitAsync(
                downloader.DownloadStarted,
                "Hosted update downloader did not start.");

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
                new ManualTimeoutScheduler(),
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
                new ManualTimeoutScheduler(),
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
                new ManualTimeoutScheduler(),
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
        public async Task CancelBeforeReadySend_DoesNotConsumeHandoffAllowance()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "candidate.zip" };
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            HostedUpdateJourney journey = null;
            bool cancelResult = false;
            journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                new ManualTimeoutScheduler(),
                delegate(HostedUpdateObservation observation)
                {
                    observations.Add(observation);
                    if (observation.Stage == HostedUpdateStage.NotifyingHost)
                        cancelResult = journey.Cancel();
                });

            Assert.False(await journey.StartAsync(Request));
            Assert.True(cancelResult);
            Assert.False(journey.HandoffSent);
            Assert.Empty(host.ReadyMessages);
            Assert.Contains(
                observations,
                observation => observation.Stage == HostedUpdateStage.Cancelled);
        }

        [Fact]
        public async Task SuccessfulHandoff_ConsumesProcessBudgetAndRejectsLaterAttempt()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "candidate.zip" };
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                new ManualTimeoutScheduler(),
                observations.Add);

            Assert.True(await journey.StartAsync(Request));

            Assert.False(await journey.StartAsync(Request));

            Assert.Equal(1, downloader.DownloadCallCount);
            Assert.Single(host.ReadyMessages);
            Assert.Equal(HostedUpdateStage.Rejected, observations[4].Stage);
            Assert.Equal("Update_handoffAlreadySent", observations[4].Message.Key);
        }

        [Fact]
        public async Task PreparationCancellation_DoesNotConsumeProcessBudget()
        {
            var downloader = new ControlledDownloader();
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                new ManualTimeoutScheduler(),
                observations.Add);

            Task first = journey.StartAsync(Request);
            Assert.True(journey.Cancel());
            downloader.Complete("cancelled.zip");
            await first;

            downloader.ImmediateResult = "retry.zip";
            Assert.True(await journey.StartAsync(Request));

            Assert.Single(host.ReadyMessages);
            Assert.True(journey.HandoffSent);
        }

        [Fact]
        public async Task PreparationFailure_DoesNotConsumeProcessBudget()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "candidate.zip" };
            var verifier = new FailsOnceVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                new ManualTimeoutScheduler(),
                observations.Add);

            Assert.False(await journey.StartAsync(Request));
            Assert.True(await journey.StartAsync(Request));

            Assert.Single(host.ReadyMessages);
            Assert.True(journey.HandoffSent);
        }

        [Fact]
        public async Task HostCancellationAfterHandoff_ConsumesBudgetAndIgnoresLateReplies()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "handed-off.zip" };
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var scheduler = new ManualTimeoutScheduler();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                scheduler,
                observations.Add);

            Assert.True(await journey.StartAsync(Request));
            int observationCount = observations.Count;

            Assert.True(journey.MarkHostCancelled());
            Assert.False(journey.MarkHostFailed("late failure"));
            Assert.False(journey.MarkHostTimedOut());
            Assert.False(await journey.StartAsync(Request));

            Assert.Equal(observationCount + 2, observations.Count);
            Assert.Equal(HostedUpdateStage.HostCancelled, observations[observationCount].Stage);
            Assert.Equal("Update_hostCancelled", observations[observationCount].Message.Key);
            Assert.Equal(HostedUpdateStage.Rejected, observations[observationCount + 1].Stage);
            Assert.Null(downloader.CleanedPackagePath);
        }

        [Fact]
        public async Task HostInstallingAfterHandoff_IsPublishedOnce()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "handed-off.zip" };
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var scheduler = new ManualTimeoutScheduler();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                scheduler,
                observations.Add);

            Assert.True(await journey.StartAsync(Request));
            int observationCount = observations.Count;

            Assert.True(journey.MarkHostInstalling());
            Assert.False(journey.MarkHostInstalling());

            Assert.Equal(observationCount + 1, observations.Count);
            Assert.Equal(HostedUpdateStage.HostInstalling, observations[observationCount].Stage);
            Assert.Equal("Update_hostInstalling", observations[observationCount].Message.Key);
        }

        [Fact]
        public async Task FastHostReplyDuringSend_IsSettledAfterSuccessfulHandoff()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "handed-off.zip" };
            var verifier = new RecordingVerifier();
            var observations = new List<HostedUpdateObservation>();
            HostedUpdateJourney journey = null;
            journey = new HostedUpdateJourney(
                downloader,
                verifier,
                delegate(string tag, string packagePath)
                {
                    Assert.True(journey.MarkHostInstalling());
                    Assert.True(journey.MarkHostCancelled());
                    return true;
                },
                new ManualTimeoutScheduler(),
                observations.Add);

            Assert.True(await journey.StartAsync(Request));

            Assert.Contains(observations, observation => observation.Stage == HostedUpdateStage.HostInstalling);
            Assert.Contains(observations, observation => observation.Stage == HostedUpdateStage.HostCancelled);
            Assert.False(journey.MarkHostTimedOut());
            Assert.False(await journey.StartAsync(Request));
            Assert.Null(downloader.CleanedPackagePath);
        }

        [Fact]
        public async Task PostHandoffObservationFailure_DoesNotCleanHandedOffPackage()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "handed-off.zip" };
            var verifier = new RecordingVerifier();
            var scheduler = new ManualTimeoutScheduler();
            var observations = new List<HostedUpdateObservation>();
            bool throwOnce = true;
            HostedUpdateJourney journey = null;
            journey = new HostedUpdateJourney(
                downloader,
                verifier,
                (tag, packagePath) =>
                {
                    Assert.True(journey.MarkHostInstalling());
                    return true;
                },
                scheduler,
                observation =>
                {
                    observations.Add(observation);
                    if (throwOnce && observation.Stage == HostedUpdateStage.HostInstalling)
                    {
                        throwOnce = false;
                        throw new InvalidOperationException("observer failed");
                    }
                });

            Assert.False(await journey.StartAsync(Request));

            Assert.True(journey.HandoffSent);
            Assert.Null(downloader.CleanedPackagePath);
            Assert.Contains(
                observations,
                observation => observation.Stage == HostedUpdateStage.Failed);
            journey.Dispose();
            Assert.True(scheduler.WasDisposed);
        }

        [Fact]
        public async Task ClosedHostTransport_DoesNotConsumeHandoffBudget()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "candidate.zip" };
            var verifier = new RecordingVerifier();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                (tag, packagePath) => false,
                new ManualTimeoutScheduler(),
                observations.Add);

            Assert.False(await journey.StartAsync(Request));
            Assert.Equal("Update_handoffFailed", observations[3].Message.Key);
            Assert.Equal("candidate.zip", downloader.CleanedPackagePath);
            Assert.True(journey.CanStartHostedInstall);
        }

        [Fact]
        public async Task HostFailureAfterHandoff_SanitizesDetailWithoutCleaningHandedOffPackage()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "handed-off.zip" };
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                new ManualTimeoutScheduler(),
                observations.Add);

            Assert.True(await journey.StartAsync(Request));

            Assert.True(journey.MarkHostFailed("bad\r\nzip\tpath"));

            HostedUpdateObservation failure = observations[observations.Count - 1];
            Assert.Equal(HostedUpdateStage.HostFailed, failure.Stage);
            Assert.Equal("Update_hostFailed", failure.Message.Key);
            Assert.Equal("bad  zip path", failure.Message.DiagnosticDetail);
            Assert.Null(downloader.CleanedPackagePath);
            Assert.False(await journey.StartAsync(Request));
        }

        [Fact]
        public async Task HostTimeoutAfterHandoff_IsDeterministicAndRejectsLateReply()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "handed-off.zip" };
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                new ManualTimeoutScheduler(),
                observations.Add);

            Assert.True(await journey.StartAsync(Request));

            Assert.True(journey.MarkHostTimedOut());
            Assert.False(journey.MarkHostInstalling());

            HostedUpdateObservation timeout = observations[observations.Count - 1];
            Assert.Equal(HostedUpdateStage.HostTimedOut, timeout.Stage);
            Assert.Equal("Update_hostTimedOut", timeout.Message.Key);
            Assert.False(await journey.StartAsync(Request));
            Assert.Null(downloader.CleanedPackagePath);
        }

        [Fact]
        public async Task ControlledTimeoutScheduler_ExpiresOnceAndInstallingDisarmsDeadline()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "handed-off.zip" };
            var verifier = new RecordingVerifier();
            var scheduler = new ManualTimeoutScheduler();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                (tag, packagePath) => true,
                scheduler,
                observations.Add);

            Assert.True(await journey.StartAsync(Request));
            Assert.True(scheduler.IsArmed);
            scheduler.Fire();

            Assert.Contains(observations, observation => observation.Stage == HostedUpdateStage.HostTimedOut);
            Assert.False(scheduler.IsArmed);
            Assert.False(journey.MarkHostInstalling());

            var secondDownloader = new ControlledDownloader { ImmediateResult = "installing.zip" };
            var secondObservations = new List<HostedUpdateObservation>();
            var secondScheduler = new ManualTimeoutScheduler();
            var secondJourney = new HostedUpdateJourney(
                secondDownloader,
                new RecordingVerifier(),
                (tag, packagePath) => true,
                secondScheduler,
                secondObservations.Add);

            Assert.True(await secondJourney.StartAsync(Request));
            Assert.True(secondJourney.MarkHostInstalling());
            Assert.False(secondScheduler.IsArmed);
            secondScheduler.Fire();
            Assert.DoesNotContain(
                secondObservations,
                observation => observation.Stage == HostedUpdateStage.HostTimedOut);
        }

        [Fact]
        public async Task DisposeAfterHandoff_DoesNotCancelOrCleanHandedOffPackage()
        {
            var downloader = new ControlledDownloader { ImmediateResult = "handed-off.zip" };
            var verifier = new RecordingVerifier();
            var host = new RecordingHost();
            var scheduler = new ManualTimeoutScheduler();
            var observations = new List<HostedUpdateObservation>();
            var journey = new HostedUpdateJourney(
                downloader,
                verifier,
                host.SendReady,
                scheduler,
                observations.Add);

            Assert.True(await journey.StartAsync(Request));

            journey.Dispose();

            Assert.False(downloader.CancellationToken.IsCancellationRequested);
            Assert.Null(downloader.CleanedPackagePath);
            Assert.False(journey.MarkHostFailed("late failure"));
            Assert.True(scheduler.WasDisposed);
        }

        [Fact]
        public async Task NewJourneyInstance_StartsWithFreshHandoffBudget()
        {
            var firstDownloader = new ControlledDownloader { ImmediateResult = "first.zip" };
            var firstHost = new RecordingHost();
            var firstJourney = new HostedUpdateJourney(
                firstDownloader,
                new RecordingVerifier(),
                firstHost.SendReady,
                new ManualTimeoutScheduler(),
                _ => { });

            Assert.True(await firstJourney.StartAsync(Request));
            Assert.False(await firstJourney.StartAsync(Request));

            var secondDownloader = new ControlledDownloader { ImmediateResult = "second.zip" };
            var secondHost = new RecordingHost();
            var secondJourney = new HostedUpdateJourney(
                secondDownloader,
                new RecordingVerifier(),
                secondHost.SendReady,
                new ManualTimeoutScheduler(),
                _ => { });

            Assert.True(await secondJourney.StartAsync(Request));
            Assert.Single(secondHost.ReadyMessages);
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
                new ManualTimeoutScheduler(),
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
                new ManualTimeoutScheduler(),
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
                new ManualTimeoutScheduler(),
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
            private readonly TaskCompletionSource<bool> started =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public string ImmediateResult { get; set; }

            public int DownloadCallCount { get; private set; }

            public CancellationToken CancellationToken { get; private set; }

            public Task DownloadStarted
            {
                get { return started.Task; }
            }

            public HostedUpdateRequest CleanedRequest { get; private set; }

            public string CleanedPackagePath { get; private set; }

            public Task<string> DownloadAsync(HostedUpdateRequest request, CancellationToken cancellationToken)
            {
                DownloadCallCount++;
                CancellationToken = cancellationToken;
                started.TrySetResult(true);
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

        private sealed class FailsOnceVerifier : IHostedUpdatePackageVerifier
        {
            private bool failNext = true;

            public void Verify(HostedUpdateRequest request, string packagePath, CancellationToken cancellationToken)
            {
                if (failNext)
                {
                    failNext = false;
                    throw new InvalidOperationException("package is invalid");
                }
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

            public bool SendReady(string versionTag, string packagePath)
            {
                ReadyMessages.Add(new ReadyMessage(versionTag, packagePath));
                return true;
            }
        }

        private sealed class ManualTimeoutScheduler : IHostedUpdateResponseTimeoutScheduler
        {
            private Action callback;

            public bool IsArmed { get { return callback != null; } }

            public bool WasDisposed { get; private set; }

            public void Start(Action callback)
            {
                this.callback = callback;
            }

            public void Stop()
            {
                callback = null;
            }

            public void Fire()
            {
                Action current = callback;
                callback = null;
                if (current != null)
                    current();
            }

            public void Dispose()
            {
                callback = null;
                WasDisposed = true;
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
