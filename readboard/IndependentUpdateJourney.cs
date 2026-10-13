using System;
using System.Threading;
using System.Threading.Tasks;

namespace readboard
{
    internal enum IndependentUpdateStage { Downloading, Verifying, Preparing, Committed, Cancelled, Failed }

    internal sealed class IndependentUpdateObservation
    {
        internal IndependentUpdateObservation(int generation, IndependentUpdateStage stage, SemanticMessage message)
        { Generation = generation; Stage = stage; Message = message; }
        internal int Generation { get; }
        internal IndependentUpdateStage Stage { get; }
        internal SemanticMessage Message { get; }
    }

    internal interface IIndependentUpdateCommitter
    {
        Task CommitAsync(HostedUpdateRequest request, string packagePath, Action persistConfiguration, CancellationToken cancellationToken);
    }

    internal sealed class IndependentUpdateJourney : IDisposable
    {
        private readonly IHostedUpdatePackageDownloader downloader;
        private readonly IHostedUpdatePackageVerifier verifier;
        private readonly IIndependentUpdateCommitter committer;
        private readonly Action persistConfiguration;
        private readonly Action<IndependentUpdateObservation> observe;
        private CancellationTokenSource cancellation;
        private bool committed;
        private bool disposed;
        internal int Generation { get; private set; }
        internal bool CanStart => !disposed && cancellation == null && !committed;

        internal IndependentUpdateJourney(IHostedUpdatePackageDownloader downloader, IHostedUpdatePackageVerifier verifier,
            IIndependentUpdateCommitter committer, Action persistConfiguration, Action<IndependentUpdateObservation> observe)
        {
            this.downloader = downloader;
            this.verifier = verifier;
            this.committer = committer;
            this.persistConfiguration = persistConfiguration;
            this.observe = observe;
        }

        internal async Task StartAsync(HostedUpdateRequest request)
        {
            if (!CanStart) return;
            int generation = ++Generation;
            var source = new CancellationTokenSource();
            cancellation = source;
            string package = null;
            try
            {
                Emit(IndependentUpdateStage.Downloading, "Update_downloadingPackage");
                package = await downloader.DownloadAsync(request, source.Token);
                source.Token.ThrowIfCancellationRequested();
                Emit(IndependentUpdateStage.Verifying, "Update_verifyingPackage");
                await Task.Run(() => verifier.Verify(request, package, source.Token), source.Token);
                source.Token.ThrowIfCancellationRequested();
                Emit(IndependentUpdateStage.Preparing, "IndependentUpdate_preparing");
                await committer.CommitAsync(request, package, persistConfiguration, source.Token);
                // The committer returns only for a persisted acceptance. Cancellation
                // after its COMMIT send is resolved by that acknowledgement, not this token.
                committed = true;
                Emit(IndependentUpdateStage.Committed, "IndependentUpdate_installing");
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
                Emit(IndependentUpdateStage.Cancelled, "IndependentUpdate_cancelled");
            }
            catch (Exception exception)
            {
                if (!source.IsCancellationRequested)
                    Emit(IndependentUpdateStage.Failed, "IndependentUpdate_failedUnchanged", exception.Message);
            }
            finally
            {
                if (!committed)
                {
                    try { downloader.Cleanup(request, package); }
                    catch (Exception exception) { System.Diagnostics.Trace.TraceError(exception.ToString()); }
                }
                if (ReferenceEquals(cancellation, source)) cancellation = null;
                source.Dispose();
            }

            void Emit(IndependentUpdateStage stage, string key, string diagnostic = null)
            {
                if (!disposed && (stage == IndependentUpdateStage.Committed || generation == Generation))
                    observe(new IndependentUpdateObservation(generation, stage, SemanticMessage.CreateWithDiagnostic(key, diagnostic)));
            }
        }

        internal bool Cancel()
        {
            if (committed || cancellation == null) return false;
            cancellation.Cancel();
            ++Generation;
            observe(new IndependentUpdateObservation(Generation, IndependentUpdateStage.Cancelled, SemanticMessage.Create("IndependentUpdate_cancelled")));
            return true;
        }

        public void Dispose()
        {
            disposed = true;
            if (!committed) cancellation?.Cancel();
        }
    }
}
