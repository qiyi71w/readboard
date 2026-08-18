using System;

namespace readboard
{
    internal interface IMainFormShutdownActions
    {
        void StopPlaceRequestQueue();
        void ClearPendingProtocolCommands();
        void ResetTitle();
        void PersistConfiguration();
        void DisposeInputHooks();
        void SendShutdownProtocol();
        void DisposeBitmap();
        void StopCoordinator();
        void DisposeWebViewUpdateBridge();
        void RequestClose();
    }

    internal sealed class MainFormShutdownCoordinator
    {
        private readonly IMainFormShutdownActions actions;

        public MainFormShutdownCoordinator(IMainFormShutdownActions actions)
        {
            this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        public void Execute(bool persistConfiguration, Action<Exception> recordException)
        {
            if (recordException == null)
                throw new ArgumentNullException(nameof(recordException));

            ExecuteStep(actions.StopPlaceRequestQueue, recordException);
            ExecuteStep(actions.ClearPendingProtocolCommands, recordException);
            ExecuteStep(actions.ResetTitle, recordException);
            if (persistConfiguration)
                ExecuteStep(actions.PersistConfiguration, recordException);
            ExecuteStep(actions.DisposeInputHooks, recordException);
            ExecuteStep(actions.SendShutdownProtocol, recordException);
            ExecuteStep(actions.DisposeBitmap, recordException);
            ExecuteStep(actions.StopCoordinator, recordException);
            ExecuteStep(actions.DisposeWebViewUpdateBridge, recordException);
            ExecuteStep(actions.RequestClose, recordException);
        }

        private static void ExecuteStep(Action action, Action<Exception> recordException)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                recordException(exception);
            }
        }
    }
}
