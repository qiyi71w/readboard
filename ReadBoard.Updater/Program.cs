using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using readboard;

namespace ReadBoard.Updater
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 1 && args[0] == "--probe")
            {
                using var probe = new UpdateWindow("cn");
                probe.CreateControl();
                return;
            }
            bool recovery = args.Length == 0 || (args.Length == 2 && args[0] == "--recover");
            if (args.Length == 0) args = new[] { "--recover", Path.Combine(AppContext.BaseDirectory, "..", "request.json") };
            if (args.Length != 2 || (args[0] != "--request" && args[0] != "--recover")) { Environment.ExitCode = 1; return; }
            IndependentUpdateRequest request;
            try
            {
                request = IndependentUpdateFiles.Read<IndependentUpdateRequest>(args[1]);
                IndependentUpdateFiles.ValidateRequest(request);
                if (IndependentUpdateFiles.IsWithin(request.InstallDirectory, AppContext.BaseDirectory))
                    throw new InvalidDataException("The updater must run from its retained external directory.");
            }
            catch (Exception exception)
            {
                Environment.ExitCode = 1;
                using var failure = new UpdateWindow("cn");
                failure.Shown += (_, _) => failure.Finish(new IndependentUpdateResult
                {
                    State = "rollbackFailed", Diagnostic = exception.Message,
                    BackupDirectory = Path.GetDirectoryName(Path.GetFullPath(args[1]))
                });
                Application.Run(failure);
                return;
            }
            using var window = new UpdateWindow(request.Language);
            window.Shown += (_, _) =>
            {
                window.Hide();
                var thread = new Thread(() => Execute(args[1], request, window, recovery)) { IsBackground = false };
                thread.Start();
            };
            Application.Run(window);
        }

        private static void Execute(string requestPath, IndependentUpdateRequest request, UpdateWindow window, bool recovery)
        {
            bool committed = recovery;
            using var pipe = new NamedPipeClientStream(".", "ReadBoard.Update." + request.OperationId,
                PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            try
            {
                if (recovery)
                {
                    window.Post(() => { window.Show(); window.SetStatus("IndependentUpdate_installing", RecoveryLocation(requestPath)); });
                    if (!File.Exists(Path.Combine(Path.GetDirectoryName(requestPath), "progress.json")) &&
                        !File.Exists(Path.Combine(Path.GetDirectoryName(requestPath), "preparation.json")))
                        throw new InvalidDataException("No durable transaction exists to recover.");
                    var recovered = new IndependentUpdateInstaller().Execute(requestPath,
                        () => throw new InvalidOperationException("Recovery cannot submit another installation."), () => { }, _ => { });
                    CompleteRestart(requestPath, request, recovered, window);
                    return;
                }
                pipe.Connect(15000);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                IndependentUpdateResult result = new IndependentUpdateInstaller().Execute(requestPath,
                    () =>
                    {
                        writer.WriteLine("PREPARED " + request.OperationId);
                        // Source closes the channel when preparation is cancelled or fails.
                        return reader.ReadLine() == "COMMIT " + request.OperationId;
                    },
                    () =>
                    {
                        committed = true;
                        window.Post(() => { window.Show(); window.SetStatus("IndependentUpdate_installing", RecoveryLocation(requestPath)); });
                        writer.WriteLine("ACCEPTED " + request.OperationId);
                    },
                    phase => { if (committed) window.Post(() => window.SetStatus("IndependentUpdate_installing", RecoveryLocation(requestPath))); });
                if (!committed)
                {
                    writer.WriteLine("ERROR " + JsonSerializer.Serialize(result.Diagnostic));
                    window.Post(() => { window.AllowClose = true; window.Close(); });
                    return;
                }
                window.Post(() => window.Finish(result, "waiting"));
                CompleteRestart(requestPath, request, result, window);
            }
            catch (Exception exception)
            {
                if (!committed)
                {
                    try
                    {
                        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                        writer.WriteLine("ERROR " + JsonSerializer.Serialize(exception.Message));
                    }
                    catch (IOException) { }
                    catch (InvalidOperationException) { }
                    window.Post(() => { window.AllowClose = true; window.Close(); });
                }
                else window.Post(() => window.Finish(new IndependentUpdateResult
                {
                    State = "rollbackFailed", Diagnostic = exception.Message,
                    BackupDirectory = IndependentUpdateFiles.OperationDirectory(request.InstallDirectory, request.OperationId)
                }));
            }
        }
        private static string RecoveryLocation(string requestPath) => Path.Combine(Path.GetDirectoryName(requestPath), "runner", IndependentUpdateFiles.RunnerName);

        private static void CompleteRestart(string path, IndependentUpdateRequest request, IndependentUpdateResult result, UpdateWindow window)
        {
            string outcome = "unavailable";
            try
            {
                var progress = IndependentUpdateFiles.Read<IndependentUpdateProgress>(Path.Combine(Path.GetDirectoryName(path), "progress.json"));
                if (!progress.RestartAdopted) { window.Post(() => window.Finish(result, outcome)); return; }
                var adoption = IndependentUpdateRestart.ReadAdoption(path, request);
                if (progress.SchemaVersion != 1 || progress.OperationId != request.OperationId
                    || adoption.RegistrationId != progress.RestartRegistrationId || adoption.HostProcessId != progress.HostProcessId
                    || adoption.HostProcessStartTimeUtc != progress.HostProcessStartTimeUtc)
                    throw new InvalidDataException("Committed restart identity mismatch.");
                outcome = "handshakeFailed";
                var deadline = DateTime.UtcNow.AddMinutes(2);
                string connectionPath = Path.Combine(Path.GetDirectoryName(path), "connection.json");
                while (!window.IsDisposed && DateTime.UtcNow < deadline)
                {
                    if (!IndependentUpdateRestart.IsAlive(adoption.HostProcessId.Value, adoption.HostProcessStartTimeUtc.Value))
                    { outcome = "hostExited"; break; }
                    if (File.Exists(connectionPath))
                    {
                        var connection = IndependentUpdateRestart.ReadConnection(path, request, adoption);
                        if (connection.State != "connected") { outcome = connection.State; break; }
                        if (connection.ActualVersion != result.ActualVersion) break;
                        string presentedPath = Path.Combine(Path.GetDirectoryName(path), "presented.json");
                        if (File.Exists(presentedPath))
                        {
                            var presented = IndependentUpdateFiles.Read<IndependentUpdatePresented>(presentedPath);
                            if (presented.SchemaVersion != 1 || presented.OperationId != request.OperationId
                                || presented.RegistrationId != adoption.RegistrationId || presented.ProcessId != connection.ProcessId
                                || presented.ProcessStartTimeUtc != connection.ProcessStartTimeUtc || presented.ActualVersion != result.ActualVersion)
                                throw new InvalidDataException("Presentation identity mismatch.");
                            window.Post(() => { window.AllowClose = true; window.Close(); });
                            return;
                        }
                    }
                    Thread.Sleep(100);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException
                || exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
            { outcome = "handshakeFailed"; }
            window.Post(() => window.Finish(result, outcome));
        }

    }

    internal sealed class UpdateWindow : Form
    {
        private readonly Dictionary<string, string> localized;
        private readonly Dictionary<string, string> defaults;
        private readonly TextBox status;
        private readonly Button close;
        internal bool AllowClose;

        internal UpdateWindow(string language)
        {
            defaults = LoadCatalog("cn");
            localized = LoadCatalog(language);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            Font = SystemFonts.MessageBoxFont;
            Text = Resolve("IndependentUpdate_caption");
            ClientSize = new Size(620, 350);
            MinimumSize = new Size(420, 260);
            StartPosition = FormStartPosition.CenterScreen;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 2 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            status = new TextBox
            {
                Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control, TabStop = true, AccessibleName = Text
            };
            close = new Button { Text = Resolve("Update_close"), AutoSize = true, Enabled = false, Anchor = AnchorStyles.Right };
            close.Click += (_, _) => Close();
            layout.Controls.Add(status, 0, 0);
            layout.Controls.Add(close, 0, 1);
            Controls.Add(layout);
            FormClosing += (_, e) => { if (!AllowClose) e.Cancel = true; };
            SetStatus("IndependentUpdate_preparing", null);
        }

        internal void Post(Action action) { if (!IsDisposed) BeginInvoke(action); }
        internal void SetStatus(string key, string diagnostic)
        {
            status.Text = SemanticMessageResolver.Resolve(SemanticMessage.CreateWithDiagnostic(key, diagnostic),
                k => localized.TryGetValue(k, out string value) ? value : null,
                k => defaults.TryGetValue(k, out string value) ? value : null);
        }
        internal void Finish(IndependentUpdateResult result, string connection = "unavailable")
        {
            string detail = result.Diagnostic;
            if (result.BackupDirectory != null) detail += Environment.NewLine + Resolve("IndependentUpdate_recoveryLocation") + Environment.NewLine + result.BackupDirectory;
            SetStatus("IndependentUpdate_" + result.State, detail);
            status.AppendText(Environment.NewLine + Environment.NewLine + Resolve("IndependentUpdate_" + connection));
            AllowClose = true;
            close.Enabled = true;
            Show();
        }
        private string Resolve(string key) => SemanticMessageResolver.Resolve(SemanticMessage.Create(key),
            k => localized.TryGetValue(k, out string value) ? value : null,
            k => defaults.TryGetValue(k, out string value) ? value : null);
        private static Dictionary<string, string> LoadCatalog(string language)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "language_" + language + ".txt")))
            {
                int separator = line.IndexOf('=');
                if (separator > 0) result[line.Substring(0, separator)] = line.Substring(separator + 1);
            }
            return result;
        }
    }
}
