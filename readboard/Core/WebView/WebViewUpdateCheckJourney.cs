using System;
using System.Threading;
using System.Threading.Tasks;

namespace readboard
{
    internal enum WebViewUpdateCheckObservationKind
    {
        Started = 0,
        Completed = 1,
        Failed = 2
    }

    internal sealed class WebViewUpdateCheckObservation
    {
        private WebViewUpdateCheckObservation(
            WebViewUpdateCheckObservationKind kind,
            UpdateCheckResult result,
            bool hostedInstallAvailable,
            Exception exception)
        {
            Kind = kind;
            Result = result;
            HostedInstallAvailable = hostedInstallAvailable;
            Exception = exception;
        }

        public WebViewUpdateCheckObservationKind Kind { get; }
        public UpdateCheckResult Result { get; }
        public bool HostedInstallAvailable { get; }
        public Exception Exception { get; }

        public static WebViewUpdateCheckObservation Started()
        {
            return new WebViewUpdateCheckObservation(
                WebViewUpdateCheckObservationKind.Started,
                null,
                false,
                null);
        }

        public static WebViewUpdateCheckObservation Completed(
            UpdateCheckResult result,
            bool hostedInstallAvailable)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            return new WebViewUpdateCheckObservation(
                WebViewUpdateCheckObservationKind.Completed,
                result,
                hostedInstallAvailable,
                null);
        }

        public static WebViewUpdateCheckObservation Failed(Exception exception)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));
            return new WebViewUpdateCheckObservation(
                WebViewUpdateCheckObservationKind.Failed,
                null,
                false,
                exception);
        }
    }

    internal sealed class WebViewUpdateCheckJourney
    {
        private readonly Action<WebViewUpdateCheckObservation> observe;
        private int generation;
        private bool isRunning;
        private CancellationTokenSource cancellation;

        public WebViewUpdateCheckJourney(Action<WebViewUpdateCheckObservation> observe)
        {
            this.observe = observe ?? throw new ArgumentNullException(nameof(observe));
        }

        public bool IsRunning
        {
            get { return isRunning; }
        }

        public async Task StartAsync(
            Func<CancellationToken, Task<UpdateCheckResult>> check,
            Func<UpdateCheckResult, bool> canOfferHostedInstall)
        {
            if (check == null)
                throw new ArgumentNullException(nameof(check));
            if (canOfferHostedInstall == null)
                throw new ArgumentNullException(nameof(canOfferHostedInstall));
            if (isRunning)
                return;

            int currentGeneration = ++generation;
            var requestCancellation = new CancellationTokenSource();
            cancellation = requestCancellation;
            isRunning = true;
            try
            {
                observe(WebViewUpdateCheckObservation.Started());
                if (currentGeneration != generation)
                    return;

                UpdateCheckResult result;
                bool hostedInstallAvailable;
                try
                {
                    result = await check(requestCancellation.Token);
                    if (currentGeneration != generation)
                        return;
                    if (result == null)
                        throw new InvalidOperationException("Update check returned no result.");

                    hostedInstallAvailable = canOfferHostedInstall(result);
                    if (currentGeneration != generation)
                        return;
                }
                catch (Exception exception)
                {
                    if (currentGeneration != generation)
                        return;

                    isRunning = false;
                    observe(WebViewUpdateCheckObservation.Failed(exception));
                    return;
                }

                isRunning = false;
                observe(WebViewUpdateCheckObservation.Completed(
                    result,
                    hostedInstallAvailable));
            }
            finally
            {
                if (currentGeneration == generation)
                    isRunning = false;
                if (ReferenceEquals(cancellation, requestCancellation))
                    cancellation = null;
                requestCancellation.Dispose();
            }
        }

        public bool Cancel()
        {
            if (!isRunning)
                return false;

            CancellationTokenSource activeCancellation = cancellation;
            generation++;
            isRunning = false;
            cancellation = null;
            activeCancellation?.Cancel();
            return true;
        }
    }
}
