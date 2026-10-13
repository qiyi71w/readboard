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
            if (args.Length != 2 || args[0] != "--request") return;
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            IndependentUpdateRequest request;
            try
            {
                request = IndependentUpdateFiles.Read<IndependentUpdateRequest>(args[1]);
                IndependentUpdateFiles.ValidateRequest(request);
                if (IndependentUpdateFiles.IsWithin(request.InstallDirectory, AppContext.BaseDirectory)) return;
            }
            catch { return; }
            using var window = new UpdateWindow(request.Language);
            window.Shown += (_, _) =>
            {
                window.Hide();
                var thread = new Thread(() => Execute(args[1], request, window)) { IsBackground = false };
                thread.Start();
            };
            Application.Run(window);
        }

        private static void Execute(string requestPath, IndependentUpdateRequest request, UpdateWindow window)
        {
            bool committed = false;
            using var pipe = new NamedPipeClientStream(".", "ReadBoard.Update." + request.OperationId,
                PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            try
            {
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
                        window.Post(() => { window.Show(); window.SetStatus("IndependentUpdate_installing", null); });
                        writer.WriteLine("ACCEPTED " + request.OperationId);
                    },
                    phase => { if (committed) window.Post(() => window.SetStatus("IndependentUpdate_installing", null)); });
                if (!committed)
                {
                    writer.WriteLine("ERROR " + JsonSerializer.Serialize(result.Diagnostic));
                    window.Post(() => { window.AllowClose = true; window.Close(); });
                    return;
                }
                window.Post(() => window.Finish(result));
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
    }

    internal sealed class UpdateWindow : Form
    {
        private readonly Dictionary<string, string> localized;
        private readonly Dictionary<string, string> defaults;
        private readonly TextBox status;
        private readonly Button close;
        internal bool AllowClose { get; set; }

        internal UpdateWindow(string language)
        {
            defaults = Load("cn");
            localized = Load(language);
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
        internal void Finish(IndependentUpdateResult result)
        {
            string detail = result.Diagnostic;
            if (result.BackupDirectory != null) detail += Environment.NewLine + Resolve("IndependentUpdate_recoveryLocation") + Environment.NewLine + result.BackupDirectory;
            SetStatus("IndependentUpdate_" + result.State, detail);
            status.AppendText(Environment.NewLine + Environment.NewLine + Resolve("IndependentUpdate_unavailable"));
            AllowClose = true;
            close.Enabled = true;
            Show();
        }
        private string Resolve(string key) => SemanticMessageResolver.Resolve(SemanticMessage.Create(key),
            k => localized.TryGetValue(k, out string value) ? value : null,
            k => defaults.TryGetValue(k, out string value) ? value : null);
        private static Dictionary<string, string> Load(string language)
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
