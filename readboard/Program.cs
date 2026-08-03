using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace readboard
{
    static class Program
    {
        public const int UiThemeClassic = AppConfig.ClassicUiThemeMode;
        public const int UiThemeOptimized = AppConfig.OptimizedUiThemeMode;
        private const string LegacyProtocolVersion = "220430";

        private static RuntimeContext runtimeContext;
        private static IAppConfigStore configStore;
        private static Hashtable defaultLangItems;
        private static ISyncSessionCoordinator sessionCoordinator;

        private static AppConfig Config
        {
            get { return runtimeContext.Config; }
        }

        public static RuntimeContext CurrentContext
        {
            get { return runtimeContext; }
        }

        public static IAppConfigStore ConfigStore
        {
            get { return configStore; }
        }

        internal static AppConfig CurrentConfig
        {
            get { return Config; }
        }

        internal static SessionState CurrentSession
        {
            get { return runtimeContext.Session; }
        }

        public static ISyncSessionCoordinator SessionCoordinator
        {
            get { return sessionCoordinator; }
        }

        // Legacy static facade for callers outside the current refactor scope.
        public static int blackPC
        {
            get { return Config.BlackOffset; }
            set { Config.BlackOffset = value; }
        }

        public static int whitePC
        {
            get { return Config.WhiteOffset; }
            set { Config.WhiteOffset = value; }
        }

        public static int blackZB
        {
            get { return Config.BlackPercent; }
            set { Config.BlackPercent = value; }
        }

        public static int whiteZB
        {
            get { return Config.WhitePercent; }
            set { Config.WhitePercent = value; }
        }

        public static bool useMag
        {
            get { return Config.UseMagnifier; }
            set { Config.UseMagnifier = value; }
        }

        public static bool verifyMove
        {
            get { return Config.VerifyMove; }
            set { Config.VerifyMove = value; }
        }

        public static bool showInBoard
        {
            get { return Config.ShowInBoard; }
            set { Config.ShowInBoard = value; }
        }

        public static bool showInBoardHint
        {
            get { return Config.ShowInBoardHint; }
            set { Config.ShowInBoardHint = value; }
        }

        public static bool autoMin
        {
            get { return Config.AutoMinimize; }
            set { Config.AutoMinimize = value; }
        }

        public static bool isScaled
        {
            get { return runtimeContext.IsScaled; }
            set { runtimeContext.IsScaled = value; }
        }

        public static string version
        {
            get { return Config.ProtocolVersion; }
        }

        public static string timename
        {
            get { return Config.SyncIntervalMs.ToString(); }
            set
            {
                int parsed;
                if (int.TryParse(value, out parsed))
                    Config.SyncIntervalMs = parsed;
            }
        }

        public static int timeinterval
        {
            get { return Config.SyncIntervalMs; }
            set { Config.SyncIntervalMs = value; }
        }

        public static bool useEnhanceScreen
        {
            get { return Config.UseEnhanceScreen; }
            set { Config.UseEnhanceScreen = value; }
        }

        public static bool playPonder
        {
            get { return Config.PlayPonder; }
            set { Config.PlayPonder = value; }
        }

        public static int uiThemeMode
        {
            get { return Config.UiThemeMode; }
            set { Config.UiThemeMode = value; }
        }

        public static Bitmap bitmap
        {
            get { return runtimeContext.BoardBitmap; }
        }

        public static string language
        {
            get { return runtimeContext.Language; }
            set { runtimeContext.Language = value; }
        }

        public static Hashtable langItems
        {
            get { return runtimeContext.LanguageItems; }
        }

        public static void ReplaceBitmap(Bitmap newBitmap)
        {
            runtimeContext.ReplaceBoardBitmap(newBitmap);
        }

        public static void DisposeBitmap()
        {
            runtimeContext.DisposeBoardBitmap();
        }

        internal static MainForm ResolveMainForm(MainForm preferredHost)
        {
            MainForm resolvedHost = GetUsableMainForm(preferredHost);
            if (resolvedHost != null)
                return resolvedHost;

            resolvedHost = GetUsableMainForm(Form.ActiveForm as MainForm);
            if (resolvedHost != null)
                return resolvedHost;

            foreach (Form openForm in Application.OpenForms)
            {
                resolvedHost = GetUsableMainForm(openForm as MainForm);
                if (resolvedHost != null)
                    return resolvedHost;
            }
            return null;
        }

        [STAThread]
        static void Main(string[] args)
        {
            LaunchOptions options;
            if (!LaunchOptions.TryParse(args, out options))
                return;

            InitializeRuntime(options);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetColorMode(GetSystemColorMode(Config.ColorMode));

            using (IReadBoardTransport transport = CreateTransport(options))
            {
                SessionCoordinatorScope.Run(
                    new SyncSessionCoordinator(transport, new LegacyProtocolAdapter()),
                    coordinator => sessionCoordinator = coordinator,
                    activeSessionCoordinator =>
                    {
                        MainForm mainForm = CreateMainForm(options, activeSessionCoordinator);
                        if (!mainForm.EnsureWebViewRuntimeAvailable())
                            return;
                        if (!TryStartSession(mainForm))
                            return;
                        mainForm.DrainStartupProtocolCommands();
                        if (mainForm.IsShutdownRequested)
                            return;
                        mainForm.NotifyProtocolReady();
                        mainForm.DrainStartupProtocolCommands();
                        if (mainForm.IsShutdownRequested)
                            return;
                        mainForm.ReplayStartupProtocolState();
                        mainForm.DrainStartupProtocolCommands();
                        if (mainForm.IsShutdownRequested)
                            return;
                        Application.Run(mainForm);
                    });
            }
        }

        public static void SaveAppConfig(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            AppConfig candidate = config.Clone();
            configStore.Save(candidate);
            runtimeContext.Config = candidate;
            runtimeContext.HasConfigFile = true;
        }

        private static IReadBoardTransport CreateTransport(LaunchOptions options)
        {
            if (options.TransportKind == TransportKind.Tcp)
                return new TcpTransport(options.TcpPort);
            return new PipeTransport();
        }

        private static MainForm CreateMainForm(
            LaunchOptions launchOptions,
            ISyncSessionCoordinator syncSessionCoordinator)
        {
            MainFormRuntimeComposer composer = new MainFormRuntimeComposer(runtimeContext.Session);
            return composer.Compose(launchOptions, syncSessionCoordinator);
        }

        private static void InitializeRuntime(LaunchOptions options)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string machineKey = GetMachineKey();
            configStore = new DualFormatAppConfigStore(baseDirectory, machineKey, LegacyProtocolVersion);
            AppConfigLoadResult loadResult = configStore.Load();
            runtimeContext = new RuntimeContext(options, loadResult.Config, new SessionState());
            runtimeContext.HasConfigFile = loadResult.HasExistingConfig;
            runtimeContext.Language = ResolveEffectiveLanguage(
                loadResult.Config.LanguagePreference,
                options.Language);
            AddDefaultLangItems();
            defaultLangItems = new Hashtable(langItems);
            LoadLanguageItems(baseDirectory, runtimeContext.Language);
        }

        internal static string ResolveEffectiveLanguage(string preference, string hostLanguage)
        {
            string normalizedPreference = AppConfig.NormalizeLanguagePreference(preference);
            if (normalizedPreference != AppConfig.FollowHostLanguage)
                return normalizedPreference;
            return AppConfig.IsSupportedLanguage(hostLanguage) ? hostLanguage : "cn";
        }
        internal static bool ApplyLanguagePreferenceValue(
            string preference,
            string hostLanguage,
            string currentLanguage,
            Action<string> setLanguage,
            Action reloadLanguageCatalog)
        {
            if (setLanguage == null)
                throw new ArgumentNullException("setLanguage");
            if (reloadLanguageCatalog == null)
                throw new ArgumentNullException("reloadLanguageCatalog");

            string effectiveLanguage = ResolveEffectiveLanguage(preference, hostLanguage);
            bool changed = !string.Equals(
                currentLanguage,
                effectiveLanguage,
                StringComparison.Ordinal);
            if (changed)
                setLanguage(effectiveLanguage);
            reloadLanguageCatalog();
            return changed;
        }

        internal static bool ApplyLanguagePreference(string preference)
        {
            return ApplyLanguagePreferenceValue(
                preference,
                runtimeContext.LaunchOptions.Language,
                runtimeContext.Language,
                delegate(string value) { runtimeContext.Language = value; },
                delegate
                {
                    langItems.Clear();
                    AddDefaultLangItems();
                    LoadLanguageItems(
                        AppDomain.CurrentDomain.BaseDirectory,
                        ResolveEffectiveLanguage(
                            preference,
                            runtimeContext.LaunchOptions.Language));
                });
        }
        internal static string GetDefaultLanguageText(string key)
        {
            if (string.IsNullOrEmpty(key)
                || defaultLangItems == null
                || !defaultLangItems.ContainsKey(key))
                return null;
            return defaultLangItems[key] as string;
        }
        internal static string ResolveLanguageText(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return key;

            string localized = null;
            if (runtimeContext != null && runtimeContext.LanguageItems != null)
                localized = runtimeContext.LanguageItems[key] as string;
            return SemanticMessageResolver.ResolveText(
                key,
                localized,
                GetDefaultLanguageText(key));
        }
        internal static string ResolveSemanticMessage(SemanticMessage message)
        {
            return SemanticMessageResolver.Resolve(
                message,
                ResolveLanguageText,
                GetDefaultLanguageText);
        }

        private static bool TryStartSession(IWin32Window owner)
        {
            try
            {
                sessionCoordinator.Start();
                return true;
            }
            catch
            {
                MessageBox.Show(owner, GetLangText("connectLizzieFailed"));
                return false;
            }
        }

        private static string GetMachineKey()
        {
            string machineName = Environment.GetEnvironmentVariable("computername");
            if (string.IsNullOrWhiteSpace(machineName))
                machineName = Environment.MachineName;
            return machineName.Replace("_", string.Empty);
        }

        private static void LoadLanguageItems(string baseDirectory, string languageName)
        {
            string fileName = "language_" + languageName + ".txt";
            string path = Path.Combine(baseDirectory, fileName);
            if (!File.Exists(path))
                return;
            ReadLangItemsFromFile(path);
        }

        private static void AddDefaultLangItems()
        {
            langItems["WebView_navControlCenter"] = "控制中心";
            langItems["WebView_windowControls"] = "窗口控制";
            langItems["WebView_minimize"] = "最小化";
            langItems["WebView_maximize"] = "最大化";
            langItems["WebView_mainNavigation"] = "主导航";
            langItems["WebView_rules"] = "规则说明";
            langItems["WebView_about"] = "关于";
            langItems["WebView_quickActions"] = "快速操作";
            langItems["WebView_status"] = "状态：";
            langItems["WebView_lastSync"] = "最后同步：";
            langItems["WebView_stoneCount"] = "棋子数：";
            langItems["WebView_duration"] = "耗时：";
            langItems["WebView_version"] = "版本：";
            langItems["WebView_platform"] = "平台：";
            langItems["WebView_moves"] = "手数：";
            langItems["WebView_nextTurn"] = "下一手：";
            langItems["WebView_title"] = "标题：";
            langItems["WebView_boardWidth"] = "棋盘宽度";
            langItems["WebView_boardHeight"] = "棋盘高度";
            langItems["WebView_syncSettings"] = "同步设置";
            langItems["WebView_stoneColor"] = "执子颜色";
            langItems["WebView_engineConditions"] = "引擎条件";
            langItems["WebView_seconds"] = "秒";
            langItems["WebView_boardSelection"] = "棋盘选择方式";
            langItems["WebView_logOutput"] = "日志输出";
            langItems["WebView_settingsDescription"] = "设置修改会先保留为草稿，保存后统一应用。";
            langItems["WebView_generalBehavior"] = "常规行为";
            langItems["WebView_autoMinimize"] = "同步后自动最小化";
            langItems["WebView_autoMinimizeDescription"] = "完成单次同步后最小化主窗口";
            langItems["WebView_backgroundAnalysis"] = "后台分析";
            langItems["WebView_backgroundAnalysisDescription"] = "双向同步时允许引擎在对手回合继续分析";
            langItems["WebView_magnifierDescription"] = "选择棋盘区域时显示局部放大";
            langItems["WebView_enhancedCaptureDescription"] = "尝试捕获桌面之外的窗口内容";
            langItems["WebView_placementValidation"] = "落子验证";
            langItems["WebView_placementValidationDescription"] = "落子后检查目标位置是否成功";
            langItems["WebView_recognitionParameters"] = "识别参数";
            langItems["WebView_syncInterval"] = "同步间隔";
            langItems["WebView_grayOffset"] = "灰度偏差";
            langItems["WebView_blackOffset"] = "黑棋颜色偏差";
            langItems["WebView_blackPercent"] = "黑棋识别百分比";
            langItems["WebView_whiteOffset"] = "白棋颜色偏差";
            langItems["WebView_whitePercent"] = "白棋识别百分比";
            langItems["WebView_appearanceDiagnostics"] = "外观与诊断";
            langItems["WebView_debugDiagnostics"] = "调试诊断";
            langItems["WebView_debugDiagnosticsDescription"] = "保存截图和识别过程以便排查问题";
            langItems["WebView_saveSettings"] = "保存设置";
            langItems["WebView_rulesDescription"] = "了解贴目模拟方式和当前同步限制。";
            langItems["WebView_komiRuleLimits"] = "贴目与规则限制";
            langItems["WebView_captureCountWarning"] = "ReadBoard 无法从目标客户端获取提子数。";
            langItems["WebView_japaneseScoringWarning"] = "直接使用日本规则数目可能导致结果不准确。";
            langItems["WebView_komiSimulationIntro"] = "需要模拟日本规则贴 6.5 目时，可在 KataGo 中使用以下组合：";
            langItems["WebView_areaScoring"] = "数子规则";
            langItems["WebView_komiSeven"] = "贴目 7.0";
            langItems["WebView_lastMoveCompensation"] = "收后方贴还 0.5 目";
            langItems["WebView_fullManual"] = "完整说明";
            langItems["WebView_fullManualDescription"] = "查看随程序发布的完整 RTF 使用说明。";
            langItems["WebView_openFullManual"] = "打开完整说明";
            langItems["WebView_aboutDescription"] = "查看版本、宿主关系和项目入口。";
            langItems["WebView_productSubtitle"] = "LizzieYzy-Next 棋盘同步工具";
            langItems["WebView_productDescription"] = "捕获第三方围棋客户端的棋盘，识别棋子，并将局面同步到宿主。";
            langItems["WebView_hostTool"] = "宿主工具";
            langItems["WebView_projectInfo"] = "项目信息";
            langItems["WebView_currentVersion"] = "当前版本";
            langItems["WebView_hostProject"] = "宿主项目";
            langItems["WebView_platformRuntime"] = "支持系统";
            langItems["WebView_projectRepository"] = "项目仓库";
            langItems["WebView_projectUpdates"] = "项目与更新";
            langItems["WebView_projectUpdatesDescription"] = "从项目仓库查看源码，或检查当前维护通道的新版本。";
            langItems["WebView_openRepository"] = "打开项目仓库";
            langItems["WebView_previewWaiting"] = "本地预览模式，等待宿主状态";
            langItems["WebView_restore"] = "还原";
            langItems["WebView_continuousSync"] = "持续同步";
            langItems["WebView_updateChecking"] = "正在检查可用更新";
            langItems["WebView_updateConnecting"] = "正在连接 GitHub Release，请稍候。";
            langItems["WebView_updateLatest"] = "当前已是最新版本";
            langItems["WebView_updateJustChecked"] = "刚刚完成检查";
            langItems["WebView_done"] = "完成";
            langItems["WebView_updateChannelNotice"] = "更新通道提示";
            langItems["WebView_noUpdateAvailable"] = "当前没有可安装的更新。";
            langItems["WebView_tryAgainLater"] = "请稍后重试。";
            langItems["WebView_preparingUpdate"] = "正在准备更新包";
            langItems["WebView_pleaseWait"] = "请稍候…";
            langItems["WebView_processing"] = "处理中…";
            langItems["WebView_installIncomplete"] = "安装未完成";
            langItems["WebView_updateIncomplete"] = "更新未完成，已切换为手动下载。";
            langItems["WebView_operationFailed"] = "操作失败";
            langItems["WebView_retryOrDownload"] = "可稍后重试或手动下载。";
            langItems["WebView_selectedIdentity"] = "已选择：";
            langItems["WebView_identityWindowHint"] = "请确认野狐棋局窗口可见，然后重新打开身份选择。";
            langItems["WebView_selectIdentity"] = "选择野狐身份";
            langItems["WebView_unnamedCandidate"] = "未命名候选";
            langItems["WebView_saved"] = "已保存";
            langItems["WebView_candidateRow"] = "候选玩家行";
            langItems["WebView_screenshot"] = "截图";
            langItems["SettingsForm_chkDisableShowInBoardShortcut"] = "关闭显示选点快捷键";
            langItems["connectLizzieFailed"] = "棋盘同步工具与Lizzie连接失败";
            langItems["WebViewRuntime_caption"] = "ReadBoard 无法启动";
            langItems["WebViewRuntime_heading"] = "缺少 Microsoft Edge WebView2 Runtime";
            langItems["WebViewRuntime_message"] = "ReadBoard 使用系统共享的 Evergreen Runtime。请先安装 Runtime，然后重试。";
            langItems["WebViewRuntime_openDownload"] = "打开官方下载页面";
            langItems["WebViewRuntime_retry"] = "重试";
            langItems["WebViewRuntime_exit"] = "退出";
            langItems["WebViewRuntime_openDownloadFailed"] = "无法打开 WebView2 Runtime 官方下载页面。";
            langItems["WebView_initializationFailed"] = "WebView2 初始化失败";
            langItems["WebView_mainPageMissing"] = "找不到 WebView 主页面。";
            langItems["WebView_manualOpenFailedTitle"] = "无法打开说明";
            langItems["WebView_resetDefaultsDescription"] = "将当前设置草稿恢复为默认值。此操作不会立即写入配置，仍需点击保存设置。";
            langItems["WebView_resetDefaults"] = "恢复默认";
            langItems["WebView_enableDiagnostics"] = "开启调试诊断";
            langItems["WebView_diagnosticsDescription"] = "调试诊断可能产生较大的文件。确认后仅修改当前设置草稿，保存设置后生效。";
            langItems["WebView_continueEnable"] = "继续开启";
            langItems["WebView_syncFailedTitle"] = "无法同步";
            langItems["WebView_recognitionFailedTitle"] = "识别失败";
            langItems["WebView_updateFetching"] = "正在获取最新版本信息…";
            langItems["WebView_hostedInstallUnsupported"] = "当前宿主不支持托管安装";
            langItems["WebView_manualDownload"] = "可打开 Release 页面手动下载更新。";
            langItems["WebView_updateStepDownload"] = "下载更新包";
            langItems["WebView_updateStepVerify"] = "校验更新包";
            langItems["WebView_updateStepNotifyHost"] = "通知宿主";
            langItems["WebView_updateStepHostInstall"] = "宿主安装";
            langItems["WebView_candidateRowNumber"] = "玩家行 {0}";
            langItems["WebView_integerAtLeast"] = "请输入不小于 {0} 的整数";
            langItems["WebView_integerRange"] = "请输入 {0}–{1} 之间的整数";
            langItems["WebView_continuousSyncLabel"] = "持续同步 ({0}ms)";
            langItems["WebView_stopContinuousSyncLabel"] = "停止持续同步 ({0}ms)";
            langItems["WebView_settingsSaveFailed"] = "保存设置失败";
            langItems["SettingsForm_invalidChoice"] = "设置值无效";
            langItems["WebView_settingsDurableSaveFailed"] = "保存设置失败，配置状态需要诊断";
            langItems["WebView_settingsEffectFailed"] = "设置已保存，但部分运行时效果未完成";
            langItems["WebView_language"] = "界面语言";
            langItems["WebView_languageDescription"] = "保存后立即应用";
            langItems["WebView_followHostLanguage"] = "跟随 LizzieYzy-Next";
            langItems["WebView_preferencesSaved"] = "偏好已保存";
            langItems["WebView_preferencesNotSaved"] = "当前选择已生效，但尚未保存";
            langItems["WebView_showInBoardHintForeground"] = "[前台]方式同步时不支持此功能。选点显示在原棋盘上后，原棋盘将无法落子。";
            langItems["WebView_showInBoardHintRestore"] = "可通过勾选“双向同步”选项恢复落子功能。";
            langItems["WebView_moveMode"] = "落子方式";
            langItems["WebView_hostConnected"] = "宿主通信正常";
            langItems["WebView_hostReadyLog"] = "宿主模式已启动，ReadBoard 就绪";
            langItems["WebView_hostModeStarted"] = "宿主模式已启动";
            langItems["WebView_ready"] = "就绪";
            langItems["WebView_syncing"] = "同步中";
            langItems["WebView_notSelected"] = "未选择";
            langItems["WebView_targetValid"] = "目标窗口有效";
            langItems["WebView_targetInvalid"] = "目标窗口已失效，请重新选择";
            langItems["WebView_waitTarget"] = "等待选择目标窗口";
            langItems["WebView_boardRecognized"] = "棋盘区域已识别";
            langItems["WebView_waitBoardRecognition"] = "等待首次棋盘识别";
            langItems["WebView_placementResolved"] = "落子区域已解析";
            langItems["WebView_placementUnavailable"] = "落子区域暂不可用";
            langItems["WebView_bound"] = "已绑定";
            langItems["WebView_notBound"] = "未绑定";
            langItems["WebView_black"] = "黑";
            langItems["WebView_white"] = "白";
            langItems["WebView_quickSync"] = "快速同步";
            langItems["WebView_stopQuickSync"] = "停止快速同步";
            langItems["WebView_stopContinuousSync"] = "停止持续同步";
            langItems["WebView_pauseAnalysis"] = "暂停分析";
            langItems["WebView_resumeAnalysis"] = "继续分析";
            langItems["WebView_unsavedChanges"] = "有尚未保存的更改";
            langItems["WebView_noUnsavedChanges"] = "当前没有未保存的更改";
            langItems["WebView_continuousSyncStarted"] = "开始持续同步";
            langItems["WebView_continuousSyncStopped"] = "持续同步已停止";
            langItems["WebView_quickSyncStarted"] = "开始快速同步";
            langItems["WebView_quickSyncStopped"] = "快速同步已停止";
            langItems["WebView_boardSent"] = "已识别并发送棋盘状态";
            langItems["keepSync"] = "持续同步";
            langItems["recgnizeFaild"] = "不能识别棋盘,请调整被同步棋盘大小后重新选择或尝试[框选1路线]";
            langItems["noSelectedBoard"] = "未选择棋盘";
            langItems["noSelectedBoardAndFailed"] = "未选择棋盘,同步失败";
            langItems["notRightBoard"] = "未选择正确的棋盘";
            langItems["stopSync"] = "停止同步";
            langItems["fastSync"] = "一键同步";
            langItems["helpFile"] = "readme.rtf";
            langItems["noHelpFile"] = "找不到说明文档,请检查Lizzie目录下[readboard]文件夹内的[readme.rtf]文件是否存在";
            langItems["komi65Describe"] = "由于同步时无法获取提子数,日本规则(数目)将变得不准确,需要同步日本规则贴6.5目的棋局时可在Katago中使用[数子+贴目7.0+收后方贴还0.5目]规则模拟";
            langItems["MainForm_rdoFox"] = "野狐";
            langItems["MainForm_rdoFoxBack"] = "野狐(后台落子)";
            langItems["MainForm_rdoYike"] = "弈客";
            langItems["MainForm_rdoTygem"] = "弈城";
            langItems["MainForm_rdoSina"] = "新浪";
            langItems["MainForm_rdoBack"] = "其他(后台)";
            langItems["MainForm_rdoFore"] = "其他(前台)";
            langItems["MainForm_btnSettings"] = "参数设置";
            langItems["MainForm_btnHelp"] = "帮助";
            langItems["MainForm_btnTheme"] = "主题";
            langItems["MainForm_btnCheckUpdate"] = "检查更新";
            langItems["MainForm_btnCheckUpdate_Checking"] = "检查中";
            langItems["MainForm_btnFastSync"] = "一键同步";
            langItems["MainForm_lblBoardSize"] = "棋盘:";
            langItems["MainForm_btnKomi65"] = "6.5目规则设置方法";
            langItems["MainForm_chkBothSync"] = "双向同步";
            langItems["MainForm_chkAutoPlay"] = "自动落子";
            langItems["MainForm_radioBlack"] = "执黑";
            langItems["MainForm_radioWhite"] = "执白";
            langItems["MainForm_radioAutoPlayColor"] = "自动";
            langItems["MainForm_btnFoxAutoPlayIdentity"] = "身份";
            langItems["MainForm_autoPlayColorStatusUnconfigured"] = "未配置";
            langItems["MainForm_autoPlayColorStatusWaiting"] = "待识别";
            langItems["MainForm_autoPlayColorStatusBlack"] = "识别:黑";
            langItems["MainForm_autoPlayColorStatusWhite"] = "识别:白";
            langItems["MainForm_autoPlayColorStatusUnsupported"] = "仅野狐";
            langItems["MainForm_autoPlayColorStatusSpectating"] = "观战禁用";
            langItems["MainForm_lblAutoPlayMoveMode"] = "落子方式:";
            langItems["MainForm_radioAutoPlayMoveFirst"] = "一选落子";
            langItems["MainForm_radioAutoPlayMoveGma"] = "引擎决策落子";
            langItems["MainForm_lblPlayCondition"] = "引擎自动落子条件:";
            langItems["MainForm_lblTime"] = "每手用时";
            langItems["MainForm_lblTotalVisits"] = "最大计算量(选填)";
            langItems["MainForm_lblBestMoveVisits"] = "首选计算量(选填)";
            langItems["MainForm_btnClickBoard"] = "选择棋盘(点击棋盘内部)";
            langItems["MainForm_btnCircleBoard"] = "框选棋盘";
            langItems["MainForm_btnCircleRow1"] = "框选1路线";
            langItems["MainForm_btnTogglePonder"] = "分析/停止";
            langItems["MainForm_chkShowInBoard"] = "原棋盘上显示选点";
            langItems["MainForm_btnOneTimeSync"] = "单次同步";
            langItems["MainForm_btnExchange"] = "交换顺序";
            langItems["MainForm_btnForceRebuild"] = "强制重建";
            langItems["MainForm_btnClearBoard"] = "清空棋盘";
            langItems["MainForm_btnKeepSync"] = "持续同步";
            langItems["MainForm_title"] = "棋盘同步工具";
            langItems["MainForm_titleTagFox"] = "野狐";
            langItems["MainForm_titleTagYike"] = "弈客";
            langItems["MainForm_titleTagRoom"] = "房间";
            langItems["MainForm_titleTagRecord"] = "棋谱";
            langItems["MainForm_titleTagSyncing"] = "同步中";
            langItems["MainForm_titleTagTitleMissing"] = "未抓到标题信息";
            langItems["MainForm_titleTagRecordEnd"] = "末手";
            langItems["MainForm_titleMoveFormatSingle"] = "第{0}手";
            langItems["MainForm_titleMoveFormatRecord"] = "第{0}/{1}手";
            langItems["MainForm_rdoCustomBoard"] = "自定义";
            langItems["MainForm_groupPlatform"] = "平台类型";
            langItems["MainForm_groupBoard"] = "棋盘规格";
            langItems["MainForm_groupSync"] = "同步与自动落子";
            langItems["MainForm_themeOptimized"] = "新版主题";
            langItems["MainForm_themeClassic"] = "默认主题";
            langItems["Update_upToDate"] = "已是最新版本";
            langItems["Update_retiredFinalVersion"] = "此通道已停止维护；最终维护版本";
            langItems["Update_upToDateRetired"] = "已是此系统通道的最终维护版本。";
            langItems["Update_outsideChannel"] = "当前版本高于此系统通道的已晋升版本；不会自动降级。";
            langItems["Update_noMatchingChannel"] = "当前 Windows 版本没有可用的维护通道。";
            langItems["Update_newerVersionRequiresWindows"] = "当前系统无法安装此主线版本";
            langItems["Update_checkFailed"] = "检查更新失败";
            langItems["Update_unknownError"] = "未知错误";
            langItems["Update_dialogTitle"] = "发现新版本";
            langItems["Update_currentVersion"] = "当前版本";
            langItems["Update_latestVersion"] = "最新版本";
            langItems["Update_releaseDate"] = "发布日期";
            langItems["Update_releaseNotes"] = "更新说明";
            langItems["Update_download"] = "去下载";
            langItems["Update_downloadAndInstall"] = "下载并安装";
            langItems["Update_downloading"] = "下载中...";
            langItems["Update_downloadingPackage"] = "正在下载更新包...";
            langItems["Update_verifyingPackage"] = "正在校验更新包...";
            langItems["Update_notifyingHost"] = "正在通知宿主安装...";
            langItems["Update_waitingForHostInstall"] = "等待宿主安装...";
            langItems["Update_cancelled"] = "更新准备已取消。";
            langItems["Update_operationAlreadyRunning"] = "更新准备正在进行中。";
            langItems["Update_handoffAlreadySent"] = "本进程已交接过更新包，后续请手动下载。";
            langItems["Update_prepareFailed"] = "更新包准备失败。";
            langItems["Update_handoffFailed"] = "向宿主交接更新包失败。";
            langItems["Update_hostInstalling"] = "宿主正在安装更新...";
            langItems["Update_hostCancelled"] = "宿主已取消安装。";
            langItems["Update_hostFailed"] = "宿主安装失败。";
            langItems["Update_hostTimedOut"] = "宿主长时间未响应。";
            langItems["Update_manualDownloadFallback"] = "已回退为手动下载，可点击“去下载”打开 release 页面。";
            langItems["Update_close"] = "关闭";
            langItems["Update_notProvided"] = "未提供";
            langItems["Update_releaseNotesUnavailable"] = "暂无更新说明。";
            langItems["Update_missingDownloadUrl"] = "未提供下载链接。";
            langItems["Update_invalidDownloadUrlFormat"] = "下载链接格式无效。";
            langItems["Update_unsupportedDownloadUrlScheme"] = "下载链接协议不受支持，仅允许 http 或 https。";
            langItems["Update_openDownloadFailed"] = "无法打开下载链接。";
            langItems["MagnifierForm_title"] = "放大镜";
            langItems["SettingsForm_title"] = "参数设置";
            langItems["SettingsForm_chkPonder"] = "后台思考";
            langItems["SettingsForm_chkMag"] = "使用放大镜";
            langItems["SettingsForm_chkVerifyMove"] = "验证落子以确保成功";
            langItems["SettingsForm_chkAutoMin"] = "同步后自动最小化";
            langItems["SettingsForm_lblBackForeOnly"] = "以下选项只对 其他(前台),其他(后台) 类型的同步生效:";
            langItems["SettingsForm_lblBlackOffsets"] = "黑色偏差(0-255)";
            langItems["SettingsForm_lblBlackPercents"] = "黑色占比(0-100)";
            langItems["SettingsForm_lblWhiteffsets"] = "白色偏差(0-255)";
            langItems["SettingsForm_lblWhitePercents"] = "白色占比(0-100)";
            langItems["SettingsForm_lblGrayOffsets"] = "灰度偏差(0-255)";
            langItems["SettingsForm_lblTips"] = "注意:所有参数都必须为整数";
            langItems["SettingsForm_lblTips1"] = "如某种颜色棋子识别过多,可尝试降低偏差或增大占比";
            langItems["SettingsForm_lblTips2"] = "如某种颜色棋子识别丢失,可尝试增大偏差或降低占比";
            langItems["SettingsForm_lblSyncInterval"] = "同步时间间隔(ms):";
            langItems["SettingsForm_btnReset"] = "恢复默认设置";
            langItems["SettingsForm_btnConfirm"] = "确认";
            langItems["SettingsForm_btnCancel"] = "取消";
            langItems["SettingsForm_chkEnhanceScreen"] = "强化截图";
            langItems["SettingsForm_chkDebugDiagnostics"] = "保存调试诊断";
            langItems["SettingsForm_btnOpenDebugDiagnostics"] = "打开调试目录";
            langItems["SettingsForm_debugDiagnosticsWarning"] = "保存调试诊断会产生大量文件，仅在排查问题时开启；不需要时请务必关闭。";
            langItems["SettingsForm_chkEnhanceScreen_ToolTip"] = "勾选可获取桌面外的截图,通常不需要(可能导致刷新降低,无法实时切换棋局等问题)";
            langItems["SettingsForm_chkPonder_ToolTip"] = "双向同步自动落子时,引擎在对手的回合计算";
            langItems["SettingsForm_lblColorMode"] = "颜色模式:";
            langItems["SettingsForm_rdoColorSystem"] = "跟随系统";
            langItems["SettingsForm_rdoColorDark"] = "深色";
            langItems["SettingsForm_rdoColorLight"] = "浅色";
            langItems["SettingsForm_colorModeRestartTip"] = "颜色模式已更改，重启后生效。";
            langItems["FoxAutoPlayIdentityDialog_title"] = "野狐自动模式";
            langItems["FoxAutoPlayIdentityDialog_lblPrompt"] = "请选择你在野狐当前房间里的玩家行。";
            langItems["FoxAutoPlayIdentityDialog_lblDetectedNicknames"] = "可选玩家行";
            langItems["FoxAutoPlayIdentityDialog_btnUseOnce"] = "本次使用";
            langItems["FoxAutoPlayIdentityDialog_btnSaveAndUse"] = "保存并使用";
            langItems["FoxAutoPlayIdentityDialog_btnClearSavedIdentity"] = "清除保存";
            langItems["FoxAutoPlayIdentityDialog_btnCancel"] = "取消";
            langItems["FoxAutoPlayIdentityDialog_noDetectedNicknames"] = "暂未识别到可选玩家行";
            langItems["FoxAutoPlayIdentityDialog_noSelectedPlayerRow"] = "请先选择你的玩家行。";
            langItems["SettingsForm_mustBeInteger"] = "必须输入整数";
            langItems["SettingsForm_outOfRange"] = "输入的值超过范围";
            langItems["SettingsForm_resetDefaultTip"] = "已恢复默认设置,请重新打开";
            langItems["TipsForm_title"] = "提示";
            langItems["TipsForm_lblTips"] = "注意: 快捷键Ctrl+X,[前台]方式同步时不支持此功能,选点显示在原棋盘上后,原棋盘将无法落子";
            langItems["TipsForm_lblTips1"] = "可通过勾选双向同步选项恢复落子功能";
            langItems["TipsForm_btnConfirm"] = "确定";
            langItems["TipsForm_btnNotAskAgain"] = "不再提示";
        }

        private static void ReadLangItemsFromFile(string fileName)
        {
            using (StreamReader reader = new StreamReader(fileName, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] parts = line.Split('=');
                    if (parts.Length != 2)
                        continue;
                    langItems[parts[0]] = parts[1];
                }
            }
        }

        private static string GetLangText(string key)
        {
            return ResolveLanguageText(key);
        }

        private static MainForm GetUsableMainForm(MainForm candidate)
        {
            if (candidate == null || candidate.IsDisposed)
                return null;
            return candidate;
        }

        private static SystemColorMode GetSystemColorMode(int colorMode)
        {
            switch (colorMode)
            {
                case AppConfig.ColorModeDark: return SystemColorMode.Dark;
                case AppConfig.ColorModeLight: return SystemColorMode.Classic;
                default: return SystemColorMode.System;
            }
        }
    }
}
