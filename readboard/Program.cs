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

        public static int grayOffset
        {
            get { return Config.GrayOffset; }
            set { Config.GrayOffset = value; }
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

        public static bool disableShowInBoardShortcut
        {
            get { return Config.DisableShowInBoardShortcut; }
            set { Config.DisableShowInBoardShortcut = value; }
        }

        public static int uiThemeMode
        {
            get { return Config.UiThemeMode; }
            set { Config.UiThemeMode = value; }
        }

        public static double factor
        {
            get { return runtimeContext.DpiFactor; }
            set { runtimeContext.DpiFactor = value; }
        }

        public static bool hasConfigFile
        {
            get { return runtimeContext.HasConfigFile; }
            set { runtimeContext.HasConfigFile = value; }
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

            runtimeContext.Config = config.Clone();
            runtimeContext.Session.SyncBoth = runtimeContext.Config.SyncBoth;
            configStore.Save(runtimeContext.Config);
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
            runtimeContext.Language = options.Language;
            AddDefaultLangItems();
            LoadLanguageItems(baseDirectory, options.Language);
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
            langItems["connectLizzieFailed"] = "棋盘同步工具与Lizzie连接失败";
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
            langItems["MainForm_lblPlayCondition"] = "引擎自动落子条件:";
            langItems["MainForm_lblTime"] = "每手用时";
            langItems["MainForm_lblTotalVisits"] = "最大计算量(选填)";
            langItems["MainForm_lblBestMoveVisits"] = "首选计算量(选填)";
            langItems["MainForm_btnClickBoard"] = "选择棋盘(点击棋盘内部)";
            langItems["MainForm_btnCircleBoard"] = "框选棋盘";
            langItems["MainForm_btnCircleRow1"] = "框选1路线";
            langItems["MainForm_btnTogglePonder"] = "分析/停止";
            langItems["MainForm_chkShowInBoard"] = "原棋盘上显示选点";
            langItems["MainForm_btnKeepSync"] = "持续同步(200ms)";
            langItems["MainForm_btnOneTimeSync"] = "单次同步";
            langItems["MainForm_btnExchange"] = "交换顺序";
            langItems["MainForm_btnForceRebuild"] = "强制重建";
            langItems["MainForm_btnClearBoard"] = "清空棋盘";
            langItems["MainForm_title"] = "棋盘同步工具";
            langItems["MainForm_titleTagFox"] = "野狐";
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
            langItems["Update_waitingForHostInstall"] = "等待宿主安装...";
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
            langItems["SettingsForm_chkDisableShowInBoardShortcut"] = "关闭显示选点快捷键";
            langItems["SettingsForm_chkDebugDiagnostics"] = "保存调试诊断";
            langItems["SettingsForm_btnOpenDebugDiagnostics"] = "打开调试目录";
            langItems["SettingsForm_chkEnhanceScreen_ToolTip"] = "勾选可获取桌面外的截图,通常不需要(可能导致刷新降低,无法实时切换棋局等问题)";
            langItems["SettingsForm_chkPonder_ToolTip"] = "双向同步自动落子时,引擎在对手的回合计算";
            langItems["SettingsForm_lblColorMode"] = "颜色模式:";
            langItems["SettingsForm_rdoColorSystem"] = "跟随系统";
            langItems["SettingsForm_rdoColorDark"] = "深色";
            langItems["SettingsForm_rdoColorLight"] = "浅色";
            langItems["SettingsForm_colorModeRestartTip"] = "颜色模式已更改，重启后生效。";
            langItems["SettingsForm_mustBeInteger"] = "必须输入整数";
            langItems["SettingsForm_outOfRange"] = "输入的值超过范围";
            langItems["SettingsForm_resetDefaultTip"] = "已恢复默认设置,请重新打开";
            langItems["TipsForm_title"] = "提示";
            langItems["TipsForm_lblTips"] = "注意: 快捷键Ctrl+D,[前台]方式同步时不支持此功能,选点显示在原棋盘上后,原棋盘将无法落子";
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
                    if (parts.Length == 2)
                        langItems[parts[0]] = parts[1];
                }
            }
        }

        private static string GetLangText(string key)
        {
            object value = langItems[key];
            return value == null ? key : value.ToString();
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
