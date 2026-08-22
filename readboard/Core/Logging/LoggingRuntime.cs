using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace readboard
{
    internal sealed class LoggingRuntimeOptions
    {
        public LaunchOptions LaunchOptions { get; set; }
        public ILoggingClock Clock { get; set; }
        public ILoggingFileSystem FileSystem { get; set; }
        public string LocalAppData { get; set; }
        public bool StartWorkers { get; set; }
        public Action Terminate { get; set; }
    }

    internal sealed class LoggingRuntime : IDisposable
    {
        private readonly object sync = new object();
        private readonly object tailSync = new object();
        private readonly LaunchOptions launchOptions;
        private readonly ILoggingClock clock;
        private readonly ILoggingFileSystem fileSystem;
        private readonly LoggingRecord[] tail = new LoggingRecord[LoggingLimits.CrashTailSize];
        private readonly BoundedLogQueue appQueue = new BoundedLogQueue();
        private readonly BoundedLogQueue traceQueue = new BoundedLogQueue();
        private readonly ManualResetEventSlim pulse = new ManualResetEventSlim(false);
        private readonly LoggerFactory loggerFactory;
        private readonly ILogger logger;
        private RollingFileSink appSink;
        private RollingFileSink traceSink;
        private RollingFileSink crashSink;
        private LoggingPathResolution pathResolution;
        private string logRoot;
        private LoggingToggle diagnostics;
        private LoggingToggle capture;
        private LoggingToggle trace;
        private LoggingPersistenceHealth captureHealth = LoggingPersistenceHealth.Healthy;
        private LoggingFailureReason launchReason;
        private Action terminate;
        private Thread appWorker;
        private Thread traceWorker;
        private int running;
        private int tailIndex;
        private int tailCount;
        private bool hooksInstalled;
        private bool disposed;

        private LoggingRuntime(LoggingRuntimeOptions options)
        {
            launchOptions = options.LaunchOptions;
            clock = options.Clock ?? new SystemLoggingClock();
            fileSystem = options.FileSystem ?? new RealLoggingFileSystem();
            terminate = options.Terminate;
            diagnostics = launchOptions.DiagnosticsEnabled == true ? LoggingToggle.On : LoggingToggle.Off;
            capture = launchOptions.CaptureEnabled == true ? LoggingToggle.On : LoggingToggle.Off;
            trace = LoggingToggle.Off;

            pathResolution = LoggingPathResolver.Resolve(launchOptions, options.LocalAppData);
            launchReason = pathResolution.InitialReason;
            InitializePersistence();

            loggerFactory = new LoggerFactory(new ILoggerProvider[] { new ReadBoardLoggerProvider(this) });
            logger = loggerFactory.CreateLogger("logging");

            if (options.StartWorkers)
                StartWorkers();
        }

        public ILoggerFactory LoggerFactory
        {
            get { return loggerFactory; }
        }

        public ILogger Logger
        {
            get { return logger; }
        }

        public string ProcessSessionId
        {
            get { return launchOptions == null ? null : launchOptions.ProcessSessionId; }
        }

        public string HostSessionId
        {
            get { return launchOptions == null ? null : launchOptions.HostSessionId; }
        }

        public string LogRoot
        {
            get { return logRoot; }
        }

        public string CaptureDirectory
        {
            get { return BoardDebugDiagnosticsPaths.GetCaptureDirectory(logRoot); }
        }

        public static LoggingRuntime Start(LaunchOptions options)
        {
            return Start(new LoggingRuntimeOptions
            {
                LaunchOptions = options,
                Clock = new SystemLoggingClock(),
                FileSystem = new RealLoggingFileSystem(),
                LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                StartWorkers = true
            });
        }

        internal static LoggingRuntime Start(LoggingRuntimeOptions options)
        {
            if (options == null || options.LaunchOptions == null)
                return CreateUnavailable(options);

            try
            {
                return new LoggingRuntime(options);
            }
            catch
            {
                return CreateUnavailable(options);
            }
        }

        public ILogger CreateLogger(string module)
        {
            return loggerFactory.CreateLogger(string.IsNullOrEmpty(module) ? "runtime" : module);
        }

        public BoardDebugDiagnosticsWriter CreateCaptureWriter(Func<bool> legacyEnabled)
        {
            if (legacyEnabled == null)
                throw new ArgumentNullException("legacyEnabled");

            LoggingLaunchKind kind = launchOptions == null
                ? LoggingLaunchKind.Unavailable
                : launchOptions.LoggingKind;
            return new BoardDebugDiagnosticsWriter(new BoardDebugDiagnosticsWriterOptions
            {
                RootDirectory = CaptureDirectory,
                IsEnabled = delegate
                {
                    if (kind == LoggingLaunchKind.Legacy)
                        return legacyEnabled();
                    lock (sync)
                        return capture == LoggingToggle.On;
                },
                Clock = clock,
                FileSystem = fileSystem,
                ReportHealth = SetCaptureHealth
            });
        }

        public void SetCaptureHealth(LoggingPersistenceHealth health)
        {
            lock (sync)
                captureHealth = health;
        }


        public LoggingObservedSnapshot Observe()
        {
            lock (sync)
            {
                LoggingPersistenceHealth appHealth = appSink == null
                    ? LoggingPersistenceHealth.Unavailable
                    : appSink.Health;
                LoggingPersistenceHealth traceHealth = traceSink == null
                    ? LoggingPersistenceHealth.Unavailable
                    : traceSink.Health;
                LoggingPersistenceHealth crashHealth = crashSink == null
                    ? LoggingPersistenceHealth.Unavailable
                    : crashSink.Health;
                LoggingPersistenceHealth persistence = LoggingWireContract.WorstPersistence(
                    appHealth,
                    traceHealth,
                    crashHealth,
                    captureHealth);
                return new LoggingObservedSnapshot
                {
                    ProcessSessionId = ProcessSessionId,
                    HostSessionId = HostSessionId,
                    LogRoot = logRoot,
                    Diagnostics = diagnostics,
                    Capture = capture,
                    Trace = trace,
                    AppHealth = appHealth,
                    TraceHealth = traceHealth,
                    CrashHealth = crashHealth,
                    CaptureHealth = captureHealth,
                    Persistence = persistence,
                    RuntimeDropCount = appQueue.DropCount,
                    TraceDropCount = traceQueue.DropCount,
                    DropCount = LoggingWireContract.CombineDropCount(appQueue.DropCount, traceQueue.DropCount),
                    Reason = ResolveReason(persistence)
                };
            }
        }

        public LoggingObserved ApplySet(LoggingSetRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            lock (sync)
            {
                if (request.Diagnostics != LoggingToggle.Unknown)
                    diagnostics = request.Diagnostics;
                if (request.Capture != LoggingToggle.Unknown)
                    capture = request.Capture;
                if (request.Trace != LoggingToggle.Unknown)
                    trace = request.Trace;
            }

            return LoggingWireContract.ToObserved(request.RequestId, Observe());
        }

        public void Write(LoggingRecord record)
        {
            if (record == null || disposed)
                return;

            try
            {
                if (LoggingJsonlSerializer.ContainsSecret(record))
                    return;

                record.ProcessSessionId = ProcessSessionId;
                if (record.HostSessionId == null)
                    record.HostSessionId = HostSessionId;
                if (record.TimestampUtc == default(DateTime))
                    record.TimestampUtc = clock.UtcNow;
                if (string.IsNullOrEmpty(record.Stream))
                    record.Stream = record.Level <= LogLevel.Debug ? LoggingStreams.Trace : LoggingStreams.App;

                bool traceEnabled;
                bool diagnosticsEnabled;
                lock (sync)
                {
                    traceEnabled = trace == LoggingToggle.On;
                    diagnosticsEnabled = diagnostics == LoggingToggle.On;
                }

                if (record.DiagnosticOnly && !diagnosticsEnabled)
                    return;
                if (string.Equals(record.Stream, LoggingStreams.Trace, StringComparison.Ordinal)
                    && !traceEnabled)
                {
                    return;
                }

                if (string.Equals(record.Stream, LoggingStreams.App, StringComparison.Ordinal)
                    || string.Equals(record.Stream, LoggingStreams.Trace, StringComparison.Ordinal))
                {
                    Remember(record);
                }

                if (string.Equals(record.Stream, LoggingStreams.Crash, StringComparison.Ordinal))
                {
                    PersistCrash(record);
                    return;
                }
                if (string.Equals(record.Stream, LoggingStreams.Trace, StringComparison.Ordinal))
                {
                    traceQueue.TryEnqueue(record);
                    pulse.Set();
                    return;
                }

                appQueue.TryEnqueue(record);
                pulse.Set();
            }
            catch
            {
            }
        }

        public void WriteSemantic(SemanticMessage message, string module)
        {
            if (message == null)
                return;

            LoggingRecord record = new LoggingRecord();
            record.TimestampUtc = clock.UtcNow;
            record.Level = ParseSemanticLevel(message.Level);
            record.Stream = record.Level <= LogLevel.Debug ? LoggingStreams.Trace : LoggingStreams.App;
            record.EventId = message.Key;
            record.Module = module;
            record.SemanticKey = message.Key;
            record.SemanticArgs = new Dictionary<string, LoggingField>();
            IReadOnlyList<object> arguments = message.Arguments;
            if (arguments != null)
            {
                for (int i = 0; i < arguments.Count; i++)
                {
                    record.SemanticArgs[i.ToString(CultureInfo.InvariantCulture)] = LoggingField.Safe(arguments[i]);
                }
            }
            if (!string.IsNullOrEmpty(message.DiagnosticDetail))
            {
                record.Fields = new Dictionary<string, LoggingField>();
                record.Fields["diagnosticDetail"] = LoggingField.Tagged(
                    LoggingSanitizer.SanitizeText(message.DiagnosticDetail, LoggingLimits.MaxFieldChars),
                    LoggingPrivacy.UserText);
            }
            Write(record);
        }

        public void WriteDiagnostic(LoggingRecord record)
        {
            if (record == null)
                return;
            record.DiagnosticOnly = true;
            if (string.IsNullOrEmpty(record.Stream))
                record.Stream = LoggingStreams.App;
            Write(record);
        }

        public void RecordCrash(Exception exception, string kind)
        {
            LoggingRecord record = new LoggingRecord();
            record.TimestampUtc = clock.UtcNow;
            record.Level = LogLevel.Critical;
            record.Stream = LoggingStreams.Crash;
            record.EventId = "runtime.unhandled";
            record.Module = "crash";
            record.Exception = exception;
            record.Fields = new Dictionary<string, LoggingField>();
            record.Fields["kind"] = LoggingField.Safe(kind ?? "unhandled");
            record.CrashTail = SnapshotTail();
            PersistCrash(record);
        }

        public void InstallCrashHandlers()
        {
            InstallCrashHandlers(terminate);
        }

        internal void InstallCrashHandlers(Action terminateAction)
        {
            terminate = terminateAction ?? terminate ?? new Action(TerminateProcess);
            if (hooksInstalled)
                return;
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            }
            catch
            {
            }
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            hooksInstalled = true;
        }

        internal void HandleThreadException(Exception exception)
        {
            RecordCrash(exception, "thread");
            RequestTerminate();
        }

        internal void HandleUnhandledException(Exception exception, bool isTerminating)
        {
            RecordCrash(exception, "unhandled");
            if (isTerminating)
                RequestTerminate();
        }

        internal void HandleUnobservedTaskException(Exception exception)
        {
            RecordCrash(exception, "unobserved");
        }

        public void Drain()
        {
            if (appWorker == null && traceWorker == null)
            {
                Pump(appQueue, appSink);
                Pump(traceQueue, traceSink);
                return;
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline && (appQueue.Count > 0 || traceQueue.Count > 0))
                pulse.Set();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Volatile.Write(ref running, 0);
            pulse.Set();
            bool appJoined = appWorker == null || appWorker.Join(1000);
            bool traceJoined = traceWorker == null || traceWorker.Join(1000);
            if (appJoined)
                Pump(appQueue, appSink);
            if (traceJoined)
                Pump(traceQueue, traceSink);
            UninstallCrashHandlers();
            loggerFactory.Dispose();
            pulse.Dispose();
        }

        private static LoggingRuntime CreateUnavailable(LoggingRuntimeOptions options)
        {
            LaunchOptions launch = options == null ? null : options.LaunchOptions;
            if (launch == null)
            {
                launch = new LaunchOptions
                {
                    LoggingKind = LoggingLaunchKind.Unavailable,
                    ProcessSessionId = null
                };
            }

            LoggingRuntimeOptions safe = new LoggingRuntimeOptions
            {
                LaunchOptions = launch,
                Clock = options == null || options.Clock == null ? new SystemLoggingClock() : options.Clock,
                FileSystem = options == null || options.FileSystem == null
                    ? new RealLoggingFileSystem()
                    : options.FileSystem,
                LocalAppData = options == null ? null : options.LocalAppData,
                StartWorkers = false,
                Terminate = options == null ? null : options.Terminate
            };
            return new LoggingRuntime(safe);
        }

        private void InitializePersistence()
        {
            bool canPersist = pathResolution != null
                && pathResolution.AllowsPersistence
                && !string.IsNullOrWhiteSpace(pathResolution.CandidateRoot)
                && fileSystem.TryCreateDirectory(pathResolution.CandidateRoot);
            if (canPersist)
            {
                logRoot = pathResolution.CandidateRoot;
                LoggingPersistenceHealth healthy = LoggingPersistenceHealth.Healthy;
                appSink = new RollingFileSink(LoggingStreams.App, logRoot, clock, fileSystem, healthy);
                traceSink = new RollingFileSink(LoggingStreams.Trace, logRoot, clock, fileSystem, healthy);
                crashSink = new RollingFileSink(LoggingStreams.Crash, logRoot, clock, fileSystem, healthy);
                appSink.Cleanup();
                traceSink.Cleanup();
                crashSink.Cleanup();
                return;
            }

            logRoot = null;
            captureHealth = LoggingPersistenceHealth.Unavailable;
            LoggingPersistenceHealth unavailable = LoggingPersistenceHealth.Unavailable;
            appSink = new RollingFileSink(LoggingStreams.App, null, clock, fileSystem, unavailable);
            traceSink = new RollingFileSink(LoggingStreams.Trace, null, clock, fileSystem, unavailable);
            crashSink = new RollingFileSink(LoggingStreams.Crash, null, clock, fileSystem, unavailable);
            if (pathResolution != null
                && pathResolution.AllowsPersistence
                && launchReason == LoggingFailureReason.Applied)
            {
                launchReason = LoggingFailureReason.PathUnavailable;
            }
        }

        private void StartWorkers()
        {
            Volatile.Write(ref running, 1);
            appWorker = new Thread(AppWorkerLoop);
            appWorker.IsBackground = true;
            appWorker.Name = "readboard-app-log";
            appWorker.Start();
            traceWorker = new Thread(TraceWorkerLoop);
            traceWorker.IsBackground = true;
            traceWorker.Name = "readboard-trace-log";
            traceWorker.Start();
        }

        private void AppWorkerLoop()
        {
            WorkerLoop(appQueue, appSink);
        }

        private void TraceWorkerLoop()
        {
            WorkerLoop(traceQueue, traceSink);
        }

        private void WorkerLoop(BoundedLogQueue queue, RollingFileSink sink)
        {
            while (Volatile.Read(ref running) != 0)
            {
                if (!PumpOnce(queue, sink))
                {
                    pulse.Wait(100);
                    pulse.Reset();
                }
            }
            Pump(queue, sink);
        }

        private void Pump(BoundedLogQueue queue, RollingFileSink sink)
        {
            while (PumpOnce(queue, sink))
            {
            }
        }

        private bool PumpOnce(BoundedLogQueue queue, RollingFileSink sink)
        {
            LoggingRecord record;
            if (queue == null || !queue.TryDequeue(out record))
                return false;
            Persist(sink, record);
            return true;
        }

        private void Persist(RollingFileSink sink, LoggingRecord record)
        {
            try
            {
                string line;
                if (sink == null || !LoggingJsonlSerializer.TrySerialize(record, out line))
                    return;
                sink.TryWriteLine(line);
            }
            catch
            {
            }
        }

        private void PersistCrash(LoggingRecord record)
        {
            try
            {
                string line;
                if (!LoggingJsonlSerializer.TrySerialize(record, out line))
                    return;
                RollingFileSink sink;
                lock (sync)
                    sink = crashSink;
                if (sink != null)
                    sink.TryWriteLine(line);
            }
            catch
            {
            }
        }

        private void Remember(LoggingRecord record)
        {
            lock (tailSync)
            {
                tail[tailIndex] = CloneForTail(record);
                tailIndex = (tailIndex + 1) % tail.Length;
                if (tailCount < tail.Length)
                    tailCount++;
            }
        }

        private IList<LoggingRecord> SnapshotTail()
        {
            lock (tailSync)
            {
                List<LoggingRecord> snapshot = new List<LoggingRecord>(tailCount);
                int start = tailCount == tail.Length ? tailIndex : 0;
                for (int i = 0; i < tailCount; i++)
                {
                    LoggingRecord item = tail[(start + i) % tail.Length];
                    if (item != null)
                        snapshot.Add(item);
                }
                return snapshot;
            }
        }

        private static LoggingRecord CloneForTail(LoggingRecord record)
        {
            return new LoggingRecord
            {
                TimestampUtc = record.TimestampUtc,
                Level = record.Level,
                Stream = record.Stream,
                EventId = record.EventId,
                Module = record.Module,
                SemanticKey = record.SemanticKey
            };
        }

        private LoggingFailureReason ResolveReason(LoggingPersistenceHealth persistence)
        {
            if (launchOptions != null && launchOptions.LoggingKind == LoggingLaunchKind.Legacy)
                return LoggingFailureReason.LegacyHelper;
            if (launchReason == LoggingFailureReason.InvalidRequest)
                return LoggingFailureReason.InvalidRequest;
            if (launchReason == LoggingFailureReason.PathUnavailable)
                return LoggingFailureReason.PathUnavailable;
            if (persistence == LoggingPersistenceHealth.Degraded
                || (appSink != null && appSink.HasWriterFault)
                || (traceSink != null && traceSink.HasWriterFault)
                || (crashSink != null && crashSink.HasWriterFault))
            {
                return LoggingFailureReason.WriterFault;
            }
            if (persistence == LoggingPersistenceHealth.Unavailable)
                return LoggingFailureReason.PathUnavailable;
            return LoggingFailureReason.Applied;
        }

        private static LogLevel ParseSemanticLevel(string level)
        {
            if (string.Equals(level, "TRACE", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Trace;
            if (string.Equals(level, "DEBUG", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Debug;
            if (string.Equals(level, "WARN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(level, "WARNING", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevel.Warning;
            }
            if (string.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Error;
            if (string.Equals(level, "CRITICAL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(level, "FATAL", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevel.Critical;
            }
            return LogLevel.Information;
        }

        private void OnThreadException(object sender, ThreadExceptionEventArgs args)
        {
            HandleThreadException(args == null ? null : args.Exception);
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            Exception exception = args == null ? null : args.ExceptionObject as Exception;
            bool terminating = args == null || args.IsTerminating;
            HandleUnhandledException(exception, terminating);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
        {
            Exception exception = args == null ? null : (Exception)args.Exception;
            HandleUnobservedTaskException(exception);
        }

        private void UninstallCrashHandlers()
        {
            if (!hooksInstalled)
                return;
            Application.ThreadException -= OnThreadException;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            hooksInstalled = false;
        }

        private void RequestTerminate()
        {
            Action action = terminate;
            if (action != null)
                action();
        }

        private static void TerminateProcess()
        {
            Environment.Exit(1);
        }
    }

    internal sealed class ReadBoardLoggerProvider : ILoggerProvider
    {
        private readonly LoggingRuntime runtime;

        public ReadBoardLoggerProvider(LoggingRuntime runtime)
        {
            this.runtime = runtime;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ReadBoardLogger(runtime, categoryName);
        }

        public void Dispose()
        {
        }
    }

    internal sealed class ReadBoardLogger : ILogger
    {
        private readonly LoggingRuntime runtime;
        private readonly string module;

        public ReadBoardLogger(LoggingRuntime runtime, string module)
        {
            this.runtime = runtime;
            this.module = module;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None)
                return false;
            if (logLevel <= LogLevel.Debug)
            {
                LoggingObservedSnapshot snapshot = runtime.Observe();
                return snapshot.Trace == LoggingToggle.On;
            }
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            string name = eventId.Name;
            if (string.IsNullOrEmpty(name) && eventId.Id != 0)
                name = "runtime.event." + eventId.Id.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(name))
                name = TryOriginalFormat(state);
            if (string.IsNullOrEmpty(name) || name.IndexOf(' ') >= 0 || name.IndexOf('{') >= 0)
                name = "runtime.log";

            LoggingRecord record = new LoggingRecord();
            record.Level = logLevel;
            record.Stream = logLevel <= LogLevel.Debug ? LoggingStreams.Trace : LoggingStreams.App;
            record.EventId = name;
            record.Module = module;
            record.Exception = exception;
            record.SemanticKey = name;
            runtime.Write(record);
        }

        private static string TryOriginalFormat<TState>(TState state)
        {
            IReadOnlyList<KeyValuePair<string, object>> values = state as IReadOnlyList<KeyValuePair<string, object>>;
            if (values == null)
                return state as string;
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i].Key, "{OriginalFormat}", StringComparison.Ordinal))
                    return values[i].Value as string;
            }
            return null;
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();

            public void Dispose()
            {
            }
        }
    }

    internal static partial class ReadBoardLogMessages
    {
        [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "logging.runtime.started")]
        public static partial void RuntimeStarted(ILogger logger);
    }
}
