using System;
using System.Collections.Generic;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Logging
{
    internal static class LoggingHarness
    {
        public const string ProcessSessionId = "dGVzdFByb2Nlc3NJRA";
        public const string HostSessionId = "dGVzdEhvc3RTZXNzaW9u";
        public const string LocalAppData = @"C:\legacy-appdata";
        public const string ContractRoot = @"C:\work\logs\readboard";

        public static LaunchOptions Parse(string processSessionId, params string[] extra)
        {
            List<string> args = new List<string> { "yzy", "10", "20", "policy", "0", "cn", "-1" };
            if (extra != null)
                args.AddRange(extra);
            LaunchOptions options;
            Assert.True(LaunchOptions.TryParse(args.ToArray(), () => processSessionId, out options));
            return options;
        }

        public static LaunchOptions Contract(
            bool diagnostics = false,
            bool capture = false,
            string processSessionId = ProcessSessionId)
        {
            return Parse(
                processSessionId,
                "--log-dir",
                ContractRoot,
                "--host-session-id",
                HostSessionId,
                "--logging-contract",
                "1",
                "--diagnostics",
                diagnostics ? "on" : "off",
                "--capture",
                capture ? "on" : "off");
        }

        public static LaunchOptions Legacy(string processSessionId = ProcessSessionId)
        {
            return Parse(processSessionId);
        }

        public static LoggingRuntime Start(
            LaunchOptions options,
            MemoryLoggingFileSystem fileSystem,
            FakeLoggingClock clock = null,
            bool startWorkers = false,
            Action terminate = null)
        {
            return LoggingRuntime.Start(new LoggingRuntimeOptions
            {
                LaunchOptions = options,
                FileSystem = fileSystem,
                Clock = clock ?? new FakeLoggingClock(new DateTime(2026, 8, 21, 17, 3, 0, DateTimeKind.Utc)),
                LocalAppData = LocalAppData,
                StartWorkers = startWorkers,
                Terminate = terminate
            });
        }
    }
}
