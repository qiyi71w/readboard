using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
        private static readonly JsonSerializerOptions WebViewJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly ReadBoardUiState webViewState = new ReadBoardUiState();
        private readonly Queue<ReadBoardUiLogEntry> webViewLogs = new Queue<ReadBoardUiLogEntry>();
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
            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();
                return true;
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(
                    "ReadBoard 需要 Microsoft Edge WebView2 Runtime。请安装 Evergreen Runtime 后重新启动。",
                    "ReadBoard 无法启动",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Dispose();
                return false;
            }
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
                    "WebView2 初始化失败：" + ex.Message,
                    "ReadBoard 无法启动",
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
                throw new FileNotFoundException("找不到 WebView 主页面。", entryPoint);

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
                    HandleControlUpdate(command.Payload);
                    break;
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
                case "about.checkUpdate":
                    _ = CheckForWebViewUpdateAsync();
                    break;
                case "update.close":
                    CloseWebViewUpdate();
                    break;
                case "update.install":
                    _ = InstallWebViewUpdateAsync();
                    break;
                case "update.openDownload":
                    OpenWebViewUpdateDownload();
                    break;
                default:
                    if (!HandleWebViewIdentityCommand(command))
                        HandleWebViewSettingsCommand(command);
                    break;
            }

            PostWebViewState();
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
            if (key == "platform")
                return IsAllowedString(value, "fox", "foxBackground", "yike", "yicheng", "sina", "otherBackground", "otherForeground");
            if (key == "boardSize")
                return IsAllowedString(value, "19", "13", "9", "custom");
            if (key == "board-width" || key == "board-height")
            {
                int dimension;
                return TryReadInteger(value, false, 2, out dimension) && dimension <= 25;
            }
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
                    Title = "无法打开说明",
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

        private void HandleControlUpdate(JsonElement payload)
        {
            JsonElement keyValue = payload.GetProperty("key");
            JsonElement value = payload.GetProperty("value");
            string key = keyValue.GetString();
            if (key == "platform")
                UpdatePlatform(value);
            else if (key == "boardSize")
                UpdateBoardSize(value);
            else if (key == "board-width")
                UpdateCustomBoardDimension(value, txtBoardWidth);
            else if (key == "board-height")
                UpdateCustomBoardDimension(value, txtBoardHeight);
            else if (key == "two-way")
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
        }

        private void UpdatePlatform(JsonElement value)
        {
            if (HasActiveSyncOperation() || value.ValueKind != JsonValueKind.String)
                return;
            switch (value.GetString())
            {
                case "fox": rdoFox.Checked = true; break;
                case "foxBackground": rdoFoxBack.Checked = true; break;
                case "yike": rdoYike.Checked = true; break;
                case "yicheng": rdoTygem.Checked = true; break;
                case "sina": rdoSina.Checked = true; break;
                case "otherBackground": rdoBack.Checked = true; break;
                case "otherForeground": rdoFore.Checked = true; break;
            }
        }

        private void UpdateBoardSize(JsonElement value)
        {
            if (HasActiveSyncOperation() || value.ValueKind != JsonValueKind.String)
                return;
            switch (value.GetString())
            {
                case "19": rdo19x19.Checked = true; break;
                case "13": rdo13x13.Checked = true; break;
                case "9": rdo9x9.Checked = true; break;
                case "custom":
                    if (UsesManualSelectionType(CurrentSyncType))
                        rdoOtherBoard.Checked = true;
                    break;
            }
        }

        private void UpdateCustomBoardDimension(JsonElement value, TextBox control)
        {
            int parsed;
            if (HasActiveSyncOperation()
                || !rdoOtherBoard.Checked
                || !TryReadInteger(value, false, 2, out parsed)
                || parsed > 25)
                return;
            control.Text = parsed.ToString();
            ResetWebViewSyncState();
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
            if (webView == null || webView.CoreWebView2 == null)
                return;

            webView.CoreWebView2.PostWebMessageAsJson(SerializeWebViewState(BuildWebViewState()));
        }

        private ReadBoardUiState BuildWebViewState()
        {
            bool? targetWindowValid = hwnd == IntPtr.Zero ? (bool?)null : IsWindow(hwnd);
            return new ReadBoardUiState
            {
                Page = webViewState.Page,
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

        private ReadBoardControlCenterState BuildControlCenterState(bool targetWindowValid)
        {
            string room = "--";
            int? moves = null;
            if (CurrentSyncType == TYPE_YIKE)
            {
                room = string.IsNullOrWhiteSpace(lastYikeWindowContext.RoomToken) ? "--" : lastYikeWindowContext.RoomToken;
                moves = lastYikeWindowContext.MoveNumber;
            }
            else if (IsFoxSyncType(CurrentSyncType))
            {
                room = string.IsNullOrWhiteSpace(lastFoxWindowContext.RoomToken) ? "--" : lastFoxWindowContext.RoomToken;
                moves = lastFoxWindowContext.ResolveDisplayedMoveNumber();
            }

            return new ReadBoardControlCenterState
            {
                Platform = ResolveWebViewPlatform(),
                Room = room,
                Moves = moves.HasValue ? moves.Value.ToString() : "--",
                NextTurn = ResolveWebViewNextTurn(),
                TitleBound = targetWindowValid,
                BoardSize = rdoOtherBoard.Checked ? "custom" : boardW.ToString(),
                BoardWidth = boardW,
                BoardHeight = boardH,
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
                ConfigurationEnabled = rdoFox.Enabled,
                TwoWaySyncEnabled = chkBothSync.Enabled,
                AutoPlayToggleEnabled = chkAutoPlay.Enabled,
                AutoPlayControlsEnabled = radioBlack.Enabled,
                CustomBoardDimensionsEnabled = txtBoardWidth.Enabled && rdoOtherBoard.Checked,
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

        private string ResolveWebViewPlatform()
        {
            switch (CurrentSyncType)
            {
                case TYPE_FOX: return "fox";
                case TYPE_FOX_BACKGROUND_PLACE: return "foxBackground";
                case TYPE_YIKE: return "yike";
                case TYPE_TYGEM: return "yicheng";
                case TYPE_SINA: return "sina";
                case TYPE_BACKGROUND: return "otherBackground";
                case TYPE_FOREGROUND: return "otherForeground";
                default: return "fox";
            }
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
