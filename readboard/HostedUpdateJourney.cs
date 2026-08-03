using System;
using System.Threading;
using System.Threading.Tasks;

namespace readboard
{
    internal sealed class HostedUpdateRequest
    {
        public HostedUpdateRequest(
            string versionTag,
            string assetName,
            string assetDownloadUrl,
            string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(versionTag))
                throw new ArgumentException("Version tag is required.", nameof(versionTag));
            if (versionTag.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException("Version tag must be a path component.", nameof(versionTag));
            if (string.IsNullOrWhiteSpace(assetName))
                throw new ArgumentException("Asset name is required.", nameof(assetName));
            if (assetName.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new ArgumentException("Asset name must be a file name.", nameof(assetName));
            if (string.IsNullOrWhiteSpace(assetDownloadUrl))
                throw new ArgumentException("Asset download URL is required.", nameof(assetDownloadUrl));
            if (string.IsNullOrWhiteSpace(expectedSha256))
                throw new ArgumentException("Expected SHA-256 is required.", nameof(expectedSha256));

            VersionTag = versionTag;
            AssetName = assetName;
            AssetDownloadUrl = assetDownloadUrl;
            ExpectedSha256 = expectedSha256;
        }

        public string VersionTag { get; }

        public string AssetName { get; }

        public string AssetDownloadUrl { get; }

        public string ExpectedSha256 { get; }

    }

    internal enum HostedUpdateStage
    {
        Downloading,
        Verifying,
        NotifyingHost,
        WaitingForHostInstall,
        HostInstalling,
        HostCancelled,
        HostFailed,
        HostTimedOut,
        Cancelled,
        Failed,
        Rejected
    }


    internal sealed class HostedUpdateObservation
    {
        public HostedUpdateObservation(
            int generation,
            HostedUpdateStage stage,
            SemanticMessage message,
            string packagePath = null)
        {
            Generation = generation;
            Stage = stage;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            PackagePath = packagePath;
        }

        public int Generation { get; }

        public HostedUpdateStage Stage { get; }

        public SemanticMessage Message { get; }

        public string PackagePath { get; }
    }

    internal interface IHostedUpdatePackageDownloader
    {
        Task<string> DownloadAsync(HostedUpdateRequest request, CancellationToken cancellationToken);

        void Cleanup(HostedUpdateRequest request, string packagePath);
    }

    internal interface IHostedUpdatePackageVerifier
    {
        void Verify(HostedUpdateRequest request, string packagePath, CancellationToken cancellationToken);
    }

    internal interface IHostedUpdateResponseTimeoutScheduler : IDisposable
    {
        void Start(Action callback);

        void Stop();
    }

    internal sealed class HostedUpdateResponseTimeoutScheduler : IHostedUpdateResponseTimeoutScheduler
    {
        private const int TimeoutMilliseconds = 15000;
        private readonly object syncRoot = new object();
        private Timer timer;

        public void Start(Action callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            lock (syncRoot)
            {
                StopUnsafe();
                timer = new Timer(
                    _ => callback(),
                    null,
                    TimeoutMilliseconds,
                    Timeout.Infinite);
            }
        }

        public void Stop()
        {
            lock (syncRoot)
                StopUnsafe();
        }

        public void Dispose()
        {
            Stop();
        }

        private void StopUnsafe()
        {
            if (timer == null)
                return;

            timer.Dispose();
            timer = null;
        }
    }

    internal sealed class HostedUpdateJourney : IDisposable
    {
        private readonly IHostedUpdatePackageDownloader downloader;
        private readonly IHostedUpdatePackageVerifier verifier;
        private readonly Func<string, string, bool> sendReady;
        private readonly IHostedUpdateResponseTimeoutScheduler responseTimeoutScheduler;
        private readonly Action<HostedUpdateObservation> observe;
        private readonly object stateSyncRoot = new object();
        private CancellationTokenSource operationCancellation;
        private HostedUpdateRequest activeRequest;
        private string activePackagePath;
        private int generation;
        private bool handoffSent;
        private bool handoffInProgress;
        private bool handoffBudgetConsumed;
        private bool hostInstallStarted;
        private bool hostOutcomeSettled;
        private bool pendingHostInstalling;
        private HostedUpdateStage? pendingHostOutcomeStage;
        private SemanticMessage pendingHostOutcome;
        private bool responseTimeoutArmed;
        private bool disposed;

        public HostedUpdateJourney(
            IHostedUpdatePackageDownloader downloader,
            IHostedUpdatePackageVerifier verifier,
            Func<string, string, bool> sendReady,
            IHostedUpdateResponseTimeoutScheduler responseTimeoutScheduler,
            Action<HostedUpdateObservation> observe)
        {
            this.downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            this.sendReady = sendReady ?? throw new ArgumentNullException(nameof(sendReady));
            this.responseTimeoutScheduler = responseTimeoutScheduler ?? throw new ArgumentNullException(nameof(responseTimeoutScheduler));
            this.observe = observe ?? throw new ArgumentNullException(nameof(observe));
        }

        public int Generation
        {
            get
            {
                lock (stateSyncRoot)
                    return generation;
            }
        }

        public bool HandoffSent
        {
            get
            {
                lock (stateSyncRoot)
                    return handoffSent;
            }
        }

        public bool CanStartHostedInstall
        {
            get
            {
                lock (stateSyncRoot)
                {
                    return !disposed &&
                        !handoffBudgetConsumed &&
                        operationCancellation == null;
                }
            }
        }

        public Task<bool> StartAsync(HostedUpdateRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            CancellationTokenSource cancellationSource;
            int operationGeneration;
            bool rejected;
            bool handoffAlreadySent;
            lock (stateSyncRoot)
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(HostedUpdateJourney));

                handoffAlreadySent = handoffBudgetConsumed;
                rejected = handoffAlreadySent || operationCancellation != null;
                if (rejected)
                {
                    cancellationSource = null;
                    operationGeneration = generation;
                }
                else
                {
                    StopResponseTimeoutUnsafe();
                    generation++;
                    operationGeneration = generation;
                    activeRequest = request;
                    activePackagePath = null;
                    handoffSent = false;
                    handoffInProgress = false;
                    hostInstallStarted = false;
                    hostOutcomeSettled = false;
                    pendingHostInstalling = false;
                    pendingHostOutcomeStage = null;
                    pendingHostOutcome = null;
                    cancellationSource = new CancellationTokenSource();
                    operationCancellation = cancellationSource;
                }
            }

            if (rejected)
            {
                Emit(
                    HostedUpdateStage.Rejected,
                    SemanticMessage.Create(handoffAlreadySent
                        ? "Update_handoffAlreadySent"
                        : "Update_operationAlreadyRunning"),
                    null,
                    operationGeneration);
                return Task.FromResult(false);
            }

            Emit(
                HostedUpdateStage.Downloading,
                SemanticMessage.Create("Update_downloadingPackage"),
                null,
                operationGeneration);
            return RunAsync(request, operationGeneration, cancellationSource);
        }

        public bool Cancel()
        {
            CancellationTokenSource cancellation;
            int cancellationGeneration;
            lock (stateSyncRoot)
            {
                if (disposed || operationCancellation == null || handoffSent || handoffInProgress)
                    return false;

                cancellation = operationCancellation;
                operationCancellation = null;
                activeRequest = null;
                activePackagePath = null;
                StopResponseTimeoutUnsafe();
                cancellationGeneration = ++generation;
            }

            cancellation.Cancel();
            cancellation.Dispose();
            // The late completion owns cleanup so ZIP verification can release its file handle first.
            Emit(
                HostedUpdateStage.Cancelled,
                SemanticMessage.Create("Update_cancelled"),
                null,
                cancellationGeneration);
            return true;
        }

        public bool MarkHostInstalling()
        {
            HostedUpdateObservation observation = null;
            lock (stateSyncRoot)
            {
                if (!CanAcceptHostObservationUnsafe() || hostOutcomeSettled || hostInstallStarted)
                    return false;

                hostInstallStarted = true;
                StopResponseTimeoutUnsafe();
                if (handoffInProgress)
                {
                    pendingHostInstalling = true;
                    return true;
                }

                observation = CreateHostObservation(
                    HostedUpdateStage.HostInstalling,
                    SemanticMessage.Create("Update_hostInstalling"));
            }

            Emit(observation);
            return true;
        }

        public bool MarkHostCancelled()
        {
            return SetHostOutcome(
                HostedUpdateStage.HostCancelled,
                SemanticMessage.Create("Update_hostCancelled"));
        }

        public bool MarkHostFailed(string detail)
        {
            return SetHostOutcome(
                HostedUpdateStage.HostFailed,
                SemanticMessage.CreateWithDiagnostic(
                    "Update_hostFailed",
                    SanitizeDiagnosticDetail(detail)));
        }

        public bool MarkHostTimedOut()
        {
            return SetHostOutcome(
                HostedUpdateStage.HostTimedOut,
                SemanticMessage.Create("Update_hostTimedOut"),
                true);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            CancellationTokenSource cancellation = null;
            lock (stateSyncRoot)
            {
                disposed = true;
                StopResponseTimeoutUnsafe();
                if (operationCancellation != null && !handoffSent && !handoffInProgress)
                {
                    cancellation = operationCancellation;
                    operationCancellation = null;
                    activeRequest = null;
                    activePackagePath = null;
                    generation++;
                }
            }

            responseTimeoutScheduler.Dispose();
            if (cancellation == null)
                return;

            cancellation.Cancel();
            cancellation.Dispose();
            // The late completion owns cleanup so ZIP verification can release its file handle first.
        }

        private async Task<bool> RunAsync(
            HostedUpdateRequest request,
            int operationGeneration,
            CancellationTokenSource cancellationSource)
        {
            CancellationToken cancellationToken = cancellationSource.Token;
            string packagePath = null;
            bool handoffAttempted = false;
            bool handoffDelivered = false;
            try
            {
                await Task.Yield();
                packagePath = await downloader.DownloadAsync(request, cancellationToken).ConfigureAwait(false);
                if (!AdoptDownloadedPackage(operationGeneration, cancellationSource, packagePath))
                {
                    Cleanup(request, packagePath);
                    return false;
                }

                Emit(
                    HostedUpdateStage.Verifying,
                    SemanticMessage.Create("Update_verifyingPackage"),
                    packagePath,
                    operationGeneration);
                verifier.Verify(request, packagePath, cancellationToken);
                if (!IsCurrent(operationGeneration, cancellationSource))
                {
                    Cleanup(request, packagePath);
                    return false;
                }

                Emit(
                    HostedUpdateStage.NotifyingHost,
                    SemanticMessage.Create("Update_notifyingHost"),
                    packagePath,
                    operationGeneration);
                handoffAttempted = true;
                if (!TrySendReady(request, packagePath, operationGeneration, cancellationSource))
                {
                    if (IsCurrent(operationGeneration, cancellationSource))
                    {
                        FailCurrentOperation(
                            request,
                            packagePath,
                            "Update_handoffFailed",
                            null,
                            operationGeneration,
                            cancellationSource,
                            false);
                    }
                    else
                    {
                        Cleanup(request, packagePath);
                    }
                    return false;
                }
                handoffDelivered = true;
                FlushPendingHostObservations();

                if (ShouldWaitForHost(operationGeneration, cancellationSource))
                {
                    Emit(
                        HostedUpdateStage.WaitingForHostInstall,
                        SemanticMessage.Create("Update_waitingForHostInstall"),
                        packagePath,
                        operationGeneration);
                }
                CompleteOperation(cancellationSource);
                return true;
            }
            catch (OperationCanceledException)
            {
                if (IsCurrent(operationGeneration, cancellationSource))
                    FailCurrentOperation(
                        request,
                        packagePath,
                        handoffAttempted ? "Update_handoffFailed" : "Update_cancelled",
                        null,
                        operationGeneration,
                        cancellationSource,
                        handoffDelivered);
                else if (!handoffDelivered)
                    Cleanup(request, packagePath);
                return false;
            }
            catch (Exception exception)
            {
                if (!IsCurrent(operationGeneration, cancellationSource))
                {
                    if (!handoffDelivered)
                        Cleanup(request, packagePath);
                    return false;
                }

                FailCurrentOperation(
                    request,
                    packagePath,
                    handoffAttempted ? "Update_handoffFailed" : "Update_prepareFailed",
                    SanitizeDiagnosticDetail(exception.Message),
                    operationGeneration,
                    cancellationSource,
                    handoffDelivered);
                return false;
            }
        }

        private bool IsCurrent(
            int operationGeneration,
            CancellationTokenSource cancellationSource)
        {
            lock (stateSyncRoot)
            {
                return IsCurrentUnsafe(operationGeneration, cancellationSource);
            }
        }

        private bool AdoptDownloadedPackage(
            int operationGeneration,
            CancellationTokenSource cancellationSource,
            string packagePath)
        {
            lock (stateSyncRoot)
            {
                if (!IsCurrentUnsafe(operationGeneration, cancellationSource))
                    return false;

                activePackagePath = packagePath;
                return true;
            }
        }

        private bool IsCurrentUnsafe(
            int operationGeneration,
            CancellationTokenSource cancellationSource)
        {
            return !disposed
                && generation == operationGeneration
                && ReferenceEquals(operationCancellation, cancellationSource)
                && !cancellationSource.IsCancellationRequested;
        }

        private bool TrySendReady(
            HostedUpdateRequest request,
            string packagePath,
            int operationGeneration,
            CancellationTokenSource cancellationSource)
        {
            lock (stateSyncRoot)
            {
                if (!IsCurrentUnsafe(operationGeneration, cancellationSource))
                    return false;

                handoffInProgress = true;
            }

            try
            {
                if (!sendReady(request.VersionTag, packagePath))
                {
                    lock (stateSyncRoot)
                    {
                        handoffInProgress = false;
                        StopResponseTimeoutUnsafe();
                        pendingHostInstalling = false;
                        pendingHostOutcomeStage = null;
                        pendingHostOutcome = null;
                    }
                    return false;
                }
            }
            catch
            {
                lock (stateSyncRoot)
                {
                    handoffInProgress = false;
                    StopResponseTimeoutUnsafe();
                    pendingHostInstalling = false;
                    pendingHostOutcomeStage = null;
                    pendingHostOutcome = null;
                    if (ReferenceEquals(operationCancellation, cancellationSource))
                    {
                        handoffSent = false;
                    }
                }
                throw;
            }

            lock (stateSyncRoot)
            {
                handoffSent = true;
                handoffBudgetConsumed = true;
                if (ReferenceEquals(operationCancellation, cancellationSource))
                    activePackagePath = null;
            }

            return true;
        }

        private bool SetHostOutcome(
            HostedUpdateStage stage,
            SemanticMessage message,
            bool rejectIfInstalling = false)
        {
            HostedUpdateObservation observation = null;
            lock (stateSyncRoot)
            {
                if (!CanAcceptHostObservationUnsafe() || hostOutcomeSettled ||
                    (rejectIfInstalling && hostInstallStarted))
                    return false;

                hostOutcomeSettled = true;
                StopResponseTimeoutUnsafe();
                if (handoffInProgress)
                {
                    pendingHostOutcomeStage = stage;
                    pendingHostOutcome = message;
                    return true;
                }

                observation = CreateHostObservation(stage, message);
            }

            Emit(observation);
            return true;
        }

        private bool CanAcceptHostObservationUnsafe()
        {
            return !disposed && (handoffSent || handoffInProgress);
        }

        private void StopResponseTimeoutUnsafe()
        {
            if (!responseTimeoutArmed)
                return;

            responseTimeoutArmed = false;
            responseTimeoutScheduler.Stop();
        }

        private HostedUpdateObservation CreateHostObservation(
            HostedUpdateStage stage,
            SemanticMessage message)
        {
            return new HostedUpdateObservation(generation, stage, message);
        }

        private void FlushPendingHostObservations()
        {
            while (true)
            {
                bool emitInstalling;
                HostedUpdateStage? outcomeStage;
                SemanticMessage outcome;
                lock (stateSyncRoot)
                {
                    emitInstalling = pendingHostInstalling;
                    pendingHostInstalling = false;
                    outcomeStage = pendingHostOutcomeStage;
                    pendingHostOutcomeStage = null;
                    outcome = pendingHostOutcome;
                    pendingHostOutcome = null;
                    if (!emitInstalling && !outcomeStage.HasValue && outcome == null)
                    {
                        handoffInProgress = false;
                        if (!disposed && !hostInstallStarted && !hostOutcomeSettled)
                        {
                            responseTimeoutArmed = true;
                            responseTimeoutScheduler.Start(delegate
                            {
                                MarkHostTimedOut();
                            });
                        }
                        return;
                    }
                }

                if (emitInstalling)
                    Emit(CreateHostObservation(
                        HostedUpdateStage.HostInstalling,
                        SemanticMessage.Create("Update_hostInstalling")));
                if (outcome != null && outcomeStage.HasValue)
                    Emit(CreateHostObservation(
                        outcomeStage.Value,
                        outcome));
            }
        }

        private bool ShouldWaitForHost(
            int operationGeneration,
            CancellationTokenSource cancellationSource)
        {
            lock (stateSyncRoot)
            {
                return IsCurrentUnsafe(operationGeneration, cancellationSource) &&
                    !hostInstallStarted &&
                    !hostOutcomeSettled;
            }
        }

        private void FailCurrentOperation(
            HostedUpdateRequest request,
            string packagePath,
            string messageKey,
            string diagnosticDetail,
            int operationGeneration,
            CancellationTokenSource cancellationSource,
            bool handoffDelivered)
        {
            bool currentOperation;
            bool shouldCleanup;
            lock (stateSyncRoot)
            {
                currentOperation = ReferenceEquals(operationCancellation, cancellationSource);
                shouldCleanup = !handoffDelivered;
                if (currentOperation)
                {
                    operationCancellation = null;
                    activeRequest = null;
                    activePackagePath = null;
                }
            }

            if (shouldCleanup)
                Cleanup(request, packagePath);
            if (!currentOperation)
                return;
            Emit(
                HostedUpdateStage.Failed,
                SemanticMessage.CreateWithDiagnostic(messageKey, diagnosticDetail),
                null,
                operationGeneration);
            cancellationSource.Dispose();
        }

        private void CompleteOperation(CancellationTokenSource cancellationSource)
        {
            lock (stateSyncRoot)
            {
                if (ReferenceEquals(operationCancellation, cancellationSource))
                {
                    operationCancellation = null;
                    activeRequest = null;
                }
            }
            cancellationSource.Dispose();
        }

        private void Cleanup(HostedUpdateRequest request, string packagePath)
        {
            if (request == null)
                return;
            downloader.Cleanup(request, packagePath);
        }

        private void Emit(
            HostedUpdateStage stage,
            SemanticMessage message,
            string packagePath,
            int observationGeneration)
        {
            observe(new HostedUpdateObservation(
                observationGeneration,
                stage,
                message,
                packagePath));
        }

        private void Emit(HostedUpdateObservation observation)
        {
            observe(observation);
        }

        private static string SanitizeDiagnosticDetail(string detail)
        {
            return string.IsNullOrWhiteSpace(detail)
                ? null
                : detail.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        }
    }
}
