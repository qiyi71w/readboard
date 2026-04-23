using System;
using System.Diagnostics;
using System.IO;
using Readboard.VerificationTests;
using Readboard.VerificationTests.Protocol;
using Readboard.VerificationTests.Recognition;
using readboard;

namespace Readboard.ProtocolConfigBenchmarks
{
    internal static class Program
    {
        private const int ProtocolIterations = 50000;
        private const int ConfigIterations = 200;
        private const string ProtocolVersion = "220430";
        private const string MachineKey = "MACHINE-001";
        private const string MainFileName = "config_readboard.txt";
        private const string OtherFileName = "config_readboard_others.txt";

        private static int Main()
        {
            BenchmarkResult protocolResult = BenchmarkProtocolParsing();
            BenchmarkResult configResult = BenchmarkLegacyConfigLoad();
            RecognitionPerformanceAcceptanceReport recognitionAcceptance =
                RecognitionPerformanceAcceptanceHarness.MeasureDefaultAcceptance();
            SustainedSyncAcceptanceReport sustainedSyncAcceptance =
                SustainedSyncAcceptanceHarness.MeasureDefaultAcceptance();
            int exitCode = DetermineExitCode(recognitionAcceptance, sustainedSyncAcceptance);

            PrintBenchmark(protocolResult);
            PrintBenchmark(configResult);
            PrintRecognitionAcceptance(recognitionAcceptance);
            PrintSustainedSyncAcceptance(sustainedSyncAcceptance);
            PrintOverallAcceptance(exitCode == 0);
            return exitCode;
        }

        private static BenchmarkResult BenchmarkProtocolParsing()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();
            var fixtureCases = ProtocolFixtureCatalog.LoadInboundCases();
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int index = 0; index < ProtocolIterations; index++)
            {
                foreach (ProtocolFixtureCase fixtureCase in fixtureCases)
                    adapter.ParseInbound(fixtureCase.RawLine);
            }

            stopwatch.Stop();
            return BenchmarkResult.Create("protocol-parse", ProtocolIterations, stopwatch.Elapsed);
        }

        private static BenchmarkResult BenchmarkLegacyConfigLoad()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int index = 0; index < ConfigIterations; index++)
            {
                using (BenchmarkWorkspace workspace = BenchmarkWorkspace.Create())
                {
                    workspace.CopyLegacyFixtures();
                    DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, MachineKey, ProtocolVersion);
                    store.Load();
                }
            }

            stopwatch.Stop();
            return BenchmarkResult.Create("config-load", ConfigIterations, stopwatch.Elapsed);
        }

        private static void PrintBenchmark(BenchmarkResult result)
        {
            Console.WriteLine("Benchmark: " + result.Name);
            Console.WriteLine("  Iterations: " + result.Iterations);
            Console.WriteLine("  ElapsedMs: " + result.Elapsed.TotalMilliseconds.ToString("F2"));
            Console.WriteLine("  OpsPerSec: " + result.OpsPerSecond.ToString("F2"));
        }

        private static void PrintRecognitionAcceptance(RecognitionPerformanceAcceptanceReport report)
        {
            Console.WriteLine("Recognition Acceptance: " + report.Scenario.Name);
            Console.WriteLine("  StepsPerPass: " + report.Scenario.Steps.Length);
            Console.WriteLine("  CachedAvgMs: " + report.Cached.AverageElapsedMilliseconds.ToString("F6"));
            Console.WriteLine("  EquivalentUncachedBaselineAvgMs: " + report.EquivalentUncachedBaseline.AverageElapsedMilliseconds.ToString("F6"));
            Console.WriteLine("  LatencyReduction: " + report.LatencyReductionRatio.ToString("P2"));
            Console.WriteLine("  CachedAvgAllocatedBytes: " + report.Cached.AverageAllocatedBytes.ToString("F2"));
            Console.WriteLine("  EquivalentUncachedBaselineAvgAllocatedBytes: " + report.EquivalentUncachedBaseline.AverageAllocatedBytes.ToString("F2"));
            Console.WriteLine("  AllocationReduction: " + report.AllocationReductionRatio.ToString("P2"));
            Console.WriteLine("  CachedSnapshotCount: " + report.Cached.CachedSnapshotCount);
            Console.WriteLine("  Thresholds: latency>=" + RecognitionPerformanceAcceptanceHarness.MinimumLatencyReductionRatio.ToString("P0")
                + ", allocation>=" + RecognitionPerformanceAcceptanceHarness.MinimumAllocationReductionRatio.ToString("P0")
                + ", expected/baseline mismatches=0");
            Console.WriteLine("  AccuracyMismatches: cached-vs-expected=" + report.Parity.CachedExpectedMismatchCount
                + ", baseline-vs-expected=" + report.Parity.BaselineExpectedMismatchCount
                + ", cached-vs-baseline=" + report.Parity.CachedVsBaselineMismatchCount);
            Console.WriteLine("  RecognitionAcceptance: " + (DetermineRecognitionExitCode(report) == 0 ? "PASS" : "FAIL"));
        }

        private static void PrintSustainedSyncAcceptance(SustainedSyncAcceptanceReport report)
        {
            Console.WriteLine("Sustained Sync Acceptance: 200ms coordinator orchestration");
            Console.WriteLine("  WorkerTicks: " + report.WorkerTickCount + "/" + report.ExpectedWorkerTickCount);
            Console.WriteLine("  CaptureCalls: " + report.CaptureCallCount);
            Console.WriteLine("  RecognitionCalls: " + report.RecognitionCallCount);
            Console.WriteLine("  WindowLocatorCalls: " + report.WindowLocatorCallCount);
            Console.WriteLine("  OutboundLines: " + report.OutboundLineCount + "/" + report.ExpectedOutboundLineCount);
            Console.WriteLine("  OverBudgetTicks: " + report.OverBudgetTickCount);
            Console.WriteLine("  MaxTickMs: " + report.MaxTickElapsed.TotalMilliseconds.ToString("F3"));
            Console.WriteLine("  SustainedSyncAcceptance: " + (report.MeetsAcceptance ? "PASS" : "FAIL"));
        }

        private static void PrintOverallAcceptance(bool accepted)
        {
            Console.WriteLine("Acceptance: " + (accepted ? "PASS" : "FAIL"));
        }

        private static int DetermineRecognitionExitCode(RecognitionPerformanceAcceptanceReport report)
        {
            return report.MeetsAllocationAcceptance
                && report.MeetsAccuracyAcceptance
                ? 0
                : 1;
        }

        private static int DetermineExitCode(
            RecognitionPerformanceAcceptanceReport recognitionAcceptance,
            SustainedSyncAcceptanceReport sustainedSyncAcceptance)
        {
            return DetermineRecognitionExitCode(recognitionAcceptance) == 0
                && sustainedSyncAcceptance.MeetsAcceptance
                ? 0
                : 1;
        }

        private sealed class BenchmarkWorkspace : IDisposable
        {
            private BenchmarkWorkspace(string rootPath)
            {
                RootPath = rootPath;
            }

            public string RootPath { get; private set; }

            public static BenchmarkWorkspace Create()
            {
                string rootPath = Path.Combine(
                    Path.GetTempPath(),
                    "readboard-benchmark-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                return new BenchmarkWorkspace(rootPath);
            }

            public void CopyLegacyFixtures()
            {
                File.Copy(
                    VerificationFixtureLocator.FixturePath(Path.Combine("config", "legacy", MainFileName)),
                    Path.Combine(RootPath, MainFileName));

                File.Copy(
                    VerificationFixtureLocator.FixturePath(Path.Combine("config", "legacy", OtherFileName)),
                    Path.Combine(RootPath, OtherFileName));
            }

            public void Dispose()
            {
                if (Directory.Exists(RootPath))
                    Directory.Delete(RootPath, recursive: true);
            }
        }

        private sealed class BenchmarkResult
        {
            private BenchmarkResult(string name, int iterations, TimeSpan elapsed)
            {
                Name = name;
                Iterations = iterations;
                Elapsed = elapsed;
            }

            public string Name { get; private set; }
            public int Iterations { get; private set; }
            public TimeSpan Elapsed { get; private set; }

            public double OpsPerSecond
            {
                get
                {
                    return Elapsed.TotalSeconds <= 0
                        ? 0
                        : Iterations / Elapsed.TotalSeconds;
                }
            }

            public static BenchmarkResult Create(string name, int iterations, TimeSpan elapsed)
            {
                return new BenchmarkResult(name, iterations, elapsed);
            }
        }
    }
}
