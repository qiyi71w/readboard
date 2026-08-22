using System;
using System.IO;

namespace readboard
{
    internal static class LoggingLimits
    {
        public const int QueueCapacity = 4096;
        public const int CrashTailSize = 256;
        public const long RollBytes = 10L * 1024L * 1024L;
        public const int RetentionDays = 7;
        public const long ClassTotalBytes = 100L * 1024L * 1024L;
        public const int MaxExceptionChars = 2048;
        public const int MaxFieldChars = 1024;
        public const long CaptureMaxPngBytes = 32L * 1024L * 1024L;
        public const long CaptureClassTotalBytes = 500L * 1024L * 1024L;
        public const int CaptureRetentionDays = 7;
    }

    internal static class LoggingStreams
    {
        public const string App = "app";
        public const string Trace = "trace";
        public const string Crash = "crash";
        public const string ArchiveDirectoryName = "archive";
        public const string CaptureDirectoryName = "capture";
    }

    internal sealed class LoggingPathResolution
    {
        public string CandidateRoot { get; set; }
        public bool UseLegacyFallback { get; set; }
        public bool AllowsPersistence { get; set; }
        public LoggingFailureReason InitialReason { get; set; }
    }

    internal static class LoggingPathResolver
    {
        public const string LegacyProductFolder = "LizzieYzyNext";
        public const string LegacyAppFolder = "ReadBoard";
        public const string LegacyLogsFolder = "logs";

        public static string GetLegacyRoot(string localAppData)
        {
            if (string.IsNullOrWhiteSpace(localAppData))
                return null;
            return Path.Combine(localAppData, LegacyProductFolder, LegacyAppFolder, LegacyLogsFolder);
        }

        public static LoggingPathResolution Resolve(LaunchOptions options, string localAppData)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            if (options.LoggingKind == LoggingLaunchKind.Legacy)
            {
                string legacyRoot = GetLegacyRoot(localAppData);
                return new LoggingPathResolution
                {
                    CandidateRoot = legacyRoot,
                    UseLegacyFallback = true,
                    AllowsPersistence = !string.IsNullOrWhiteSpace(legacyRoot),
                    InitialReason = LoggingFailureReason.LegacyHelper
                };
            }

            if (options.LoggingKind == LoggingLaunchKind.Contract
                && LaunchOptions.IsAbsoluteLogDirectory(options.LogDirectory))
            {
                return new LoggingPathResolution
                {
                    CandidateRoot = options.LogDirectory,
                    UseLegacyFallback = false,
                    AllowsPersistence = true,
                    InitialReason = LoggingFailureReason.Applied
                };
            }

            return new LoggingPathResolution
            {
                CandidateRoot = null,
                UseLegacyFallback = false,
                AllowsPersistence = false,
                InitialReason = LoggingFailureReason.InvalidRequest
            };
        }
    }
}
