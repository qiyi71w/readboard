using System;
using System.Security.Cryptography;

namespace readboard
{
    internal enum LoggingLaunchKind
    {
        Legacy = 0,
        Contract = 1,
        Unavailable = 2
    }

    internal sealed class LaunchOptions
    {
        private const string LogDirFlag = "--log-dir";
        private const string HostSessionIdFlag = "--host-session-id";
        private const string LoggingContractFlag = "--logging-contract";
        private const string DiagnosticsFlag = "--diagnostics";
        private const string CaptureFlag = "--capture";
        private const string ContractVersion = "1";

        public string AiTime { get; set; }
        public string Playouts { get; set; }
        public string FirstPolicy { get; set; }
        public TransportKind TransportKind { get; set; }
        public string Language { get; set; }
        public int TcpPort { get; set; }
        public LoggingLaunchKind LoggingKind { get; set; }
        public string LogDirectory { get; set; }
        public string HostSessionId { get; set; }
        public string LoggingContractVersion { get; set; }
        public bool? DiagnosticsEnabled { get; set; }
        public bool? CaptureEnabled { get; set; }
        public string ProcessSessionId { get; set; }

        public bool ShouldEmitLoggingCapability
        {
            get { return LoggingKind != LoggingLaunchKind.Legacy; }
        }

        public bool ShouldEmitLoggingObserved
        {
            get { return LoggingKind != LoggingLaunchKind.Legacy; }
        }

        public bool UsesLegacyLogFallback
        {
            get { return LoggingKind == LoggingLaunchKind.Legacy; }
        }

        public static bool TryParse(string[] args, out LaunchOptions options)
        {
            return TryParse(args, CreateProcessSessionId, out options);
        }

        internal static bool TryParse(string[] args, Func<string> processSessionIdFactory, out LaunchOptions options)
        {
            options = null;
            if (args == null || args.Length < 7 || !string.Equals(args[0], "yzy", StringComparison.Ordinal))
                return false;
            if (processSessionIdFactory == null)
                processSessionIdFactory = CreateProcessSessionId;

            int tcpPort;
            int.TryParse(args[6], out tcpPort);
            LoggingSuffix suffix = ParseLoggingSuffix(args);
            options = new LaunchOptions
            {
                AiTime = args[1],
                Playouts = args[2],
                FirstPolicy = args[3],
                TransportKind = args[4] == "1" ? TransportKind.Tcp : TransportKind.Pipe,
                Language = string.IsNullOrWhiteSpace(args[5]) ? "cn" : args[5],
                TcpPort = tcpPort,
                LoggingKind = suffix.Kind,
                LogDirectory = suffix.LogDirectory,
                HostSessionId = suffix.HostSessionId,
                LoggingContractVersion = suffix.ContractVersion,
                DiagnosticsEnabled = suffix.DiagnosticsEnabled,
                CaptureEnabled = suffix.CaptureEnabled,
                ProcessSessionId = processSessionIdFactory()
            };
            return true;
        }

        internal static string CreateProcessSessionId()
        {
            byte[] bytes = new byte[16];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private sealed class LoggingSuffix
        {
            public LoggingLaunchKind Kind;
            public string LogDirectory;
            public string HostSessionId;
            public string ContractVersion;
            public bool? DiagnosticsEnabled;
            public bool? CaptureEnabled;
        }

        private static LoggingSuffix ParseLoggingSuffix(string[] args)
        {
            LoggingSuffix suffix = new LoggingSuffix();
            bool sawKnownFlag = false;
            bool malformed = false;
            bool logDirPresent = false;
            bool hostSessionPresent = false;
            bool contractPresent = false;

            for (int i = 7; i < args.Length; i++)
            {
                string token = args[i];
                if (!IsFlag(token))
                    continue;

                bool hasValue = i + 1 < args.Length && !IsFlag(args[i + 1]);
                string value = hasValue ? args[++i] : null;

                if (string.Equals(token, LogDirFlag, StringComparison.Ordinal))
                {
                    sawKnownFlag = true;
                    logDirPresent = true;
                    suffix.LogDirectory = value;
                    if (value == null || !IsAbsoluteLogDirectory(value))
                        malformed = true;
                }
                else if (string.Equals(token, HostSessionIdFlag, StringComparison.Ordinal))
                {
                    sawKnownFlag = true;
                    hostSessionPresent = true;
                    if (value == null || !LoggingWireContract.IsOpaqueId(value))
                        malformed = true;
                    else
                        suffix.HostSessionId = value;
                }
                else if (string.Equals(token, LoggingContractFlag, StringComparison.Ordinal))
                {
                    sawKnownFlag = true;
                    contractPresent = true;
                    suffix.ContractVersion = value;
                    if (!string.Equals(value, ContractVersion, StringComparison.Ordinal))
                        malformed = true;
                }
                else if (string.Equals(token, DiagnosticsFlag, StringComparison.Ordinal))
                {
                    sawKnownFlag = true;
                    bool enabled;
                    if (!TryParseLaunchToggle(value, out enabled))
                    {
                        malformed = true;
                        suffix.DiagnosticsEnabled = null;
                    }
                    else
                    {
                        suffix.DiagnosticsEnabled = enabled;
                    }
                }
                else if (string.Equals(token, CaptureFlag, StringComparison.Ordinal))
                {
                    sawKnownFlag = true;
                    bool enabled;
                    if (!TryParseLaunchToggle(value, out enabled))
                    {
                        malformed = true;
                        suffix.CaptureEnabled = null;
                    }
                    else
                    {
                        suffix.CaptureEnabled = enabled;
                    }
                }
            }

            if (!sawKnownFlag)
            {
                suffix.Kind = LoggingLaunchKind.Legacy;
                return suffix;
            }

            bool complete = !malformed
                && logDirPresent
                && hostSessionPresent
                && contractPresent
                && string.Equals(suffix.ContractVersion, ContractVersion, StringComparison.Ordinal)
                && IsAbsoluteLogDirectory(suffix.LogDirectory)
                && LoggingWireContract.IsOpaqueId(suffix.HostSessionId);
            suffix.Kind = complete ? LoggingLaunchKind.Contract : LoggingLaunchKind.Unavailable;
            if (complete)
            {
                if (!suffix.DiagnosticsEnabled.HasValue)
                    suffix.DiagnosticsEnabled = false;
                if (!suffix.CaptureEnabled.HasValue)
                    suffix.CaptureEnabled = false;
            }

            return suffix;
        }

        private static bool TryParseLaunchToggle(string value, out bool enabled)
        {
            enabled = false;
            if (string.Equals(value, ProtocolKeywords.LoggingOn, StringComparison.Ordinal))
            {
                enabled = true;
                return true;
            }

            return string.Equals(value, ProtocolKeywords.LoggingOff, StringComparison.Ordinal);
        }

        private static bool IsFlag(string token)
        {
            return !string.IsNullOrEmpty(token) && token.StartsWith("--", StringComparison.Ordinal);
        }

        internal static bool IsAbsoluteLogDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            if (path.Length >= 4
                && IsAsciiLetter(path[0])
                && path[1] == ':'
                && (path[2] == '\\' || path[2] == '/')
                && path[3] != '\\'
                && path[3] != '/')
                return true;
            if (!path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal))
                return false;

            int serverEnd = IndexOfSeparator(path, 2);
            if (serverEnd <= 2)
                return false;
            int shareStart = serverEnd + 1;
            if (shareStart >= path.Length)
                return false;
            int shareEnd = IndexOfSeparator(path, shareStart);
            int shareLength = (shareEnd < 0 ? path.Length : shareEnd) - shareStart;
            return shareLength > 0;
        }

        private static int IndexOfSeparator(string path, int start)
        {
            return path.IndexOfAny(new[] { '\\', '/' }, start);
        }

        private static bool IsAsciiLetter(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        }
    }
}
