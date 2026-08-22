using System;

namespace readboard
{
    internal sealed class LoggingHandshakeController
    {
        private readonly LaunchOptions launchOptions;
        private readonly LoggingRuntime runtime;
        private readonly Action<string> send;

        public LoggingHandshakeController(
            LaunchOptions launchOptions,
            LoggingRuntime runtime,
            Action<string> send)
        {
            this.launchOptions = launchOptions;
            this.runtime = runtime;
            this.send = send;
        }

        public void EmitCapability()
        {
            if (launchOptions == null
                || runtime == null
                || send == null
                || !launchOptions.ShouldEmitLoggingCapability)
            {
                return;
            }

            LoggingCapability capability = LoggingWireContract.ToCapability(runtime.Observe());
            if (string.IsNullOrEmpty(capability.ProcessSessionId)
                || !LoggingWireContract.IsOpaqueId(capability.ProcessSessionId))
            {
                return;
            }

            send(LoggingWireContract.FormatCapability(capability));
        }

        public bool TryHandleInbound(string rawLine)
        {
            if (!LoggingWireContract.IsLoggingControlLine(rawLine))
                return false;

            if (launchOptions == null || !launchOptions.ShouldEmitLoggingObserved)
                return true;

            LoggingSetRequest request;
            if (!LoggingWireContract.TryParseSet(rawLine, out request) || runtime == null || send == null)
                return true;

            LoggingObserved observed = runtime.ApplySet(request);
            string line;
            if (LoggingWireContract.TryFormatObserved(launchOptions, observed, out line))
                send(line);
            return true;
        }
    }
}
