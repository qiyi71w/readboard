using System;

namespace readboard
{
    internal enum LoggingToggle
    {
        Off = 0,
        On = 1,
        Unknown = 2
    }

    internal enum LoggingPersistenceHealth
    {
        Healthy = 0,
        Degraded = 1,
        Unavailable = 2
    }

    internal enum LoggingFailureReason
    {
        Applied = 0,
        LegacyHelper = 1,
        CapabilityTimeout = 2,
        PathUnavailable = 3,
        WriterFault = 4,
        InvalidRequest = 5
    }

    internal enum LoggingPrivacy
    {
        Safe = 0,
        LocalPath = 1,
        LocalUrl = 2,
        UserText = 3,
        SessionId = 4,
        Secret = 5
    }


    internal sealed class LoggingCapability
    {
        public string ProcessSessionId { get; set; }
        public LoggingToggle Diagnostics { get; set; }
        public LoggingToggle Capture { get; set; }
        public LoggingToggle Trace { get; set; }
        public LoggingPersistenceHealth Persistence { get; set; }
        public int DropCount { get; set; }
    }

    internal sealed class LoggingSetRequest
    {
        public string RequestId { get; set; }
        public LoggingToggle Diagnostics { get; set; }
        public LoggingToggle Capture { get; set; }
        public LoggingToggle Trace { get; set; }
    }

    internal sealed class LoggingObserved
    {
        public string RequestId { get; set; }
        public string ProcessSessionId { get; set; }
        public LoggingToggle Diagnostics { get; set; }
        public LoggingToggle Capture { get; set; }
        public LoggingToggle Trace { get; set; }
        public LoggingPersistenceHealth Persistence { get; set; }
        public int DropCount { get; set; }
        public LoggingFailureReason Reason { get; set; }
    }

    internal sealed class LoggingRequestGate
    {
        public string LatestRequestId { get; private set; }

        public void NoteRequest(string requestId)
        {
            if (LoggingWireContract.IsOpaqueId(requestId))
                LatestRequestId = requestId;
        }

        public bool IsCurrent(string requestId)
        {
            return LatestRequestId != null
                && string.Equals(LatestRequestId, requestId, StringComparison.Ordinal);
        }

        public bool TryAcceptObserved(LoggingObserved observed)
        {
            return observed != null && IsCurrent(observed.RequestId);
        }
    }

    internal static class LoggingWireContract
    {
        public static bool IsOpaqueId(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool allowed = (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '-'
                    || c == '_';
                if (!allowed)
                    return false;
            }

            return true;
        }

        public static bool IsLoggingControlLine(string rawLine)
        {
            string command;
            return TryReadCommand(rawLine, out command)
                && (string.Equals(command, ProtocolKeywords.LoggingCapability, StringComparison.Ordinal)
                    || string.Equals(command, ProtocolKeywords.LoggingSet, StringComparison.Ordinal)
                    || string.Equals(command, ProtocolKeywords.LoggingObserved, StringComparison.Ordinal));
        }

        public static bool TryParseCapability(string rawLine, out LoggingCapability message)
        {
            message = null;
            string[] fields;
            if (!TrySplitFields(rawLine, 7, out fields)
                || !string.Equals(fields[0], ProtocolKeywords.LoggingCapability, StringComparison.Ordinal)
                || !IsOpaqueId(fields[1]))
                return false;

            LoggingToggle diagnostics;
            LoggingToggle capture;
            LoggingToggle trace;
            LoggingPersistenceHealth persistence;
            int dropCount;
            if (!TryParseDeterminateToggle(fields[2], out diagnostics)
                || !TryParseDeterminateToggle(fields[3], out capture)
                || !TryParseDeterminateToggle(fields[4], out trace)
                || !TryParsePersistence(fields[5], out persistence)
                || !TryParseDropCount(fields[6], out dropCount))
                return false;

            message = new LoggingCapability
            {
                ProcessSessionId = fields[1],
                Diagnostics = diagnostics,
                Capture = capture,
                Trace = trace,
                Persistence = persistence,
                DropCount = dropCount
            };
            return true;
        }

        public static bool TryParseSet(string rawLine, out LoggingSetRequest message)
        {
            message = null;
            string[] fields;
            if (!TrySplitFields(rawLine, 5, out fields)
                || !string.Equals(fields[0], ProtocolKeywords.LoggingSet, StringComparison.Ordinal)
                || !IsOpaqueId(fields[1]))
                return false;

            LoggingToggle diagnostics;
            LoggingToggle capture;
            LoggingToggle trace;
            if (!TryParseDeterminateToggle(fields[2], out diagnostics)
                || !TryParseDeterminateToggle(fields[3], out capture)
                || !TryParseDeterminateToggle(fields[4], out trace))
                return false;

            message = new LoggingSetRequest
            {
                RequestId = fields[1],
                Diagnostics = diagnostics,
                Capture = capture,
                Trace = trace
            };
            return true;
        }

        public static bool TryParseObserved(string rawLine, out LoggingObserved message)
        {
            message = null;
            string[] fields;
            if (!TrySplitFields(rawLine, 9, out fields)
                || !string.Equals(fields[0], ProtocolKeywords.LoggingObserved, StringComparison.Ordinal)
                || !IsOpaqueId(fields[1])
                || !IsOpaqueId(fields[2]))
                return false;

            LoggingToggle diagnostics;
            LoggingToggle capture;
            LoggingToggle trace;
            LoggingPersistenceHealth persistence;
            int dropCount;
            LoggingFailureReason reason;
            if (!TryParseObservedToggle(fields[3], out diagnostics)
                || !TryParseObservedToggle(fields[4], out capture)
                || !TryParseObservedToggle(fields[5], out trace)
                || !TryParsePersistence(fields[6], out persistence)
                || !TryParseDropCount(fields[7], out dropCount)
                || !TryParseReason(fields[8], out reason))
                return false;

            message = new LoggingObserved
            {
                RequestId = fields[1],
                ProcessSessionId = fields[2],
                Diagnostics = diagnostics,
                Capture = capture,
                Trace = trace,
                Persistence = persistence,
                DropCount = dropCount,
                Reason = reason
            };
            return true;
        }

        public static string FormatCapability(LoggingCapability message)
        {
            if (message == null)
                throw new ArgumentNullException("message");

            return string.Join(
                " ",
                ProtocolKeywords.LoggingCapability,
                message.ProcessSessionId,
                FormatDeterminateToggle(message.Diagnostics),
                FormatDeterminateToggle(message.Capture),
                FormatDeterminateToggle(message.Trace),
                FormatPersistence(message.Persistence),
                FormatDropCount(message.DropCount));
        }

        public static string FormatSet(LoggingSetRequest message)
        {
            if (message == null)
                throw new ArgumentNullException("message");

            return string.Join(
                " ",
                ProtocolKeywords.LoggingSet,
                message.RequestId,
                FormatDeterminateToggle(message.Diagnostics),
                FormatDeterminateToggle(message.Capture),
                FormatDeterminateToggle(message.Trace));
        }

        public static string FormatObserved(LoggingObserved message)
        {
            if (message == null)
                throw new ArgumentNullException("message");

            return string.Join(
                " ",
                ProtocolKeywords.LoggingObserved,
                message.RequestId,
                message.ProcessSessionId,
                FormatObservedToggle(message.Diagnostics),
                FormatObservedToggle(message.Capture),
                FormatObservedToggle(message.Trace),
                FormatPersistence(message.Persistence),
                FormatDropCount(message.DropCount),
                FormatReason(message.Reason));
        }

        public static LoggingCapability CreateLaunchCapability(LaunchOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            return new LoggingCapability
            {
                ProcessSessionId = options.ProcessSessionId,
                Diagnostics = options.DiagnosticsEnabled == true ? LoggingToggle.On : LoggingToggle.Off,
                Capture = options.CaptureEnabled == true ? LoggingToggle.On : LoggingToggle.Off,
                Trace = LoggingToggle.Off,
                Persistence = options.LoggingKind == LoggingLaunchKind.Contract
                    ? LoggingPersistenceHealth.Healthy
                    : LoggingPersistenceHealth.Unavailable,
                DropCount = 0
            };
        }

        public static bool TryFormatCapability(LaunchOptions options, out string line)
        {
            line = null;
            if (options == null || !options.ShouldEmitLoggingCapability)
                return false;

            line = FormatCapability(CreateLaunchCapability(options));
            return true;
        }

        public static bool TryFormatObserved(LaunchOptions options, LoggingObserved observed, out string line)
        {
            line = null;
            if (options == null || observed == null || !options.ShouldEmitLoggingObserved)
                return false;

            line = FormatObserved(observed);
            return true;
        }

        public static LoggingCapability ToCapability(LoggingObservedSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");

            return new LoggingCapability
            {
                ProcessSessionId = snapshot.ProcessSessionId,
                Diagnostics = snapshot.Diagnostics == LoggingToggle.On ? LoggingToggle.On : LoggingToggle.Off,
                Capture = snapshot.Capture == LoggingToggle.On ? LoggingToggle.On : LoggingToggle.Off,
                Trace = snapshot.Trace == LoggingToggle.On ? LoggingToggle.On : LoggingToggle.Off,
                Persistence = snapshot.Persistence,
                DropCount = snapshot.DropCount
            };
        }

        public static LoggingObserved ToObserved(string requestId, LoggingObservedSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");

            return new LoggingObserved
            {
                RequestId = requestId,
                ProcessSessionId = snapshot.ProcessSessionId,
                Diagnostics = snapshot.Diagnostics,
                Capture = snapshot.Capture,
                Trace = snapshot.Trace,
                Persistence = snapshot.Persistence,
                DropCount = snapshot.DropCount,
                Reason = snapshot.Reason
            };
        }

        public static LoggingPersistenceHealth WorstPersistence(
            LoggingPersistenceHealth app,
            LoggingPersistenceHealth trace,
            LoggingPersistenceHealth crash,
            LoggingPersistenceHealth capture)
        {
            LoggingPersistenceHealth worst = app;
            if (trace > worst)
                worst = trace;
            if (crash > worst)
                worst = crash;
            if (capture > worst)
                worst = capture;
            return worst;
        }

        public static int CombineDropCount(int runtimeDrops, int traceDrops)
        {
            if (runtimeDrops < 0)
                runtimeDrops = 0;
            if (traceDrops < 0)
                traceDrops = 0;
            return runtimeDrops + traceDrops;
        }

        public static bool TryParsePrivacy(string token, out LoggingPrivacy privacy)
        {
            if (string.Equals(token, ProtocolKeywords.LoggingPrivacySafe, StringComparison.Ordinal))
            {
                privacy = LoggingPrivacy.Safe;
                return true;
            }
            if (string.Equals(token, ProtocolKeywords.LoggingPrivacyLocalPath, StringComparison.Ordinal))
            {
                privacy = LoggingPrivacy.LocalPath;
                return true;
            }
            if (string.Equals(token, ProtocolKeywords.LoggingPrivacyLocalUrl, StringComparison.Ordinal))
            {
                privacy = LoggingPrivacy.LocalUrl;
                return true;
            }
            if (string.Equals(token, ProtocolKeywords.LoggingPrivacyUserText, StringComparison.Ordinal))
            {
                privacy = LoggingPrivacy.UserText;
                return true;
            }
            if (string.Equals(token, ProtocolKeywords.LoggingPrivacySessionId, StringComparison.Ordinal))
            {
                privacy = LoggingPrivacy.SessionId;
                return true;
            }
            if (string.Equals(token, ProtocolKeywords.LoggingPrivacySecret, StringComparison.Ordinal))
            {
                privacy = LoggingPrivacy.Secret;
                return true;
            }

            privacy = LoggingPrivacy.Safe;
            return false;
        }

        public static string FormatPrivacy(LoggingPrivacy privacy)
        {
            switch (privacy)
            {
                case LoggingPrivacy.LocalPath:
                    return ProtocolKeywords.LoggingPrivacyLocalPath;
                case LoggingPrivacy.LocalUrl:
                    return ProtocolKeywords.LoggingPrivacyLocalUrl;
                case LoggingPrivacy.UserText:
                    return ProtocolKeywords.LoggingPrivacyUserText;
                case LoggingPrivacy.SessionId:
                    return ProtocolKeywords.LoggingPrivacySessionId;
                case LoggingPrivacy.Secret:
                    return ProtocolKeywords.LoggingPrivacySecret;
                default:
                    return ProtocolKeywords.LoggingPrivacySafe;
            }
        }


        public static LoggingObserved ApplySetIndependently(
            LoggingSetRequest request,
            string processSessionId,
            LoggingPersistenceHealth app,
            LoggingPersistenceHealth trace,
            LoggingPersistenceHealth crash,
            LoggingPersistenceHealth capture,
            int runtimeDrops,
            int traceDrops,
            LoggingFailureReason reason)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            return new LoggingObserved
            {
                RequestId = request.RequestId,
                ProcessSessionId = processSessionId,
                Diagnostics = request.Diagnostics,
                Capture = request.Capture,
                Trace = request.Trace,
                Persistence = WorstPersistence(app, trace, crash, capture),
                DropCount = CombineDropCount(runtimeDrops, traceDrops),
                Reason = reason
            };
        }

        private static bool TryReadCommand(string rawLine, out string command)
        {
            command = null;
            if (string.IsNullOrWhiteSpace(rawLine))
                return false;

            string trimmed = rawLine.Trim();
            int space = trimmed.IndexOf(' ');
            command = space < 0 ? trimmed : trimmed.Substring(0, space);
            return command.Length > 0;
        }

        private static bool TrySplitFields(string rawLine, int expectedCount, out string[] fields)
        {
            fields = null;
            if (string.IsNullOrWhiteSpace(rawLine))
                return false;

            string trimmed = rawLine.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
            string[] parts = trimmed.Split(' ');
            if (parts.Length != expectedCount)
                return false;

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                    return false;
            }

            fields = parts;
            return true;
        }

        private static bool TryParseDeterminateToggle(string token, out LoggingToggle toggle)
        {
            toggle = LoggingToggle.Off;
            if (string.Equals(token, ProtocolKeywords.LoggingOn, StringComparison.Ordinal))
            {
                toggle = LoggingToggle.On;
                return true;
            }

            if (string.Equals(token, ProtocolKeywords.LoggingOff, StringComparison.Ordinal))
                return true;

            return false;
        }

        private static bool TryParseObservedToggle(string token, out LoggingToggle toggle)
        {
            if (string.Equals(token, ProtocolKeywords.LoggingUnknown, StringComparison.Ordinal))
            {
                toggle = LoggingToggle.Unknown;
                return true;
            }

            return TryParseDeterminateToggle(token, out toggle);
        }

        private static bool TryParsePersistence(string token, out LoggingPersistenceHealth persistence)
        {
            if (string.Equals(token, ProtocolKeywords.LoggingHealthy, StringComparison.Ordinal))
            {
                persistence = LoggingPersistenceHealth.Healthy;
                return true;
            }

            if (string.Equals(token, ProtocolKeywords.LoggingDegraded, StringComparison.Ordinal))
            {
                persistence = LoggingPersistenceHealth.Degraded;
                return true;
            }

            if (string.Equals(token, ProtocolKeywords.LoggingUnavailable, StringComparison.Ordinal))
            {
                persistence = LoggingPersistenceHealth.Unavailable;
                return true;
            }

            persistence = LoggingPersistenceHealth.Unavailable;
            return false;
        }

        private static bool TryParseReason(string token, out LoggingFailureReason reason)
        {
            if (string.Equals(token, ProtocolKeywords.LoggingReasonApplied, StringComparison.Ordinal))
            {
                reason = LoggingFailureReason.Applied;
                return true;
            }

            if (string.Equals(token, ProtocolKeywords.LoggingReasonLegacyHelper, StringComparison.Ordinal))
            {
                reason = LoggingFailureReason.LegacyHelper;
                return true;
            }

            if (string.Equals(token, ProtocolKeywords.LoggingReasonCapabilityTimeout, StringComparison.Ordinal))
            {
                reason = LoggingFailureReason.CapabilityTimeout;
                return true;
            }

            if (string.Equals(token, ProtocolKeywords.LoggingReasonPathUnavailable, StringComparison.Ordinal))
            {
                reason = LoggingFailureReason.PathUnavailable;
                return true;
            }

            if (string.Equals(token, ProtocolKeywords.LoggingReasonWriterFault, StringComparison.Ordinal))
            {
                reason = LoggingFailureReason.WriterFault;
                return true;
            }

            if (string.Equals(token, ProtocolKeywords.LoggingReasonInvalidRequest, StringComparison.Ordinal))
            {
                reason = LoggingFailureReason.InvalidRequest;
                return true;
            }

            reason = LoggingFailureReason.InvalidRequest;
            return false;
        }

        private static bool TryParseDropCount(string token, out int dropCount)
        {
            dropCount = 0;
            if (string.IsNullOrEmpty(token))
                return false;

            for (int i = 0; i < token.Length; i++)
            {
                if (token[i] < '0' || token[i] > '9')
                    return false;
            }

            return int.TryParse(token, out dropCount);
        }

        private static string FormatDeterminateToggle(LoggingToggle toggle)
        {
            return toggle == LoggingToggle.On ? ProtocolKeywords.LoggingOn : ProtocolKeywords.LoggingOff;
        }

        private static string FormatObservedToggle(LoggingToggle toggle)
        {
            if (toggle == LoggingToggle.Unknown)
                return ProtocolKeywords.LoggingUnknown;
            return FormatDeterminateToggle(toggle);
        }

        private static string FormatPersistence(LoggingPersistenceHealth persistence)
        {
            if (persistence == LoggingPersistenceHealth.Healthy)
                return ProtocolKeywords.LoggingHealthy;
            if (persistence == LoggingPersistenceHealth.Degraded)
                return ProtocolKeywords.LoggingDegraded;
            return ProtocolKeywords.LoggingUnavailable;
        }

        private static string FormatDropCount(int dropCount)
        {
            if (dropCount < 0)
                dropCount = 0;
            return dropCount.ToString();
        }

        private static string FormatReason(LoggingFailureReason reason)
        {
            switch (reason)
            {
                case LoggingFailureReason.LegacyHelper:
                    return ProtocolKeywords.LoggingReasonLegacyHelper;
                case LoggingFailureReason.CapabilityTimeout:
                    return ProtocolKeywords.LoggingReasonCapabilityTimeout;
                case LoggingFailureReason.PathUnavailable:
                    return ProtocolKeywords.LoggingReasonPathUnavailable;
                case LoggingFailureReason.WriterFault:
                    return ProtocolKeywords.LoggingReasonWriterFault;
                case LoggingFailureReason.InvalidRequest:
                    return ProtocolKeywords.LoggingReasonInvalidRequest;
                default:
                    return ProtocolKeywords.LoggingReasonApplied;
            }
        }
    }
}
