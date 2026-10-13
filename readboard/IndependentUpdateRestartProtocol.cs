using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace readboard
{
    internal sealed class IndependentUpdateRestartProtocol
    {
        private readonly object gate = new object();
        private readonly Action<string> send;
        private readonly TimeSpan responseTimeout;
        private bool supported;
        private string pendingOperation;
        private string acceptedOperation;
        private TaskCompletionSource<bool> pending;
        internal event Action<string> Connected;
        internal bool Supported { get { lock (gate) return supported; } }

        internal IndependentUpdateRestartProtocol(Action<string> send, TimeSpan? responseTimeout = null)
        {
            this.send = send;
            this.responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(5);
        }

        internal bool Handle(string line)
        {
            if (line == null || !line.StartsWith("readboardSelfUpdate", StringComparison.Ordinal)) return false;
            string[] fields = (line.EndsWith('\n') ? line.Substring(0, line.Length - 1) : line).Split('\t');
            if (fields.Any(field => field.Contains('\r') || field.Contains('\n')) || fields.Length < 2 || fields[1] != "1") return true;
            lock (gate)
            {
                if (fields[0] == "readboardSelfUpdateRestartSupported" && fields.Length == 2) supported = true;
                if (fields.Length < 3) return true;
                try { IndependentUpdateFiles.ValidateOperationId(fields[2]); } catch (InvalidDataException) { return true; }
                if (fields[0] == "readboardSelfUpdatePrepared" && fields.Length == 3)
                {
                    if (pendingOperation == fields[2] && pending != null) pending.TrySetResult(true);
                    else if (acceptedOperation != fields[2]) send("readboardSelfUpdateCancel\t1\t" + fields[2]);
                }
                else if (fields[0] == "readboardSelfUpdateRejected" && fields.Length == 4
                    && new[] { "busy", "staleSession", "invalidRequest", "unavailable" }.Contains(fields[3]))
                {
                    if (pendingOperation == fields[2]) pending?.TrySetResult(false);
                }
                else if (fields[0] == "readboardSelfUpdateConnected" && fields.Length == 3) Connected?.Invoke(fields[2]);
            }
            return true;
        }

        internal async Task<IndependentUpdateAdoption> PrepareAsync(string path, CancellationToken cancellationToken)
        {
            var request = IndependentUpdateRestart.ReadRequest(path);
            var adoption = new IndependentUpdateAdoption { OperationId = request.OperationId };
            TaskCompletionSource<bool> completion;
            lock (gate)
            {
                if (!supported) return adoption;
                if (pending != null) throw new InvalidOperationException("Restart registration is already pending.");
                pendingOperation = request.OperationId;
                pending = completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                send("readboardSelfUpdatePrepare\t1\t" + request.OperationId + "\t" + path);
            }
            bool adopted = false;
            try
            {
                bool accepted;
                try { accepted = await completion.Task.WaitAsync(responseTimeout, cancellationToken); }
                catch (TimeoutException) { return adoption; }
                if (!accepted) return adoption;
                cancellationToken.ThrowIfCancellationRequested();
                var registration = IndependentUpdateRestart.ReadRegistration(path, request);
                adoption.RegistrationId = registration.RegistrationId;
                adoption.HostProcessId = registration.HostProcessId;
                adoption.HostProcessStartTimeUtc = registration.HostProcessStartTimeUtc;
                lock (gate) acceptedOperation = request.OperationId;
                adopted = true;
                return adoption;
            }
            finally
            {
                lock (gate) { pending = null; pendingOperation = null; }
                if (!adopted) Cancel(request.OperationId);
            }
        }

        internal void Ready(string operationId, string version) => send("readboardSelfUpdateReady\t1\t" + operationId + "\t" + version);
        internal void Cancel(string operationId)
        {
            lock (gate)
            {
                if (acceptedOperation == operationId) acceptedOperation = null;
                if (pendingOperation == operationId) pending?.TrySetResult(false);
                send("readboardSelfUpdateCancel\t1\t" + operationId);
            }
        }
    }
}
