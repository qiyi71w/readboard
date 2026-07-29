using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace readboard
{
    public partial class MainForm
    {
        private const string WebViewHostName = "app.readboard";
        private const string ReadBoardRepositoryUrl = "https://github.com/qiyi71w/readboard";
        private const string WebViewRuntimeInstallerUrl =
            "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";
        private static readonly JsonSerializerOptions WebViewJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly ReadBoardUiState webViewState = new ReadBoardUiState();
        private readonly Queue<ReadBoardUiLogEntry> webViewLogs = new Queue<ReadBoardUiLogEntry>();
        private bool webViewTextSent;
        private WebView2 webView;
        private bool hostCommunicationEstablished;

        private const int WmNcHitTest = 0x0084;
        internal const int WsThickFrame = 0x00040000;
        internal const int WsMinimizeBox = 0x00020000;
        internal const int WsMaximizeBox = 0x00010000;
        internal const int HtClient = 1;
        internal const int HtMaxButton = 9;
        internal const int HtLeft = 10;
        internal const int HtRight = 11;
        internal const int HtTop = 12;
        internal const int HtTopLeft = 13;
        internal const int HtTopRight = 14;
        internal const int HtBottom = 15;
        internal const int HtBottomLeft = 16;
        internal const int HtBottomRight = 17;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.Style = ResolveWebViewWindowStyle(parameters.Style);
                return parameters;
            }
        }

        internal static int ResolveWebViewWindowStyle(int style)
        {
            return style | WsThickFrame | WsMinimizeBox | WsMaximizeBox;
        }

        internal bool EnsureWebViewRuntimeAvailable()
        {
            while (true)
            {
                try
                {
                    CoreWebView2Environment.GetAvailableBrowserVersionString();
                    return true;
                }
                catch (WebView2RuntimeNotFoundException)
                {
                    var openDownloadPage = new TaskDialogButton(
                        getLangStr("WebViewRuntime_openDownload"));
                    var retry = new TaskDialogButton(getLangStr("WebViewRuntime_retry"));
                    var exit = new TaskDialogButton(getLangStr("WebViewRuntime_exit"));
                    var page = new TaskDialogPage
                    {
                        Caption = getLangStr("WebViewRuntime_caption"),
                        Heading = getLangStr("WebViewRuntime_heading"),
                        Text = string.Format(
                            CultureInfo.CurrentCulture,
                            getLangStr("WebViewRuntime_message"),
                            AppReleaseVersion.GetCurrentVersion()),
                        Icon = TaskDialogIcon.Error,
                        AllowCancel = false,
                        DefaultButton = retry
                    };
                    page.Buttons.Add(openDownloadPage);
                    page.Buttons.Add(retry);
                    page.Buttons.Add(exit);

                    TaskDialogButton selected = TaskDialog.ShowDialog(this, page);
                    if (ReferenceEquals(selected, retry))
                        continue;
                    if (ReferenceEquals(selected, openDownloadPage))
                    {
                        try
                        {
                            using (Process process = Process.Start(
                                CreateWebViewRuntimeDownloadStartInfo(
                                    GetWebViewRuntimeInstallerUri())))
                            {
                            }
                        }
                        catch (Exception exception)
                        {
                            Trace.TraceError(exception.ToString());
                            MessageBox.Show(
                                this,
                                getLangStr("WebViewRuntime_openDownloadFailed"),
                                getLangStr("WebViewRuntime_caption"),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                        continue;
                    }

                    Dispose();
                    return false;
                }
            }
        }

        internal static Uri GetWebViewRuntimeInstallerUri()
        {
            return new Uri(WebViewRuntimeInstallerUrl, UriKind.Absolute);
        }

        internal static ProcessStartInfo CreateWebViewRuntimeDownloadStartInfo(Uri uri)
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri));

            return new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            };
        }

        private void InitializeWebViewShell()
        {
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = true;
            TopMost = false;
            ApplySavedWebViewWindowBounds();

            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(243, 246, 250)
            };
            foreach (Control control in Controls)
                control.Visible = false;
            Controls.Add(webView);
            webView.BringToFront();
            Shown += InitializeWebViewAsync;
            Resize += MainFormWebView_Resize;
        }

        private void ApplySavedWebViewWindowBounds()
        {
            AppConfig config = Program.CurrentContext.Config;
            int dpi = Math.Max(96, DeviceDpi);
            Size minimumClientSize = WebViewWindowLayoutPolicy.ScaleLogicalSize(
                WebViewWindowLayoutPolicy.MinimumLogicalClientSize,
                dpi);
            Size desiredClientSize = WebViewWindowLayoutPolicy.ScaleLogicalSize(
                new Size(config.WindowClientWidth, config.WindowClientHeight),
                dpi);
            Size minimumSize = SizeFromClientSize(minimumClientSize);
            Size desiredSize = SizeFromClientSize(desiredClientSize);
            Point desiredLocation = config.WindowPosX == -1 || config.WindowPosY == -1
                ? Location
                : new Point(config.WindowPosX, config.WindowPosY);
            Rectangle workingArea = Screen.FromPoint(desiredLocation).WorkingArea;
            Rectangle desiredBounds = new Rectangle(desiredLocation, desiredSize);
            Bounds = WebViewWindowLayoutPolicy.ClampBoundsToWorkingArea(
                desiredBounds,
                workingArea,
                minimumSize);
            MinimumSize = new Size(
                Math.Min(minimumSize.Width, workingArea.Width),
                Math.Min(minimumSize.Height, workingArea.Height));
            if (config.WindowMaximized)
                WindowState = FormWindowState.Maximized;
        }

        private void UpdateWebViewMinimumSizeForCurrentDpi()
        {
            Size minimumClientSize = WebViewWindowLayoutPolicy.ScaleLogicalSize(
                WebViewWindowLayoutPolicy.MinimumLogicalClientSize,
                DeviceDpi);
            Size minimumSize = SizeFromClientSize(minimumClientSize);
            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(
                Math.Min(minimumSize.Width, workingArea.Width),
                Math.Min(minimumSize.Height, workingArea.Height));
        }

        private async void InitializeWebViewAsync(object sender, EventArgs e)
        {
            Shown -= InitializeWebViewAsync;
            try
            {
                await webView.EnsureCoreWebView2Async();
                ConfigureWebView();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        getLangStr("WebView_initializationFailed"),
                        ex.Message),
                    getLangStr("WebViewRuntime_caption"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Close();
            }
        }

        private void ConfigureWebView()
        {
            webViewTextSent = false;
            string webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView");
            string entryPoint = Path.Combine(webRoot, "index.html");
            if (!File.Exists(entryPoint))
                throw new FileNotFoundException(getLangStr("WebView_mainPageMissing"), entryPoint);

            CoreWebView2 core = webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsNonClientRegionSupportEnabled = true;
            core.SetVirtualHostNameToFolderMapping(
                WebViewHostName,
                webRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += CoreWebView2_NavigationStarting;
            core.NewWindowRequested += CoreWebView2_NewWindowRequested;
            core.WebMessageReceived += CoreWebView2_WebMessageReceived;
            core.NavigationCompleted += CoreWebView2_NavigationCompleted;
            core.Navigate("https://" + WebViewHostName + "/index.html");
        }

        private void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            Uri uri;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && string.Equals(uri.Host, WebViewHostName, StringComparison.OrdinalIgnoreCase))
                return;

            e.Cancel = true;
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }

        private static void OpenExternalUri(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return;

            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }

        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
                PostWebViewState();
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            ReadBoardUiCommand command;
            if (!TryParseWebViewCommand(e.WebMessageAsJson, out command))
                return;

            ReadBoardUpdateIntent updateIntent;
            bool updateCommand = TryParseWebViewUpdateIntent(command, out updateIntent);

            if (updateCommand)
            {
                HandleWebViewUpdateIntent(updateIntent);
            }
            else
            {
                bool publish = DispatchNonUpdateWebViewCommand(command);
                if (publish)
                    PostWebViewState();
                return;
            }
        }

        private bool DispatchNonUpdateWebViewCommand(ReadBoardUiCommand command)
        {
            switch (command.Type)
            {
                case "window.minimize":
                    WindowState = FormWindowState.Minimized;
                    break;
                case "window.maximize":
                    WindowState = WindowState == FormWindowState.Maximized
                        ? FormWindowState.Normal
                        : FormWindowState.Maximized;
                    break;
                case "window.close":
                    Close();
                    break;
                case "navigate":
                    HandleNavigate(command.Payload);
                    break;
                case "control.update":
                    return HandleControlUpdate(command.Payload);
                case "sync.quick":
                    if (SupportsFastSyncType(CurrentSyncType)
                        && (!sessionCoordinator.StartedSync || sessionCoordinator.IsContinuousSyncing))
                        button10_Click(this, EventArgs.Empty);
                    break;
                case "sync.continuous":
                    if (!sessionCoordinator.IsContinuousSyncing)
                        button5_Click(this, EventArgs.Empty);
                    break;
                case "sync.once":
                    if (!HasActiveSyncOperation())
                        button4_Click(this, EventArgs.Empty);
                    break;
                case "sync.toggleAnalysis":
                    HandleWebViewToggleAnalysis();
                    break;
                case "sync.swapOrder":
                    button8_Click(this, EventArgs.Empty);
                    break;
                case "sync.rebuild":
                    btnForceRebuild_Click(this, EventArgs.Empty);
                    break;
                case "sync.clearBoard":
                    sessionCoordinator.StopSyncSessionAndClearBoard();
                    break;
                case "board.select":
                    HandleBoardSelect(command.Payload);
                    break;
                case "rules.openManual":
                    OpenWebViewManual();
                    break;
                case "about.openRepository":
                    OpenExternalUri(ReadBoardRepositoryUrl);
                    break;
                default:
                    if (!HandleWebViewIdentityCommand(command))
                        HandleWebViewSettingsCommand(command);
                    break;
            }
            return true;
        }

        private void HandleWebViewUpdateIntent(ReadBoardUpdateIntent intent)
        {
            switch (intent)
            {
                case ReadBoardUpdateIntent.Check:
                    _ = CheckForWebViewUpdateAsync();
                    break;
                case ReadBoardUpdateIntent.Close:
                    CloseWebViewUpdate();
                    break;
                case ReadBoardUpdateIntent.Install:
                    _ = InstallWebViewUpdateAsync();
                    break;
                case ReadBoardUpdateIntent.OpenDownload:
                    OpenWebViewUpdateDownload();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent));
            }
        }

        internal static bool TryParseWebViewUpdateIntent(
            ReadBoardUiCommand command,
            out ReadBoardUpdateIntent intent)
        {
            intent = default(ReadBoardUpdateIntent);
            if (command == null || !HasEmptyPayload(command.Payload))
                return false;

            switch (command.Type)
            {
                case "about.checkUpdate":
                    intent = ReadBoardUpdateIntent.Check;
                    return true;
                case "update.close":
                    intent = ReadBoardUpdateIntent.Close;
                    return true;
                case "update.install":
                    intent = ReadBoardUpdateIntent.Install;
                    return true;
                case "update.openDownload":
                    intent = ReadBoardUpdateIntent.OpenDownload;
                    return true;
                default:
                    return false;
            }
        }

        internal static bool TryParseWebViewCommand(string json, out ReadBoardUiCommand command)
        {
            command = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                command = JsonSerializer.Deserialize<ReadBoardUiCommand>(json, WebViewJsonOptions);
                return IsValidWebViewCommand(command);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsValidWebViewCommand(ReadBoardUiCommand command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Type))
                return false;
            switch (command.Type)
            {
                case "window.minimize":
                case "window.maximize":
                case "window.close":
                case "sync.quick":
                case "sync.continuous":
                case "sync.once":
                case "sync.toggleAnalysis":
                case "sync.swapOrder":
                case "sync.rebuild":
                case "sync.clearBoard":
                case "rules.openManual":
                case "about.openRepository":
                case "about.checkUpdate":
                case "update.close":
                case "update.install":
                case "update.openDownload":
                    return HasEmptyPayload(command.Payload);
                case "navigate":
                    return HasSingleAllowedString(command.Payload, "page", "controlCenter", "settings", "rules", "about");
                case "board.select":
                    return HasSingleAllowedString(command.Payload, "mode", "inside", "rectangle", "line1");
                case "control.update":
                    return IsValidControlUpdate(command.Payload);
                default:
                    return IsValidWebViewIdentityCommand(command)
                        || IsValidWebViewSettingsCommand(command);
            }
        }

        private static bool HasSingleAllowedString(JsonElement payload, string name, params string[] allowed)
        {
            JsonElement value;
            if (payload.ValueKind != JsonValueKind.Object
                || CountProperties(payload) != 1
                || !payload.TryGetProperty(name, out value)
                || value.ValueKind != JsonValueKind.String)
                return false;
            string text = value.GetString();
            for (int i = 0; i < allowed.Length; i++)
            {
                if (text == allowed[i])
                    return true;
            }
            return false;
        }

        private static bool IsValidControlUpdate(JsonElement payload)
        {
            JsonElement keyValue;
            JsonElement value;
            if (payload.ValueKind != JsonValueKind.Object
                || CountProperties(payload) != 2
                || !payload.TryGetProperty("key", out keyValue)
                || keyValue.ValueKind != JsonValueKind.String
                || !payload.TryGetProperty("value", out value))
                return false;
            string key = keyValue.GetString();
            ControlCenterIntent controlCenterIntent;
            if (TryCreateControlCenterIntent(payload, out controlCenterIntent))
                return true;
            if (key == "two-way" || key == "auto-play" || key == "show-on-board")
                return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False;
            if (key == "color")
                return IsAllowedString(value, "black", "white", "auto");
            if (key == "placement")
                return IsAllowedString(value, "direct", "engine");
            int numeric;
            if (key == "ai-time")
                return TryReadInteger(value, false, 1, out numeric);
            if (key == "playouts" || key == "first-policy")
                return TryReadInteger(value, true, 0, out numeric);
            return false;
        }

        internal static bool TryCreateControlCenterIntent(
            ReadBoardUiCommand command,
            out ControlCenterIntent intent)
        {
            intent = null;
            if (command == null || command.Type != "control.update")
                return false;
            return TryCreateControlCenterIntent(command.Payload, out intent);
        }

        private static bool TryCreateControlCenterIntent(
            JsonElement payload,
            out ControlCenterIntent intent)
        {
            intent = null;
            if (payload.ValueKind != JsonValueKind.Object
                || CountProperties(payload) != 2
                || !payload.TryGetProperty("key", out JsonElement keyValue)
                || !payload.TryGetProperty("value", out JsonElement value)
                || keyValue.ValueKind != JsonValueKind.String)
                return false;

            string key = keyValue.GetString();
            if (key == "platform" && value.ValueKind == JsonValueKind.String)
            {
                SyncMode platform;
                if (!ControlCenterPreferences.TryParsePlatform(value.GetString(), out platform))
                    return false;
                intent = ControlCenterIntent.SetPlatform(platform);
                return true;
            }

            if (key == "boardSize" && value.ValueKind == JsonValueKind.String)
            {
                ControlCenterBoardSizeKind boardSizeKind;
                if (!ControlCenterPreferences.TryParseBoardSize(value.GetString(), out boardSizeKind))
                    return false;
                intent = ControlCenterIntent.SetBoardSize(boardSizeKind);
                return true;
            }

            int dimension;
            if ((key == "board-width" || key == "board-height")
                && TryReadInteger(value, false, 2, out dimension)
                && ControlCenterPreferences.IsValidDimension(dimension))
            {
                intent = key == "board-width"
                    ? ControlCenterIntent.SetCustomBoardWidth(dimension)
                    : ControlCenterIntent.SetCustomBoardHeight(dimension);
                return true;
            }

            return false;
        }

        private static bool IsAllowedString(JsonElement value, params string[] allowed)
        {
            if (value.ValueKind != JsonValueKind.String)
                return false;
            string text = value.GetString();
            for (int i = 0; i < allowed.Length; i++)
            {
                if (text == allowed[i])
                    return true;
            }
            return false;
        }

        internal static string SerializeWebViewState(ReadBoardUiState state)
        {
            return JsonSerializer.Serialize(new { type = "state", payload = state }, WebViewJsonOptions);
        }

        private static bool HasEmptyPayload(JsonElement payload)
        {
            return payload.ValueKind == JsonValueKind.Undefined
                || payload.ValueKind == JsonValueKind.Null
                || (payload.ValueKind == JsonValueKind.Object && !payload.EnumerateObject().MoveNext());
        }

        private void HandleNavigate(JsonElement payload)
        {
            webViewState.Page = payload.GetProperty("page").GetString();
            if (webViewState.Page == "settings")
                GetWebViewSettingsState();
        }

        private void OpenWebViewManual()
        {
            try
            {
                Process.Start(new ProcessStartInfo(getLangStr("helpFile")) { UseShellExecute = true });
            }
            catch (Exception)
            {
                webViewSettingsDialog = new ReadBoardDialogUiState
                {
                    Open = true,
                    Title = getLangStr("WebView_manualOpenFailedTitle"),
                    Message = getLangStr("noHelpFile")
                };
            }
        }

        private void ShowWebViewMessage(string title, string message)
        {
            webViewSettingsDialog = new ReadBoardDialogUiState
            {
                Open = true,
                Title = title,
                Message = message
            };
            AddWebViewLog("WARN", message);
            PostWebViewState();
        }

        private bool HandleControlUpdate(JsonElement payload)
        {
            JsonElement keyValue = payload.GetProperty("key");
            JsonElement value = payload.GetProperty("value");
            string key = keyValue.GetString();

            ControlCenterIntent controlCenterIntent;
            if (TryCreateControlCenterIntent(payload, out controlCenterIntent))
                return ApplyControlCenterIntent(controlCenterIntent).ShouldPublishSnapshot;

            if (key == "two-way")
                UpdateBooleanControl(value, chkBothSync);
            else if (key == "auto-play")
                UpdateBooleanControl(value, chkAutoPlay);
            else if (key == "color")
                UpdateAutoPlayColor(value);
            else if (key == "placement")
                UpdatePlacementMode(value);
            else if (key == "ai-time")
                UpdateNumericControl(value, textBox1, false, 1);
            else if (key == "playouts")
                UpdateNumericControl(value, textBox2, true, 0);
            else if (key == "first-policy")
                UpdateNumericControl(value, textBox3, true, 0);
            else if (key == "show-on-board" && SupportsShowInBoard())
                UpdateBooleanControl(value, chkShowInBoard);
            return true;
        }

        private static void UpdateBooleanControl(JsonElement value, CheckBox control)
        {
            if (control.Enabled
                && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
                control.Checked = value.GetBoolean();
        }

        private void UpdateAutoPlayColor(JsonElement value)
        {
            if (!chkAutoPlay.Checked || value.ValueKind != JsonValueKind.String)
                return;
            switch (value.GetString())
            {
                case "black": radioBlack.Checked = true; break;
                case "white": radioWhite.Checked = true; break;
                case "auto":
                    if (IsFoxSyncType(CurrentSyncType))
                        radioAutoPlayColor.Checked = true;
                    break;
            }
        }

        private void UpdatePlacementMode(JsonElement value)
        {
            if (!chkAutoPlay.Checked || value.ValueKind != JsonValueKind.String)
                return;
            if (value.GetString() == "direct")
                radioAutoPlayMoveFirst.Checked = true;
            else if (value.GetString() == "engine")
                radioAutoPlayMoveGma.Checked = true;
        }

        private static void UpdateNumericControl(JsonElement value, TextBox control, bool allowEmpty, int minimum)
        {
            int parsed;
            if (!control.Enabled || value.ValueKind != JsonValueKind.String)
                return;
            string text = value.GetString();
            if (allowEmpty && string.IsNullOrEmpty(text))
            {
                control.Text = string.Empty;
                return;
            }
            if (int.TryParse(text, out parsed) && parsed >= minimum)
                control.Text = parsed.ToString();
        }

        private static bool TryReadInteger(JsonElement value, bool allowEmpty, int minimum, out int parsed)
        {
            parsed = 0;
            if (value.ValueKind != JsonValueKind.String)
                return false;
            string text = value.GetString();
            if (allowEmpty && string.IsNullOrEmpty(text))
                return true;
            return int.TryParse(text, out parsed) && parsed >= minimum;
        }

        private void HandleBoardSelect(JsonElement payload)
        {
            if (HasActiveSyncOperation())
                return;
            string mode = payload.GetProperty("mode").GetString();
            if (mode == "inside" && !UsesManualSelectionType(CurrentSyncType))
                button3_Click(this, EventArgs.Empty);
            else if (mode == "rectangle" && UsesManualSelectionType(CurrentSyncType))
                Button2_Click(this, EventArgs.Empty);
            else if (mode == "line1" && UsesManualSelectionType(CurrentSyncType))
                button11_Click(this, EventArgs.Empty);
        }

        private static int CountProperties(JsonElement value)
        {
            int count = 0;
            foreach (JsonProperty property in value.EnumerateObject())
                count++;
            return count;
        }

        private void MainFormWebView_Resize(object sender, EventArgs e)
        {
            webViewState.Shell.Maximized = WindowState == FormWindowState.Maximized;
            PostWebViewState();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WmNcHitTest
                || m.Result != (IntPtr)HtClient)
                return;

            long coordinates = m.LParam.ToInt64();
            Point clientPoint = PointToClient(new Point(
                unchecked((short)(coordinates & 0xffff)),
                unchecked((short)((coordinates >> 16) & 0xffff))));
            Size logicalClientSize = WebViewWindowLayoutPolicy.UnscalePhysicalSize(ClientSize, DeviceDpi);
            double pageScale = WebViewWindowLayoutPolicy.ResolveScale(logicalClientSize);
            int titleControlExtent = (int)Math.Round(48d * pageScale * Math.Max(96, DeviceDpi) / 96d);
            int hit = ResolveWebViewNonClientHitTest(
                clientPoint,
                ClientSize,
                Math.Max(6, DeviceDpi / 16),
                titleControlExtent,
                WindowState == FormWindowState.Maximized);
            if (hit != HtClient)
                m.Result = (IntPtr)hit;
        }

        internal static int ResolveWebViewNonClientHitTest(
            Point point,
            Size size,
            int resizeBorder,
            int titleControlExtent,
            bool maximized)
        {
            if (maximized)
                return HtClient;

            int resizeHit = ResolveResizeHitTest(point, size, resizeBorder);
            if (resizeHit != HtClient)
                return resizeHit;

            int maximizeLeft = size.Width - titleControlExtent * 2;
            int maximizeRight = size.Width - titleControlExtent;
            if (point.Y >= 0
                && point.Y < titleControlExtent
                && point.X >= maximizeLeft
                && point.X < maximizeRight)
                return HtMaxButton;
            return HtClient;
        }

        internal static int ResolveResizeHitTest(Point point, Size size, int border)
        {
            bool left = point.X >= 0 && point.X < border;
            bool right = point.X < size.Width && point.X >= size.Width - border;
            bool top = point.Y >= 0 && point.Y < border;
            bool bottom = point.Y < size.Height && point.Y >= size.Height - border;
            if (top && left) return HtTopLeft;
            if (top && right) return HtTopRight;
            if (bottom && left) return HtBottomLeft;
            if (bottom && right) return HtBottomRight;
            if (left) return HtLeft;
            if (right) return HtRight;
            if (top) return HtTop;
            if (bottom) return HtBottom;
            return HtClient;
        }

        private void PostWebViewState()
        {
            if (suppressWebViewStatePublication
                || webView == null
                || webView.CoreWebView2 == null)
                return;

            webView.CoreWebView2.PostWebMessageAsJson(SerializeWebViewState(BuildWebViewState()));
            webViewTextSent = true;
        }

        private ReadBoardUiState BuildWebViewState()
        {
            bool? targetWindowValid = hwnd == IntPtr.Zero ? (bool?)null : IsWindow(hwnd);
            return new ReadBoardUiState
            {
                Page = webViewState.Page,
                Language = Program.language,
                Text = webViewTextSent ? null : BuildWebViewText(),
                Shell = new ReadBoardShellState
                {
                    Version = "v" + AppReleaseVersion.GetCurrentVersion(),
                    Theme = ResolveWebViewTheme(Program.CurrentConfig.ColorMode),
                    Connected = hostCommunicationEstablished,
                    SyncStatus = ResolveWebViewSyncStatus(
                        hostCommunicationEstablished,
                        HasActiveSyncOperation()),
                    LastSync = webViewState.Shell.LastSync ?? "--:--:--",
                    StoneCount = webViewState.Shell.StoneCount,
                    Duration = "--",
                    TargetWindowValid = targetWindowValid,
                    BoardRegionRecognized = webViewState.Shell.BoardRegionRecognized,
                    PlacementRegionResolved = webViewState.Shell.PlacementRegionResolved,
                    Maximized = WindowState == FormWindowState.Maximized
                },
                ControlCenter = BuildControlCenterState(targetWindowValid == true),
                Settings = GetWebViewSettingsState(),
                Update = GetWebViewUpdateState(),
                Identity = GetWebViewIdentityState(),
                Dialog = GetWebViewSettingsDialogState(),
                Logs = new List<ReadBoardUiLogEntry>(webViewLogs)
            };
        }

        private static IDictionary<string, string> BuildWebViewText()
        {
            var text = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in Program.langItems)
            {
                if (entry.Key is string key && entry.Value is string value)
                    text[key] = value;
            }
            return text;
        }

        private ReadBoardControlCenterState BuildControlCenterState(bool targetWindowValid)
        {
            ControlCenterRuntimeSnapshot controlCenter = controlCenterRuntime.Snapshot;
            string room = "--";
            int? moves = null;
            if (controlCenter.Platform == SyncMode.Yike)
            {
                room = string.IsNullOrWhiteSpace(lastYikeWindowContext.RoomToken) ? "--" : lastYikeWindowContext.RoomToken;
                moves = lastYikeWindowContext.MoveNumber;
            }
            else if (controlCenter.Platform == SyncMode.Fox
                || controlCenter.Platform == SyncMode.FoxBackgroundPlace)
            {
                room = string.IsNullOrWhiteSpace(lastFoxWindowContext.RoomToken) ? "--" : lastFoxWindowContext.RoomToken;
                moves = lastFoxWindowContext.ResolveDisplayedMoveNumber();
            }

            return new ReadBoardControlCenterState
            {
                Platform = ControlCenterPreferences.ToPlatformToken(controlCenter.Platform),
                Room = room,
                Moves = moves.HasValue ? moves.Value.ToString() : "--",
                NextTurn = ResolveWebViewNextTurn(),
                TitleBound = targetWindowValid,
                BoardSize = ControlCenterPreferences.ToBoardSizeToken(controlCenter.BoardSizeKind),
                BoardWidth = controlCenter.BoardWidth,
                BoardHeight = controlCenter.BoardHeight,
                TwoWaySync = sessionCoordinator.SyncBoth,
                AutoPlay = chkAutoPlay.Checked,
                Color = radioAutoPlayColor.Checked ? "auto" : radioWhite.Checked ? "white" : "black",
                Placement = radioAutoPlayMoveGma.Checked ? "engine" : "direct",
                AiTime = textBox1.Text,
                Playouts = textBox2.Text,
                FirstPolicy = textBox3.Text,
                FirstPolicyEnabled = textBox3.Enabled,
                ShowOnBoard = Program.showInBoard,
                QuickSyncActive = sessionCoordinator.IsContinuousSyncing,
                ContinuousSyncActive = sessionCoordinator.StartedSync && !sessionCoordinator.IsContinuousSyncing,
                QuickSyncEnabled = SupportsFastSyncType(CurrentSyncType)
                    && (!sessionCoordinator.StartedSync || sessionCoordinator.IsContinuousSyncing),
                ContinuousSyncEnabled = !sessionCoordinator.IsContinuousSyncing,
                SyncInterval = Program.timeinterval,
                AnalysisRunning = hostAnalysisRunning != false,
                AnalysisStateAvailable = hostAnalysisRunning.HasValue,
                AnalysisToggleEnabled = hostAnalysisRunning != false || hostAnalysisRunning.HasValue,
                ConfigurationEnabled = controlCenter.ConfigurationEnabled,
                TwoWaySyncEnabled = chkBothSync.Enabled,
                AutoPlayToggleEnabled = chkAutoPlay.Enabled,
                AutoPlayControlsEnabled = radioBlack.Enabled,
                CustomBoardSizeEnabled = controlCenter.CustomBoardSizeEnabled,
                CustomBoardDimensionsEnabled = controlCenter.CustomBoardDimensionsEnabled,
                PreferencesSaved = controlCenter.PreferencesSaved,
                PersistenceError = controlCenter.PersistenceError,
                IdentityEnabled = btnFoxAutoPlayIdentity.Enabled,
                ShowOnBoardEnabled = chkShowInBoard.Enabled
            };
        }

        internal static string ResolveWebViewSyncStatus(
            bool communicationEstablished,
            bool activeSync)
        {
            if (activeSync)
                return "同步中";
            return communicationEstablished ? "就绪" : "宿主模式已启动";
        }

        private string ResolveWebViewNextTurn()
        {
            if (lastMainWindowTitleTurn == MainWindowTitleTurn.Black)
                return "黑";
            if (lastMainWindowTitleTurn == MainWindowTitleTurn.White)
                return "白";
            return "--";
        }

        private void UpdateWebViewBoardFrameState(
            BoardFrame frame,
            int boardPixelWidth,
            int boardPixelHeight,
            bool placementRegionResolved)
        {
            webViewState.Shell.BoardRegionRecognized = IsBoardRegionRecognized(
                frame,
                boardPixelWidth,
                boardPixelHeight);
            webViewState.Shell.PlacementRegionResolved = webViewState.Shell.BoardRegionRecognized && placementRegionResolved;
            PostWebViewState();
        }

        internal static bool IsBoardRegionRecognized(
            BoardFrame frame,
            int boardPixelWidth,
            int boardPixelHeight)
        {
            PixelRect bounds = frame == null || frame.Viewport == null
                ? null
                : frame.Viewport.SourceBounds ?? frame.Viewport.ScreenBounds;
            return frame != null
                && bounds != null
                && bounds.Width > 0
                && bounds.Height > 0
                && boardPixelWidth > 0
                && boardPixelHeight > 0;
        }

        private void UpdateWebViewSnapshotState(BoardSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.IsValid)
                return;
            webViewState.Shell.LastSync = DateTime.Now.ToString("HH:mm:ss");
            webViewState.Shell.StoneCount = snapshot.BlackStoneCount + snapshot.WhiteStoneCount;
            PostWebViewState();
        }

        private void UpdateWebViewSnapshotSentState(BoardSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.IsValid)
                return;
            AddWebViewLog("SYNC", "已识别并发送棋盘状态");
            PostWebViewState();
        }

        private void HandleWebViewToggleAnalysis()
        {
            if (hostAnalysisRunning == false)
            {
                sessionCoordinator.SendResumePonder();
                return;
            }
            sessionCoordinator.SendNoPonder();
        }

        private void ResetWebViewSyncState()
        {
            ResetShellSyncState(webViewState.Shell);
            PostWebViewState();
        }

        internal static void ResetShellSyncState(ReadBoardShellState shell)
        {
            shell.BoardRegionRecognized = false;
            shell.PlacementRegionResolved = false;
            shell.LastSync = null;
            shell.StoneCount = 0;
        }

        private void AddWebViewLog(string level, string message)
        {
            if (webViewLogs.Count == 100)
                webViewLogs.Dequeue();
            webViewLogs.Enqueue(new ReadBoardUiLogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Level = level,
                Message = message
            });
        }
    }
}
