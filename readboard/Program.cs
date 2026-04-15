using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace readboard
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        static string[] arg;
        public static int blackPC = 96;
        public static int whitePC = 96;
        public static int blackZB = 33;
        public static int whiteZB = 33;
        public static Boolean useMag = true;
        public static Boolean verifyMove = true;
        public static Boolean showScaleHint = true;
        public static Boolean showInBoard = false;
        public static Boolean showInBoardHint = true;
        public static Boolean autoMin = true;
       // public const Boolean isAdvScale = false;
        public static Boolean isScaled = false;
        public static String version = "220430";
        //public static Boolean isChn = false;
        public static String timename="200";
        public static int timeinterval=200;
        public static int grayOffset = 50;
        public static Boolean useEnhanceScreen = false;
        public static Boolean playPonder = true;
        public const int UiThemeClassic = 0;
        public const int UiThemeOptimized = 1;
        public static int uiThemeMode = UiThemeOptimized;
     //   public static Boolean scaleForBack = false;

        public static double factor = 1.0;

        public static Boolean hasConfigFile = false;
        public static System.Drawing.Bitmap bitmap;
        public static String language = "cn";
        public static Hashtable  langItems = new Hashtable();

        public static void ReplaceBitmap(System.Drawing.Bitmap newBitmap)
        {
            System.Drawing.Bitmap oldBitmap = Interlocked.Exchange(ref bitmap, newBitmap);
            if (oldBitmap != null)
                oldBitmap.Dispose();
        }

        public static void DisposeBitmap()
        {
            ReplaceBitmap(null);
        }

        //public static Thread th;
        //public  static Boolean run =true;
        // static Form Form1;
        [STAThread]
        static void Main(string[] args)
        {
            arg = args;
            //Start();
            ThreadStart threadStart = new ThreadStart(Start);           
            Thread thread = new Thread(Start);
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();//启动界面
            if (arg.Length < 7)
                System.Environment.Exit(0);
            language = arg[5].ToString();
            String languageFileName = "language_" + language + ".txt";
            addDefaultLangItems();
            if (File.Exists(languageFileName))
            {
                readLangItemsFromFile(languageFileName);
            }
            //if (arg[5].Equals("0"))
            //{
            //    isChn = true;
            //}
            //else {
            //    isChn = false;
            //}
            if (arg[4].Equals("0"))
            {
                AllocConsole();
                hideConsole();
                while (true)
                {
                    String a = Console.ReadLine();                
                    if (a.StartsWith("place"))
                    {
                        char[] separator = { ' ' }; string[] arr = a.Split(separator);
                        try
                        {
                            MainForm.pcurrentWin.placeMove(int.Parse(arr[1]), int.Parse(arr[2]));
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine(e);
                        }
                    }
                    if (a.StartsWith("loss"))
                    {
                        MainForm.pcurrentWin.lossFocus();
                    }
                    if (a.StartsWith("notinboard"))
                    {
                        MainForm.pcurrentWin.stopInBoard();
                    }
                    if (a.StartsWith("version"))
                    {
                        MainForm.pcurrentWin.sendVersion();
                    }
                    if (a.StartsWith("quit"))
                    {
                        Control.CheckForIllegalCrossThreadCalls = false;
                        MainForm.pcurrentWin.shutdown();
                    }
                    }
            }
        }

        private static void addDefaultLangItems()
        {
            langItems.Add("connectLizzieFailed", "棋盘同步工具与Lizzie连接失败");
            langItems.Add("keepSync", "持续同步");
            langItems.Add("recgnizeFaild", "不能识别棋盘,请调整被同步棋盘大小后重新选择或尝试[框选1路线]");
            langItems.Add("noSelectedBoard", "未选择棋盘");
            langItems.Add("noSelectedBoardAndFailed", "未选择棋盘,同步失败");
            langItems.Add("notRightBoard", "未选择正确的棋盘");
            langItems.Add("stopSync", "停止同步");
            langItems.Add("fastSync", "一键同步");
            langItems.Add("helpFile", "readme.rtf");
            langItems.Add("noHelpFile", "找不到说明文档,请检查Lizzie目录下[readboard]文件夹内的[readme.rtf]文件是否存在");
            langItems.Add("komi65Describe", "由于同步时无法获取提子数,日本规则(数目)将变得不准确,需要同步日本规则贴6.5目的棋局时可在Katago中使用[数子+贴目7.0+收后方贴还0.5目]规则模拟");

            langItems.Add("MainForm_rdoFox", "野狐");
            langItems.Add("MainForm_rdoFoxBack", "野狐(后台落子)");
            langItems.Add("MainForm_rdoTygem", "弈城");
            langItems.Add("MainForm_rdoSina", "新浪");
            langItems.Add("MainForm_rdoBack", "其他(后台)");
            langItems.Add("MainForm_rdoFore", "其他(前台)");
            langItems.Add("MainForm_btnSettings", "参数设置");
            langItems.Add("MainForm_btnHelp", "帮助");
            langItems.Add("MainForm_btnTheme", "主题");
            langItems.Add("MainForm_btnFastSync", "一键同步");
            langItems.Add("MainForm_lblBoardSize", "棋盘:");
            langItems.Add("MainForm_btnKomi65", "6.5目规则设置方法");
            langItems.Add("MainForm_chkBothSync", "双向同步");
            langItems.Add("MainForm_chkAutoPlay", "自动落子");
            langItems.Add("MainForm_radioBlack", "执黑");
            langItems.Add("MainForm_radioWhite", "执白");
            langItems.Add("MainForm_lblPlayCondition", "引擎自动落子条件:");
            langItems.Add("MainForm_lblTime", "每手用时");
            langItems.Add("MainForm_lblTotalVisits", "最大计算量(选填)");
            langItems.Add("MainForm_lblBestMoveVisits", "首选计算量(选填)");
            langItems.Add("MainForm_btnClickBoard", "选择棋盘(点击棋盘内部)");
            langItems.Add("MainForm_btnCircleBoard", "框选棋盘");
            langItems.Add("MainForm_btnCircleRow1", "框选1路线");
            langItems.Add("MainForm_btnTogglePonder", "分析/停止");
            langItems.Add("MainForm_chkShowInBoard", "原棋盘上显示选点");
            langItems.Add("MainForm_btnKeepSync", "持续同步(200ms)");
            langItems.Add("MainForm_btnOneTimeSync", "单次同步");
            langItems.Add("MainForm_btnExchange", "交换顺序");
            langItems.Add("MainForm_btnClearBoard", "清空棋盘");
            langItems.Add("MainForm_title", "棋盘同步工具");
            langItems.Add("MainForm_rdoCustomBoard", "自定义");
            langItems.Add("MainForm_groupPlatform", "平台类型");
            langItems.Add("MainForm_groupBoard", "棋盘规格");
            langItems.Add("MainForm_groupSync", "同步与自动落子");
            langItems.Add("MainForm_themeOptimized", "修复版主题");
            langItems.Add("MainForm_themeClassic", "默认主题");

            langItems.Add("MagnifierForm_title", "放大镜");

            langItems.Add("SettingsForm_title", "参数设置");
            langItems.Add("SettingsForm_chkPonder", "后台思考");
            langItems.Add("SettingsForm_chkMag", "使用放大镜");
            langItems.Add("SettingsForm_chkVerifyMove", "验证落子以确保成功");
            langItems.Add("SettingsForm_chkAutoMin", "同步后自动最小化");
            langItems.Add("SettingsForm_lblBackForeOnly", "以下选项只对 其他(前台),其他(后台) 类型的同步生效:");
            langItems.Add("SettingsForm_lblBlackOffsets", "黑色偏差(0-255)");
            langItems.Add("SettingsForm_lblBlackPercents", "黑色占比(0-100)");
            langItems.Add("SettingsForm_lblWhiteffsets", "白色偏差(0-255)");
            langItems.Add("SettingsForm_lblWhitePercents", "白色占比(0-100)");
            langItems.Add("SettingsForm_lblGrayOffsets", "灰度偏差(0-255)");
            langItems.Add("SettingsForm_lblTips", "注意:所有参数都必须为整数");
            langItems.Add("SettingsForm_lblTips1", "如某种颜色棋子识别过多,可尝试降低偏差或增大占比");
            langItems.Add("SettingsForm_lblTips2", "如某种颜色棋子识别丢失,可尝试增大偏差或降低占比");
            langItems.Add("SettingsForm_lblSyncInterval", "同步时间间隔(ms):");
            langItems.Add("SettingsForm_btnReset", "恢复默认设置");
            langItems.Add("SettingsForm_btnConfirm", "确认");
            langItems.Add("SettingsForm_btnCancel", "取消");
            langItems.Add("SettingsForm_chkEnhanceScreen", "强化截图");

            langItems.Add("SettingsForm_chkEnhanceScreen_ToolTip", "勾选可获取桌面外的截图,通常不需要(可能导致刷新降低,无法实时切换棋局等问题)");
            langItems.Add("SettingsForm_chkPonder_ToolTip", "双向同步自动落子时,引擎在对手的回合计算");

            langItems.Add("SettingsForm_mustBeInteger", "必须输入整数");
            langItems.Add("SettingsForm_outOfRange", "输入的值超过范围");
            langItems.Add("SettingsForm_resetDefaultTip", "已恢复默认设置,请重新打开");

            langItems.Add("TipsForm_title", "提示");
            langItems.Add("TipsForm_lblTips", "注意: 快捷键Ctrl+D,[前台]方式同步时不支持此功能,选点显示在原棋盘上后,原棋盘将无法落子");
            langItems.Add("TipsForm_lblTips1", "可通过勾选双向同步选项恢复落子功能");
            langItems.Add("TipsForm_btnConfirm", "确定");
            langItems.Add("TipsForm_btnNotAskAgain", "不再提示");
        }

        private static void readLangItemsFromFile(String fileName)
        {
            StreamReader sr = new StreamReader(fileName, Encoding.UTF8);
            String line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] param = line.Split('=');
                if(param.Length == 2)
                    langItems[param[0]] = param[1];
            }
        }

        // 启动控制台
        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, uint nCmdShow);

        private static void hideConsole(string ConsoleTitle = "")
        {
            ConsoleTitle = String.IsNullOrEmpty(ConsoleTitle) ? Console.Title : ConsoleTitle;
            IntPtr hWnd = FindWindow("ConsoleWindowClass", ConsoleTitle);
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, 0);
            }
        }


        private static void Start()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);           
            try
            {
                if (arg[0].Equals("yzy"))
                {
                        Application.Run(new MainForm(arg[1], arg[2], arg[3], arg[4],  arg[6]));                
                }
            }
            catch (Exception ex)
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.Error.WriteLine("error "+ex.Message);
                System.Environment.Exit(0);
            }
          
        }
    }

}
