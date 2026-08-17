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
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        };

        private readonly ReadBoardUiState webViewState = new ReadBoardUiState();
        private readonly Queue<ReadBoardUiLogEntry> webViewLogs = new Queue<ReadBoardUiLogEntry>();
        private WebView2 webView;
        private bool suppressWebViewResizePublication;
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

        internal enum WebViewRuntimePromptChoice
        {
            OpenDownload,
            Retry,
            Exit
        }

        internal bool EnsureWebViewRuntimeAvailable()
        {
            return ResolveWebViewRuntimeAvailability(
                delegate
                {
                    try
                    {
                        CoreWebView2Environment.GetAvailableBrowserVersionString();
                        return true;
                    }
                    catch (WebView2RuntimeNotFoundException)
                    {
                        return false;
                    }
                },
                ShowMissingWebViewRuntimePrompt,
                OpenWebViewRuntimeDownload,
                Dispose);
        }

        internal static bool ResolveWebViewRuntimeAvailability(
            Func<bool> isAvailable,
            Func<WebViewRuntimePromptChoice> chooseAction,
            Action openDownload,
            Action exit)
        {
            if (isAvailable == null)
                throw new ArgumentNullException(nameof(isAvailable));
            if (chooseAction == null)
                throw new ArgumentNullException(nameof(chooseAction));
            if (openDownload == null)
                throw new ArgumentNullException(nameof(openDownload));
            if (exit == null)
                throw new ArgumentNullException(nameof(exit));

            while (!isAvailable())
            {
                switch (chooseAction())
                {
                    case WebViewRuntimePromptChoice.OpenDownload:
                        openDownload();
                        break;
                    case WebViewRuntimePromptChoice.Retry:
                        break;
                    case WebViewRuntimePromptChoice.Exit:
                        exit();
                        return false;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(chooseAction));
                }
            }

            return true;
        }

        private WebViewRuntimePromptChoice ShowMissingWebViewRuntimePrompt()
        {
            var openDownloadPage = new TaskDialogButton(
                getLangStr("WebViewRuntime_openDownload"));
            var retry = new TaskDialogButton(getLangStr("WebViewRuntime_retry"));
            var exit = new TaskDialogButton(getLangStr("WebViewRuntime_exit"));
            var page = new TaskDialogPage
            {
                Caption = getLangStr("WebViewRuntime_caption"),
                Heading = getLangStr("WebViewRuntime_heading"),
                Text = Program.ResolveSemanticMessage(
                    SemanticMessage.CreateWithDiagnostic(
                        "WebViewRuntime_message",
                        AppReleaseVersion.GetCurrentVersion())),
                Icon = TaskDialogIcon.Error,
                AllowCancel = false,
                DefaultButton = retry
            };
            page.Buttons.Add(openDownloadPage);
            page.Buttons.Add(retry);
            page.Buttons.Add(exit);

            TaskDialogButton selected = TaskDialog.ShowDialog(this, page);
            if (ReferenceEquals(selected, openDownloadPage))
                return WebViewRuntimePromptChoice.OpenDownload;
            if (ReferenceEquals(selected, retry))
                return WebViewRuntimePromptChoice.Retry;
            return WebViewRuntimePromptChoice.Exit;
        }

        private void OpenWebViewRuntimeDownload()
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
                    Program.ResolveSemanticMessage(
                        SemanticMessage.CreateWithDiagnostic(
                            "WebView_initializationFailed",
                            ex.Message)),
                    getLangStr("WebViewRuntime_caption"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Close();
            }
        }

        private void ConfigureWebView()
        {
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
            if (TryParseWebViewCommand(e.WebMessageAsJson, out command))
                DispatchWebViewCommand(command);
        }

        internal bool DispatchWebViewCommand(ReadBoardUiCommand command)
        {
            if (command == null)
                return false;

            return webViewStatePublisher.Dispatch(delegate
            {
                ReadBoardUpdateIntent updateIntent;
                return TryParseWebViewUpdateIntent(command, out updateIntent)
                    ? DispatchWebViewUpdateIntent(updateIntent)
                    : DispatchNonUpdateWebViewCommand(command);
            });
        }



        private bool DispatchNonUpdateWebViewCommand(ReadBoardUiCommand command)
        {
            WebViewWindowIntent windowIntent;
            if (WebViewWindowCommandRuntime.TryCreateIntent(command, out windowIntent))
                return webViewWindowCommandRuntime.Apply(windowIntent);

            switch (command.Type)
            {
                case "navigate":
                {
                    WebViewNavigationIntent navigationIntent;
                    if (!TryCreateWebViewNavigationIntent(command, out navigationIntent))
                        return false;
                    return HandleNavigate(navigationIntent);
                }
                case "control.update":
                    return HandleControlUpdate(command.Payload);
                case "sync.quick":
                case "sync.continuous":
                case "sync.once":
                case "sync.toggleAnalysis":
                case "sync.swapOrder":
                case "sync.rebuild":
                case "sync.clearBoard":
                case "board.select":
                    return HandleControlCenterAction(command);
                case "rules.openManual":
                    return OpenWebViewManual();
                case "about.openRepository":
                    OpenExternalUri(ReadBoardRepositoryUrl);
                    return false;
                default:
                    if (IsValidWebViewIdentityCommand(command))
                        return HandleWebViewIdentityCommand(command);
                    return HandleWebViewSettingsCommand(command);
            }
        }

        private bool DispatchWebViewUpdateIntent(ReadBoardUpdateIntent intent)
        {
            switch (intent)
            {
                case ReadBoardUpdateIntent.Check:
                    if (webViewUpdateState.Open
                        && (webViewUpdateState.Status == "checking"
                            || webViewUpdateState.Status == "processing"))
                        return false;
                    _ = CheckForWebViewUpdateAsync();
                    return false;
                case ReadBoardUpdateIntent.Close:
                    return CloseWebViewUpdate();
                case ReadBoardUpdateIntent.Install:
                    if (ShouldPublishWebViewUpdateInstallRejection(
                        webViewUpdateState.Open,
                        webViewUpdateState.Status))
                        return true;
                    _ = InstallWebViewUpdateAsync();
                    return false;
                case ReadBoardUpdateIntent.OpenDownload:
                    if (ShouldPublishWebViewUpdateOpenDownloadRejection(
                        webViewUpdateState.Open,
                        webViewUpdateState.OpenDownloadEnabled))
                        return true;
                    OpenWebViewUpdateDownload();
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent));
            }
        }
        internal static bool ShouldPublishWebViewUpdateInstallRejection(
            bool open,
            string status)
        {
            return !open || status != "available";
        }
        internal static bool IsWebViewUpdateCloseAllowed(bool open, bool closeEnabled)
        {
            return !open || closeEnabled;
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
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    if (!IsStrictWebViewCommandRoot(document.RootElement))
                        return false;
                    command = document.RootElement.Deserialize<ReadBoardUiCommand>(WebViewJsonOptions);
                    return IsValidWebViewCommand(command);
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsStrictWebViewCommandRoot(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            bool typeSeen = false;
            bool payloadSeen = false;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Name == "type")
                {
                    if (typeSeen)
                        return false;
                    typeSeen = true;
                }
                else if (property.Name == "payload")
                {
                    if (payloadSeen)
                        return false;
                    payloadSeen = true;
                }
                else
                {
                    return false;
                }
            }
            return typeSeen;
        }

        internal static bool TryCreateWebViewNavigationIntent(
            ReadBoardUiCommand command,
            out WebViewNavigationIntent intent)
        {
            intent = null;
            if (command == null
                || command.Type != "navigate"
                || command.Payload.ValueKind != JsonValueKind.Object
                || CountProperties(command.Payload) != 1)
                return false;

            JsonElement pageValue;
            WebViewPage page;
            if (!command.Payload.TryGetProperty("page", out pageValue)
                || pageValue.ValueKind != JsonValueKind.String
                || !WebViewPageNames.TryParse(pageValue.GetString(), out page))
                return false;

            intent = new WebViewNavigationIntent(page);
            return true;
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
                {
                    WebViewNavigationIntent navigationIntent;
                    return TryCreateWebViewNavigationIntent(command, out navigationIntent);
                }
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
            if (key == "auto-play")
                return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False;
            if (key == "color")
                return IsAllowedString(value, "black", "white", "auto");
            if (key == "placement")
                return IsAllowedString(value, "direct", "engine");
            int numeric;
            if (key == "ai-time")
                return TryReadInteger(value, true, 0, out numeric);
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

            if ((key == "two-way" || key == "show-on-board")
                && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                intent = key == "two-way"
                    ? ControlCenterIntent.SetTwoWaySync(value.GetBoolean())
                    : ControlCenterIntent.SetShowOnBoard(value.GetBoolean());
                return true;
            }

            if (key == "auto-play"
                && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                intent = ControlCenterIntent.SetAutoPlayEnabled(value.GetBoolean());
                return true;
            }

            if (key == "color" && value.ValueKind == JsonValueKind.String)
            {
                AutoPlayColorMode colorMode;
                if (!TryParseAutoPlayColorMode(value.GetString(), out colorMode))
                    return false;
                intent = ControlCenterIntent.SetAutoPlayColor(colorMode);
                return true;
            }

            if (key == "placement" && value.ValueKind == JsonValueKind.String)
            {
                AutoPlayMoveMode moveMode;
                if (!TryParseAutoPlayMoveMode(value.GetString(), out moveMode))
                    return false;
                intent = ControlCenterIntent.SetAutoPlayMoveMode(moveMode);
                return true;
            }

            if (key == "ai-time" && value.ValueKind == JsonValueKind.String)
            {
                int numeric;
                if (!TryReadInteger(value, true, 0, out numeric))
                    return false;
                intent = ControlCenterIntent.SetAiTime(value.GetString());
                return true;
            }

            if (key == "playouts" && value.ValueKind == JsonValueKind.String)
            {
                int numeric;
                if (!TryReadInteger(value, true, 0, out numeric))
                    return false;
                intent = ControlCenterIntent.SetPlayouts(value.GetString());
                return true;
            }

            if (key == "first-policy" && value.ValueKind == JsonValueKind.String)
            {
                int numeric;
                if (!TryReadInteger(value, true, 0, out numeric))
                    return false;
                intent = ControlCenterIntent.SetFirstPolicy(value.GetString());
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

        internal static bool TryCreateControlCenterActionIntent(
            ReadBoardUiCommand command,
            out ControlCenterActionIntent intent)
        {
            intent = null;
            if (command == null)
                return false;

            switch (command.Type)
            {
                case "sync.quick":
                    if (!HasEmptyPayload(command.Payload))
                        return false;
                    intent = ControlCenterActionIntent.QuickSync();
                    return true;
                case "sync.continuous":
                    if (!HasEmptyPayload(command.Payload))
                        return false;
                    intent = ControlCenterActionIntent.ContinuousSync();
                    return true;
                case "sync.once":
                    if (!HasEmptyPayload(command.Payload))
                        return false;
                    intent = ControlCenterActionIntent.OneTimeSync();
                    return true;
                case "sync.toggleAnalysis":
                    if (!HasEmptyPayload(command.Payload))
                        return false;
                    intent = ControlCenterActionIntent.ToggleAnalysis();
                    return true;
                case "sync.swapOrder":
                    if (!HasEmptyPayload(command.Payload))
                        return false;
                    intent = ControlCenterActionIntent.SwapOrder();
                    return true;
                case "sync.rebuild":
                    if (!HasEmptyPayload(command.Payload))
                        return false;
                    intent = ControlCenterActionIntent.ForceRebuild();
                    return true;
                case "sync.clearBoard":
                    if (!HasEmptyPayload(command.Payload))
                        return false;
                    intent = ControlCenterActionIntent.ClearBoard();
                    return true;
                case "board.select":
                    return TryCreateBoardSelectionIntent(command.Payload, out intent);
                default:
                    return false;
            }
        }

        private static bool TryCreateBoardSelectionIntent(
            JsonElement payload,
            out ControlCenterActionIntent intent)
        {
            intent = null;
            if (payload.ValueKind != JsonValueKind.Object
                || CountProperties(payload) != 1
                || !payload.TryGetProperty("mode", out JsonElement modeValue)
                || modeValue.ValueKind != JsonValueKind.String)
                return false;

            switch (modeValue.GetString())
            {
                case "inside":
                    intent = ControlCenterActionIntent.SelectBoard(
                        ControlCenterBoardSelectionMode.Inside);
                    return true;
                case "rectangle":
                    intent = ControlCenterActionIntent.SelectBoard(
                        ControlCenterBoardSelectionMode.Rectangle);
                    return true;
                case "line1":
                    intent = ControlCenterActionIntent.SelectBoard(
                        ControlCenterBoardSelectionMode.Line1);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseAutoPlayColorMode(
            string value,
            out AutoPlayColorMode mode)
        {
            switch (value)
            {
                case "black":
                    mode = AutoPlayColorMode.ManualBlack;
                    return true;
                case "white":
                    mode = AutoPlayColorMode.ManualWhite;
                    return true;
                case "auto":
                    mode = AutoPlayColorMode.FoxAuto;
                    return true;
                default:
                    mode = default(AutoPlayColorMode);
                    return false;
            }
        }

        private static bool TryParseAutoPlayMoveMode(
            string value,
            out AutoPlayMoveMode mode)
        {
            switch (value)
            {
                case "direct":
                    mode = AutoPlayMoveMode.FirstCandidate;
                    return true;
                case "engine":
                    mode = AutoPlayMoveMode.GenmoveAnalyze;
                    return true;
                default:
                    mode = default(AutoPlayMoveMode);
                    return false;
            }
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

        private bool HandleNavigate(WebViewNavigationIntent intent)
        {
            if (intent == null)
                return false;
            return NavigateWebViewPage(intent.Page);
        }

        private bool NavigateWebViewPage(WebViewPage page)
        {
            string pageName = WebViewPageNames.ToWireName(page);
            if (webViewState.Page == pageName)
                return false;
            webViewState.Page = pageName;
            return true;
        }

        private bool OpenWebViewManual()
        {
            try
            {
                Process.Start(new ProcessStartInfo(getLangStr("helpFile")) { UseShellExecute = true });
                return false;
            }
            catch (Exception)
            {
                webViewSettingsDialog = CreateWebViewMessageDialog(
                    "WebView_manualOpenFailedTitle",
                    "noHelpFile");
                return true;
            }
        }

        private void ShowWebViewMessage(string titleKey, string messageKey)
        {
            webViewSettingsDialog = CreateWebViewMessageDialog(titleKey, messageKey);
            AddWebViewSemanticLog(
                "WARN",
                SemanticMessage.CreateLog("WARN", messageKey));
            PostWebViewState();
        }

        private bool HandleControlUpdate(JsonElement payload)
        {
            ControlCenterIntent controlCenterIntent;
            if (TryCreateControlCenterIntent(payload, out controlCenterIntent))
            {
                if (controlCenterIntent.Kind == ControlCenterIntentKind.SetAutoPlayColor
                    && controlCenterIntent.AutoPlayColorMode == AutoPlayColorMode.FoxAuto
                    && string.IsNullOrWhiteSpace(foxIdentitySelection.EffectiveIdentitySignature))
                {
                    OpenWebViewIdentity(true);
                    return true;
                }
                return ApplyControlCenterIntent(controlCenterIntent).ShouldPublishSnapshot;
            }

            return false;
        }

        private bool HandleControlCenterAction(ReadBoardUiCommand command)
        {
            ControlCenterActionIntent intent;
            if (!TryCreateControlCenterActionIntent(command, out intent))
                return false;
            ControlCenterActionApplyResult result = ApplyControlCenterAction(intent);
            return result.ShouldPublishSnapshot;
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

        private static int CountProperties(JsonElement value)
        {
            int count = 0;
            foreach (JsonProperty property in value.EnumerateObject())
                count++;
            return count;
        }

        private void MainFormWebView_Resize(object sender, EventArgs e)
        {
            bool maximized = WindowState == FormWindowState.Maximized;
            if (webViewState.Shell.Maximized == maximized)
                return;
            webViewState.Shell.Maximized = maximized;
            if (suppressWebViewResizePublication)
                return;
            PostWebViewState();
        }

        private void ApplyWebViewWindowState(WebViewWindowState windowState)
        {
            FormWindowState formWindowState;
            switch (windowState)
            {
                case WebViewWindowState.Normal:
                    formWindowState = FormWindowState.Normal;
                    break;
                case WebViewWindowState.Minimized:
                    formWindowState = FormWindowState.Minimized;
                    break;
                case WebViewWindowState.Maximized:
                    formWindowState = FormWindowState.Maximized;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(windowState));
            }

            bool previous = suppressWebViewResizePublication;
            suppressWebViewResizePublication = true;
            try
            {
                WindowState = formWindowState;
                webViewState.Shell.Maximized = formWindowState == FormWindowState.Maximized;
            }
            finally
            {
                suppressWebViewResizePublication = previous;
            }
        }

        private sealed class MainFormWebViewWindowAdapter : IWebViewWindowAdapter
        {
            private readonly MainForm owner;

            public MainFormWebViewWindowAdapter(MainForm owner)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public WebViewWindowState State
            {
                get
                {
                    if (owner.WindowState == FormWindowState.Maximized)
                        return WebViewWindowState.Maximized;
                    if (owner.WindowState == FormWindowState.Minimized)
                        return WebViewWindowState.Minimized;
                    return WebViewWindowState.Normal;
                }
            }

            public void SetState(WebViewWindowState state)
            {
                owner.ApplyWebViewWindowState(state);
            }

            public void Close()
            {
                owner.Close();
            }
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
            webViewStatePublisher.Request();
        }

        private void PostWebViewStateCore()
        {
            if (webView == null || webView.CoreWebView2 == null)
                return;

            webView.CoreWebView2.PostWebMessageAsJson(SerializeWebViewState(BuildWebViewState()));
        }

        private ReadBoardUiState BuildWebViewState()
        {
            ControlCenterRuntimeSnapshot controlCenter = controlCenterRuntime.Snapshot;
            bool? targetWindowValid = controlCenter.TargetWindowValid;
            return new ReadBoardUiState
            {
                Page = webViewState.Page,
                Language = Program.language,
                Text = BuildWebViewText(),
                Shell = new ReadBoardShellState
                {
                    Version = "v" + AppReleaseVersion.GetCurrentVersion(),
                    Theme = ResolveWebViewTheme(Program.CurrentConfig.ColorMode),
                    Connected = controlCenter.HostConnected,
                    SyncStatus = ResolveWebViewMessage(
                        ResolveWebViewSyncStatusKey(
                            controlCenter.HostConnected,
                            controlCenter.QuickSyncActive || controlCenter.ContinuousSyncActive)),
                    HostStatus = ResolveWebViewMessage(
                        controlCenter.HostConnected
                            ? "WebView_hostConnected"
                            : "WebView_hostModeStarted"),
                    TargetStatus = ResolveWebViewMessage(ResolveWebViewTargetStatusKey(targetWindowValid)),
                    BoardStatus = ResolveWebViewMessage(
                        controlCenter.BoardRegionRecognized
                            ? "WebView_boardRecognized"
                            : "WebView_waitBoardRecognition"),
                    PlacementStatus = ResolveWebViewMessage(
                        controlCenter.PlacementRegionResolved
                            ? "WebView_placementResolved"
                            : "WebView_placementUnavailable"),
                    LastSync = controlCenter.LastSync ?? "--:--:--",
                    StoneCount = controlCenter.StoneCount,
                    Duration = controlCenter.Duration ?? "--",
                    TargetWindowValid = targetWindowValid,
                    BoardRegionRecognized = controlCenter.BoardRegionRecognized,
                    PlacementRegionResolved = controlCenter.PlacementRegionResolved,
                    Maximized = WindowState == FormWindowState.Maximized,
                    MaximizeLabel = ResolveWebViewMessage(
                        WindowState == FormWindowState.Maximized ? "WebView_restore" : "WebView_maximize")
                },
                ControlCenter = BuildControlCenterState(targetWindowValid == true),
                Settings = GetWebViewSettingsState(),
                Update = GetWebViewUpdateState(),
                Identity = GetWebViewIdentityState(),
                Dialog = GetWebViewSettingsDialogState(),
                Logs = BuildWebViewLogs()
            };
        }

        private List<ReadBoardUiLogEntry> BuildWebViewLogs()
        {
            return ResolveWebViewLogs(
                webViewLogs,
                getLangStr,
                Program.GetDefaultLanguageText);
        }

        internal static List<ReadBoardUiLogEntry> ResolveWebViewLogs(
            IEnumerable<ReadBoardUiLogEntry> entries,
            Func<string, string> getLocalizedText,
            Func<string, string> getDefaultText)
        {
            List<ReadBoardUiLogEntry> logs = new List<ReadBoardUiLogEntry>();
            foreach (ReadBoardUiLogEntry entry in entries)
            {
                string message = entry.Message;
                if (!string.IsNullOrWhiteSpace(entry.MessageKey))
                {
                    message = SemanticMessageResolver.Resolve(
                        new SemanticMessage(entry.MessageKey, entry.Arguments, entry.DiagnosticDetail),
                        getLocalizedText,
                        getDefaultText);
                }
                else if (!string.IsNullOrWhiteSpace(entry.DiagnosticDetail))
                {
                    message = string.IsNullOrWhiteSpace(message)
                        ? entry.DiagnosticDetail
                        : message + ": " + entry.DiagnosticDetail;
                }
                logs.Add(new ReadBoardUiLogEntry
                {
                    Time = entry.Time,
                    Level = entry.Level,
                    Message = message,
                    MessageKey = entry.MessageKey,
                    DiagnosticDetail = entry.DiagnosticDetail,
                    Arguments = entry.Arguments
                });
            }
            return logs;
        }

        private static IDictionary<string, string> BuildWebViewText()
        {
            var text = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in Program.langItems)
            {
                if (entry.Key is string key)
                    text[key] = Program.ResolveLanguageText(key);
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
                room = string.IsNullOrWhiteSpace(controlCenter.YikeWindowContext.RoomToken) ? "--" : controlCenter.YikeWindowContext.RoomToken;
                moves = controlCenter.YikeWindowContext.MoveNumber;
            }
            else if (controlCenter.Platform == SyncMode.Fox
                || controlCenter.Platform == SyncMode.FoxBackgroundPlace)
            {
                FoxWindowContext foxWindowContext = controlCenter.FoxWindowContext;
                room = string.IsNullOrWhiteSpace(foxWindowContext.RoomToken) ? "--" : foxWindowContext.RoomToken;
                moves = foxWindowContext.ResolveDisplayedMoveNumber();
            }

            string platformToken = ControlCenterPreferences.ToPlatformToken(controlCenter.Platform);
            bool foxAutoPlay = controlCenter.AutoPlayColorMode == AutoPlayColorMode.FoxAuto;
            return new ReadBoardControlCenterState
            {
                Platform = platformToken,
                PlatformLabel = ResolveWebViewMessage(ResolveWebViewPlatformKey(controlCenter.Platform)),
                Room = room,
                Moves = moves.HasValue ? moves.Value.ToString() : "--",
                NextTurn = ResolveWebViewNextTurnText(controlCenter.TitleTurn),
                TitleBound = targetWindowValid,
                BindingStatus = ResolveWebViewMessage(targetWindowValid ? "WebView_bound" : "WebView_notBound"),
                BoardSize = ControlCenterPreferences.ToBoardSizeToken(controlCenter.BoardSizeKind),
                BoardWidth = controlCenter.BoardWidth,
                BoardHeight = controlCenter.BoardHeight,
                TwoWaySync = controlCenter.TwoWaySync,
                AutoPlay = controlCenter.AutoPlayEnabled,
                Color = controlCenter.AutoPlayColorMode == AutoPlayColorMode.FoxAuto
                    ? "auto"
                    : controlCenter.AutoPlayColorMode == AutoPlayColorMode.ManualWhite ? "white" : "black",
                Placement = controlCenter.AutoPlayMoveMode == AutoPlayMoveMode.GenmoveAnalyze ? "engine" : "direct",
                AiTime = controlCenter.AiTimeValue,
                Playouts = controlCenter.PlayoutsValue,
                FirstPolicy = controlCenter.FirstPolicyValue,
                FirstPolicyEnabled = controlCenter.FirstPolicyEnabled,
                ColorEnabled = controlCenter.ManualColorEnabled,
                AutoColorEnabled = controlCenter.FoxAutoColorEnabled,
                PlacementEnabled = controlCenter.MoveModeEnabled,
                AiTimeEnabled = controlCenter.AiTimeEnabled,
                PlayoutsEnabled = controlCenter.PlayoutsEnabled,
                AutoPlayColorStatus = foxAutoPlay
                    ? ResolveWebViewMessage(ResolveAutoPlayColorStatusKey(controlCenter.AutoPlayColorStatus))
                    : string.Empty,
                PlayColorKnown = controlCenter.AutoPlayColorResolution != null
                    && controlCenter.AutoPlayColorResolution.IsKnown,
                ShowOnBoard = controlCenter.ShowOnBoard,
                QuickSyncActive = controlCenter.QuickSyncActive,
                ContinuousSyncActive = controlCenter.ContinuousSyncActive,
                QuickSyncLabel = ResolveWebViewMessage(
                    controlCenter.QuickSyncActive ? "WebView_stopQuickSync" : "WebView_quickSync"),
                ContinuousSyncLabel = ResolveWebViewMessage(
                    controlCenter.ContinuousSyncActive
                        ? "WebView_stopContinuousSyncLabel"
                        : "WebView_continuousSyncLabel",
                    Program.timeinterval),
                QuickSyncEnabled = controlCenter.QuickSyncEnabled,
                ContinuousSyncEnabled = controlCenter.ContinuousSyncEnabled,
                OneTimeSyncEnabled = controlCenter.OneTimeSyncEnabled,
                SyncInterval = Program.timeinterval,
                AnalysisRunning = controlCenter.AnalysisRunning,
                AnalysisLabel = ResolveWebViewMessage(
                    controlCenter.AnalysisRunning ? "WebView_pauseAnalysis" : "WebView_resumeAnalysis"),
                AnalysisStateAvailable = controlCenter.AnalysisStateAvailable,
                AnalysisToggleEnabled = controlCenter.AnalysisToggleEnabled,
                SwapOrderEnabled = controlCenter.SwapOrderEnabled,
                ForceRebuildEnabled = controlCenter.ForceRebuildEnabled,
                ClearBoardEnabled = controlCenter.ClearBoardEnabled,
                BoardSelectionInsideEnabled = controlCenter.BoardSelectionInsideEnabled,
                BoardSelectionRectangleEnabled = controlCenter.BoardSelectionRectangleEnabled,
                BoardSelectionLine1Enabled = controlCenter.BoardSelectionLine1Enabled,
                ConfigurationEnabled = controlCenter.ConfigurationEnabled,
                TwoWaySyncEnabled = controlCenter.TwoWaySyncEnabled,
                AutoPlayToggleEnabled = controlCenter.AutoPlayToggleEnabled,
                AutoPlayControlsEnabled = controlCenter.AutoPlayControlsEnabled,
                CustomBoardSizeEnabled = controlCenter.CustomBoardSizeEnabled,
                CustomBoardDimensionsEnabled = controlCenter.CustomBoardDimensionsEnabled,
                PreferencesSaved = controlCenter.PreferencesSaved,
                PreferencesStatus = ResolveWebViewMessage(
                    controlCenter.PreferencesSaved
                        ? "WebView_preferencesSaved"
                        : "WebView_preferencesNotSaved"),
                PersistenceError = controlCenter.PersistenceError,
                IdentityEnabled = controlCenter.IdentityEnabled,
                ShowOnBoardEnabled = controlCenter.ShowOnBoardEnabled
            };
        }

        private static string ResolveWebViewMessage(string key, params object[] arguments)
        {
            return Program.ResolveSemanticMessage(SemanticMessage.Create(key, arguments));
        }

        internal static string ResolveWebViewSyncStatusKey(
            bool communicationEstablished,
            bool activeSync)
        {
            if (activeSync)
                return "WebView_syncing";
            return communicationEstablished ? "WebView_ready" : "WebView_hostModeStarted";
        }

        private static string ResolveWebViewNextTurnText(MainWindowTitleTurn titleTurn)
        {
            if (titleTurn == MainWindowTitleTurn.Black)
                return ResolveWebViewMessage("WebView_black");
            if (titleTurn == MainWindowTitleTurn.White)
                return ResolveWebViewMessage("WebView_white");
            return "--";
        }

        private static string ResolveWebViewTargetStatusKey(bool? targetWindowValid)
        {
            return targetWindowValid == true
                ? "WebView_targetValid"
                : targetWindowValid == false ? "WebView_targetInvalid" : "WebView_waitTarget";
        }

        private static string ResolveWebViewPlatformKey(SyncMode platform)
        {
            switch (ControlCenterPreferences.ToPlatformToken(platform))
            {
                case "fox": return "MainForm_rdoFox";
                case "foxBackground": return "MainForm_rdoFoxBack";
                case "yike": return "MainForm_rdoYike";
                case "yicheng": return "MainForm_rdoTygem";
                case "sina": return "MainForm_rdoSina";
                case "otherBackground": return "MainForm_rdoBack";
                case "otherForeground": return "MainForm_rdoFore";
                default: return "WebView_notSelected";
            }
        }

        private static string ResolveAutoPlayColorStatusKey(AutoPlayColorStatus status)
        {
            switch (status)
            {
                case AutoPlayColorStatus.Unconfigured:
                    return "MainForm_autoPlayColorStatusUnconfigured";
                case AutoPlayColorStatus.RecognizedBlack:
                    return "MainForm_autoPlayColorStatusBlack";
                case AutoPlayColorStatus.RecognizedWhite:
                    return "MainForm_autoPlayColorStatusWhite";
                case AutoPlayColorStatus.UnsupportedPlatform:
                    return "MainForm_autoPlayColorStatusUnsupported";
                case AutoPlayColorStatus.Spectating:
                    return "MainForm_autoPlayColorStatusSpectating";
                default:
                    return "MainForm_autoPlayColorStatusWaiting";
            }
        }

        private static string FormatWebViewDuration(TimeSpan duration)
        {
            long milliseconds = Math.Max(0L, (long)Math.Round(duration.TotalMilliseconds));
            return milliseconds.ToString(CultureInfo.InvariantCulture) + " ms";
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

        private void ResetWebViewSyncState()
        {
            ApplyControlCenterSessionObservation(
                new ControlCenterSessionObservation(
                    controlCenterRuntime.BeginSessionObservationGeneration())
                    .ClearRuntimeFrame());
        }

        internal static void ResetShellSyncState(ReadBoardShellState shell)
        {
            shell.BoardRegionRecognized = false;
            shell.PlacementRegionResolved = false;
            shell.LastSync = null;
            shell.StoneCount = 0;
        }

        private void AddWebViewSemanticLog(
            string level,
            SemanticMessage message)
        {
            if (message == null)
                return;

            if (webViewLogs.Count == 100)
                webViewLogs.Dequeue();
            webViewLogs.Enqueue(new ReadBoardUiLogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Level = message.Level ?? level,
                MessageKey = message.Key,
                Arguments = message.Arguments,
                DiagnosticDetail = message.DiagnosticDetail
            });
        }
    }
}
