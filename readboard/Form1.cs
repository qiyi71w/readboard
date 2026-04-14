using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MouseKeyboardActivityMonitor;
using MouseKeyboardActivityMonitor.WinApi;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using System.Threading.Tasks;
using System.Drawing.Imaging;
using System.ComponentModel;
using LwInterop = Interop.lw;

namespace readboard
{
    public partial class MainForm : Form
    {
        // Boolean showDebugImage = true;
        TcpClient client;
        NetworkStream io;
        Thread threadReceive;
        string readStr;
        Boolean pressed = false;
        Boolean clicked = false;

        public static int ox1;
        int ox2;
        public static int oy1;
        int oy2;
        //  public static CDmSoft dm;
        //public static CDmSoft dm2;
        IntPtr hwnd = IntPtr.Zero;
        //int hwndFoxPlace = 0;
        Form2 form2;

        Boolean startedSync = false;
        Boolean isContinuousSyncing = false;
        //object qx1;
        // object qy1;

        Boolean keepSync = false;
        int sx1;
        int sy1;
        int width;
        int height;
        //int rectSize;
        int rectX1;
        int rectY1;
        //   int sx1ty5=0;
        //   int sy1ty5=0;
        //int all;
        //  System.Timers.Timer t;
        private const int TYPE_FOX = 0;
        private const int TYPE_TYGEM = 1;
        private const int TYPE_SINA = 2;
        private const int TYPE_BACKGROUND = 3;
        private const int TYPE_FOX_BACKGROUND_PLACE = 4;
        private const int TYPE_FOREGROUND = 5;
        public static int type = TYPE_FOX;
        // Boolean isQTYC = false;
        // int boardWidth=19;
        int boardH = 19;
        int boardW = 19;
        static double widthMagrin = 0;
        static double heightMagrin = 0;
        //Boolean noticeLast = true;
        Boolean syncBoth = false;
        Boolean canUseLW = false;
        //Boolean noLw = false;
        Boolean useTcp = false;
        Thread threadFastSync;
        Thread threadKeepSync;
        Boolean isMannulCircle = false;
        float factor = 1.0f;
        private KeyboardHookListener hookListener;
        private int port = 0;


        Boolean savedPlace = false;
        int savedX;
        int savedY;

        int posX = -1;
        int posY = -1;

        private Boolean isJavaFrame = false;
        private int javaX, javaY;
        LwInterop.lwsoft lw;
        Boolean canUsePrintWindow = true;
        Boolean isFirstGetPos = true;
        Boolean cansetFirstGetPos = true;
        Boolean isSecondTime = false;

        Boolean needForceUnbind = false;
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);
        [StructLayout(LayoutKind.Sequential)]


        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static Boolean IsFoxSyncType(int syncType)
        {
            return syncType == TYPE_FOX || syncType == TYPE_FOX_BACKGROUND_PLACE;
        }

        private static Boolean UsesManualSelectionType(int syncType)
        {
            return syncType == TYPE_BACKGROUND || syncType == TYPE_FOREGROUND;
        }

        private static Boolean SupportsFastSyncType(int syncType)
        {
            return IsFoxSyncType(syncType) || syncType == TYPE_TYGEM || syncType == TYPE_SINA;
        }

        private void setNativeBoardMode(int syncType)
        {
            type = syncType;
            this.btnCircleBoard.Enabled = false;
            this.btnCircleRow1.Enabled = false;
            this.btnClickBoard.Enabled = true;
            this.btnFastSync.Enabled = true;
            if (this.rdoOtherBoard.Checked)
                this.rdo19x19.Checked = true;
            this.rdoOtherBoard.Enabled = false;
        }

        private void setManualSelectionMode(int syncType)
        {
            type = syncType;
            this.btnClickBoard.Enabled = false;
            this.btnCircleRow1.Enabled = true;
            this.btnCircleBoard.Enabled = true;
            this.btnFastSync.Enabled = false;
            this.rdoOtherBoard.Enabled = true;
        }


        [DllImport("gdi32.dll", EntryPoint = "DeleteDC")]
        public static extern IntPtr DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        public static extern IntPtr DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll", EntryPoint = "BitBlt")]
        public static extern bool BitBlt(IntPtr hdcDest, int nXDest,
        int nYDest, int nWidth, int nHeight, IntPtr hdcSrc,
        int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc,
        int nWidth, int nHeight);

        [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", EntryPoint = "SelectObject")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobjBmp);

        [DllImport("user32.dll", EntryPoint = "GetDC")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "ReleaseDC")]
        public static extern IntPtr ReleaseDC(IntPtr hWnd, IntPtr hDC);
        private Bitmap GetWindowImage(IntPtr hwnd)
        {
            return GetWindowImage(hwnd, false, -1, -1, -1, -1, true);
        }
        private Bitmap GetWindowImage(IntPtr hwnd, Boolean usePrintWindow)
        {
            return GetWindowImage(hwnd, false, -1, -1, -1, -1, usePrintWindow);
        }

        private Bitmap GetWindowImage(IntPtr hwnd, Boolean calcRect, int x, int y, int w, int h, Boolean usePrintWindow)
        {
            if (isJavaFrame)
            {
                return GetWindowPrintImage(hwnd);
            }
            else
            {
                try
                {
                    cansetFirstGetPos = true;
                    RECT rect = new RECT();
                    GetWindowRect(hwnd, ref rect);
                    int screenWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
                    int screenHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
                    if (usePrintWindow)
                    {
                        if (calcRect)
                        {
                            int startx = rect.Left + x;
                            int endx = rect.Left + x + w;
                            int starty = rect.Top + y;
                            int endy = rect.Top + y + h;
                            if (Program.useEnhanceScreen && canUsePrintWindow && (startx < 0 || startx > screenWidth || starty < 0 || endy > screenHeight))
                            {
                                return GetWindowPrintImage(hwnd);
                            }
                        }
                        else
                        if (Program.useEnhanceScreen && canUsePrintWindow && (rect.Left < 0 || rect.Left > screenWidth || rect.Top < 0 || rect.Bottom > screenHeight))
                        {
                            return GetWindowPrintImage(hwnd);
                        }
                    }
                    int rectWidth = rect.Right - rect.Left;
                    int rectHeight = rect.Bottom - rect.Top;
                    IntPtr windc = GetDC(hwnd);
                    IntPtr hDCMem = CreateCompatibleDC(windc);
                    IntPtr hbitmap = CreateCompatibleBitmap(windc, rectWidth, rectHeight);
                    IntPtr hOldBitmap = (IntPtr)SelectObject(hDCMem, hbitmap);
                    BitBlt(hDCMem, 0, 0, rectWidth, rectHeight, windc, 0, 0, 13369376);
                    hbitmap = (IntPtr)SelectObject(hDCMem, hOldBitmap);
                    Bitmap bitmap = Bitmap.FromHbitmap(hbitmap);
                    DeleteObject(hbitmap);
                    DeleteObject(hOldBitmap);
                    DeleteDC(hDCMem);
                    ReleaseDC(hwnd, windc);
                    return bitmap;
                }
                catch (Exception e)
                {
                    SendError(e.ToString());
                    return null;
                }
            }
        }

        private Bitmap GetWindowPrintImage(IntPtr hWnd)
        {
            try
            {
                cansetFirstGetPos = false;
                RECT rect = new RECT();
                GetWindowRect(hWnd, ref rect);
                int rectWidth = rect.Right - rect.Left;
                int rectHeight = rect.Bottom - rect.Top;
                if (rectWidth <= 0 || rectHeight <= 0) return null;
                Bitmap bmp = new Bitmap(rectWidth, rectHeight);
                Graphics g = Graphics.FromImage(bmp);
                IntPtr dc = g.GetHdc();
                PrintWindow(hWnd, dc, 0);
                g.ReleaseHdc();
                g.Dispose();
                return bmp;
            }
            catch { return null; }
        }

        private Bitmap GetWindowBmp(IntPtr hWnd, int x, int y, int width, int height)
        {
            if (x < 0 || y < 0 || width <= 0 || height <= 0)
                return null;
            try
            {
                Bitmap bmp = GetWindowImage(hWnd, true, x, y, width, height, true);
                if (bmp == null)
                    return null;
                Bitmap mybit = new Bitmap(width, height);
                Graphics g1 = Graphics.FromImage(mybit);
                g1.DrawImage(bmp /* 原图 */,
                         new Rectangle(new System.Drawing.Point(0, 0), new System.Drawing.Size(width, height)), // 目标位置 
                         new Rectangle(new System.Drawing.Point(x, y), new System.Drawing.Size(width, height)), // 原图位置 
                        GraphicsUnit.Pixel);
                g1.Dispose();
                return mybit;
            }
            catch { return null; }
        }

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(
         IntPtr hwnd,               // Window to copy,Handle to the window that will be copied.
         IntPtr hdcBlt,             // HDC to print into,Handle to the device context.
         UInt32 nFlags              // Optional flags,Specifies the drawing options. It can be one of the following values.
         );

        private String GetClassNameStr(IntPtr hwnd)
        {
            StringBuilder className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            return className.ToString();
        }

        private String GetTitleStr(IntPtr hwnd)
        {
            StringBuilder title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            return title.ToString();
        }

        public void SendError(String strMsg)
        {
            if (useTcp)
            {
                try
                {
                    byte[] by = Encoding.UTF8.GetBytes("error: " + strMsg + "\r\n");
                    io.Write(by, 0, by.Length);
                    io.Flush();
                }
                catch (Exception e)
                {
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                    Console.Error.WriteLine(e.Message);
                }
            }
            else
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.Error.WriteLine("error: " + strMsg);
            }
        }

        private void Send(String strMsg)
        {
            //Console.OutputEncoding = Encoding.UTF8;
            if (useTcp)
            {
                try
                {
                    byte[] by = Encoding.UTF8.GetBytes(strMsg + "\r\n");
                    io.Write(by, 0, by.Length);
                    io.Flush();
                }
                catch (Exception e)
                {
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                    Console.Error.WriteLine(e.Message);
                }
            }
            else
            {
                Console.WriteLine(strMsg);
            }
        }

        private Boolean isCtrlDown = false;

        private void HookListener_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            // Console.Out.WriteLine(e.KeyValue);
            if (e.KeyValue == 162 || e.KeyValue == 163)
                isCtrlDown = true;
            if (isCtrlDown && e.KeyValue == 88)
                chkShowInBoard.Checked = !chkShowInBoard.Checked;
        }

        private void HookListener_KeyUp(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if ( e.KeyValue == 162 || e.KeyValue==163)
                isCtrlDown = false;
        }

        private void Receive()
        {
            try
            {
                while (true)
                {
                    try
                    {
                        byte[] data = new byte[20000];
                        int numBytesRead = io.Read(data, 0, data.Length);
                        if (numBytesRead > 0)
                        {
                            readStr = Encoding.UTF8.GetString(data, 0, numBytesRead);
                            //Console.Error.WriteLine(readStr);
                        }
                        readPlace(readStr);
                    }
                    catch (Exception ex)
                    {
                        SendError(ex.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                SendError(e.ToString());
            }
        }
        private void readPlace(String a)
        {
            if (a.StartsWith("place"))
            {
                char[] separator = { ' ' }; string[] arr = a.Split(separator);
                try
                {
                    placeMove(int.Parse(arr[1]), int.Parse(arr[2]));
                }
                catch (Exception e)
                {
                    SendError(e.ToString());
                }
            }
            if (a.StartsWith("loss"))
            {
                lossFocus();
            }
            if (a.StartsWith("notinboard"))
            {
                stopInBoard();
            }
            if (a.StartsWith("version"))
            {
                sendVersion();
            }
            if (a.StartsWith("quit"))
            {
                Control.CheckForIllegalCrossThreadCalls = false;
                MainForm.pcurrentWin.shutdown();
            }
        }

        public MainForm(String aitime, String playouts, String firstpo, String usetcp, String serverPort)
        {
            InitializeComponent();
            if (usetcp.Equals("1"))
            {
                useTcp = true;
            }
            if (useTcp)
            {
                try
                {
                    port = Convert.ToInt32(serverPort);
                    client = new TcpClient("127.0.0.1", port);
                    io = client.GetStream();
                    threadReceive = new Thread(new ThreadStart(Receive));
                    threadReceive.IsBackground = true;
                    threadReceive.Start();
                }
                catch
                {
                    MessageBox.Show(getLangStr("connectLizzieFailed"));// "棋盘同步工具与Lizzie连接失败" : "Can not connect to Lizzie");
                }
            }
            GlobalHooker hooker = new GlobalHooker();
            hookListener = new KeyboardHookListener(hooker);
            hookListener.KeyDown += HookListener_KeyDown;
            hookListener.KeyUp += HookListener_KeyUp;
            hookListener.Start();
            System.Drawing.Bitmap bitmap = new Bitmap(1, 1);
            System.Drawing.Graphics graphics2 = Graphics.FromImage(bitmap);
            factor = graphics2.DpiX / 96;
            if (factor > 1.0f)
            {
                Program.isScaled = true;
                Program.factor = factor;
            }
            if (File.Exists("config_readboard.txt"))
            {
                StreamReader sr = new StreamReader("config_readboard.txt", Encoding.UTF8);
                String line;
                if ((line = sr.ReadLine()) != null)
                {
                    string[] arr = line.Split('_');
                    if (arr.Length == 12)
                    {
                        try
                        {
                            if (Environment.GetEnvironmentVariable("computername").Replace("_", "").Equals(arr[10]))
                            {
                                Program.blackPC = Convert.ToInt32(arr[0]);
                                Program.blackZB = Convert.ToInt32(arr[1]);
                                Program.whitePC = Convert.ToInt32(arr[2]);
                                Program.whiteZB = Convert.ToInt32(arr[3]);
                                Program.useMag = (Convert.ToInt32(arr[4]) == 1);
                                Program.verifyMove = (Convert.ToInt32(arr[5]) == 1);
                                Program.showScaleHint = (Convert.ToInt32(arr[6]) == 1);
                                Program.showInBoard = (Convert.ToInt32(arr[7]) == 1);
                                Program.showInBoardHint = (Convert.ToInt32(arr[8]) == 1);
                                Program.autoMin = (Convert.ToInt32(arr[9]) == 1);
                                type = Convert.ToInt32(arr[11]);
                                Program.hasConfigFile = true;
                            }
                            else
                            {
                                if (File.Exists("config_readboard_others.txt"))
                                    File.Delete("config_readboard_others.txt");
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
                sr.Close();
            }

            if (File.Exists("config_readboard_others.txt"))
            {
                int customW = -1;
                int customH = -1;
                StreamReader sr = new StreamReader("config_readboard_others.txt", Encoding.UTF8);
                String line;
                if ((line = sr.ReadLine()) != null)
                {
                    string[] arr = line.Split('_');
                    if (arr.Length == 12)
                    {
                        try
                        {
                            int savedVersion= Convert.ToInt32(arr[0]);
                            if (savedVersion == Convert.ToInt32(Program.version)) {
                                this.boardW = Convert.ToInt32(arr[1]);
                                this.boardH = Convert.ToInt32(arr[2]);
                                customW = Convert.ToInt32(arr[3]);
                                customH = Convert.ToInt32(arr[4]);
                                Program.timeinterval = Convert.ToInt32(arr[5]);
                                Program.timename = Program.timeinterval.ToString();
                                this.syncBoth = (Convert.ToInt32(arr[6]) == 1);
                                Program.grayOffset = Convert.ToInt32(arr[7]);
                                posX = Convert.ToInt32(arr[8]);
                                posY = Convert.ToInt32(arr[9]);
                                Program.useEnhanceScreen = (Convert.ToInt32(arr[10]) == 1);
                                Program.playPonder = (Convert.ToInt32(arr[11]) == 1);
                                if (posX != -1 && posY != -1)
                                {
                                    Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
                                    int maxX = Math.Max(screenBounds.Left, screenBounds.Right - this.Width);
                                    int maxY = Math.Max(screenBounds.Top, screenBounds.Bottom - this.Height);
                                    this.Location = new System.Drawing.Point(
                                        Math.Min(Math.Max(screenBounds.Left, posX), maxX),
                                        Math.Min(Math.Max(screenBounds.Top, posY), maxY));
                                }
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
                sr.Close();
                if (boardW == boardH)
                {
                    if (boardW == 19)
                    {
                        this.rdo19x19.Checked = true;
                        if (customW > 0)
                            this.txtBoardWidth.Text = customW.ToString();
                        if (customH > 0)
                            this.txtBoardHeight.Text = customH.ToString();
                    }
                    else if (boardW == 13)
                    {
                        this.rdo13x13.Checked = true;
                        if (customW > 0)
                            this.txtBoardWidth.Text = customW.ToString();
                        if (customH > 0)
                            this.txtBoardHeight.Text = customH.ToString();
                    }
                    else if (boardW == 9)
                    {
                        this.rdo9x9.Checked = true;
                        if (customW > 0)
                            this.txtBoardWidth.Text = customW.ToString();
                        if (customH > 0)
                            this.txtBoardHeight.Text = customH.ToString();
                    }
                    else
                    {
                        this.txtBoardWidth.Text = this.boardW.ToString();
                        this.txtBoardHeight.Text = this.boardH.ToString();
                        this.rdoOtherBoard.Checked = true;
                    }
                }
                else
                {
                    this.txtBoardWidth.Text = this.boardW.ToString();
                    this.txtBoardHeight.Text = this.boardH.ToString();
                    this.rdoOtherBoard.Checked = true;
                }
            }
            else
                this.rdo19x19.Checked = true;
            switch (type)
            {
                case TYPE_FOX:
                    rdoFox.Checked = true;
                    break;
                case TYPE_TYGEM:
                    rdoTygem.Checked = true;
                    break;
                case TYPE_SINA:
                    rdoSina.Checked = true;
                    break;
                case TYPE_FOX_BACKGROUND_PLACE:
                    rdoFoxBack.Checked = true;
                    break;
                case TYPE_FOREGROUND:
                    rdoFore.Checked = true;
                    break;
                case TYPE_BACKGROUND:
                    rdoBack.Checked = true;
                    break;
            }
            this.chkShowInBoard.Checked = Program.showInBoard;
            Program.showInBoard = chkShowInBoard.Checked;
            //if (Program.showScaleHint && factor > 1)
            //{

            //    Form6 form6 = new Form6();
            //    form6.ShowDialog();

            //}
            //dm = new CDmSoft();
            //dm.SetShowErrorMsg(0);
            this.MaximizeBox = false;
            pcurrentWin = this;

            if (SupportsFastSyncType(type))
                this.btnFastSync.Enabled = true;
            else
                this.btnFastSync.Enabled = false;
            if (!aitime.Equals(" "))
                textBox1.Text = aitime;
            if (!playouts.Equals(" "))
                textBox2.Text = playouts;
            if (!firstpo.Equals(" "))
                textBox3.Text = firstpo;
            try
            {
                //  int s = DllRegisterServer();
                //  if (s >= 0)
                //  {
                //注册成功!             
                try
                {
                    lw = new LwInterop.lwsoft();
                    canUseLW = false;// true;
                }
                catch (Exception)
                {
                    canUseLW = false;
                }
                //  }
                //  else
                //  {
                //注册失败}
                //      canUseLW = false;
                //  }
            }
            catch (Exception)
            {
                canUseLW = false;
            }
            radioWhite.Enabled = false;
            radioBlack.Enabled = false;
            textBox1.Enabled = false;
            textBox2.Enabled = false;
            textBox3.Enabled = false;
            if (syncBoth)
            {
                chkBothSync.Checked = true;
                chkAutoPlay.Enabled = true;
            }
            else
            {
                chkBothSync.Checked = false;
                chkAutoPlay.Enabled = false;
            }
            this.rdoFox.Text = getLangStr("MainForm_rdoFox");
            this.rdoFoxBack.Text = getLangStr("MainForm_rdoFoxBack");
            this.rdoTygem.Text = getLangStr("MainForm_rdoTygem");
            this.rdoSina.Text = getLangStr("MainForm_rdoSina");
            this.rdoBack.Text = getLangStr("MainForm_rdoBack");
            this.rdoFore.Text = getLangStr("MainForm_rdoFore");
            this.btnSettings.Text = getLangStr("MainForm_btnSettings");
            this.btnHelp.Text = getLangStr("MainForm_btnHelp");
            this.btnFastSync.Text = getLangStr("MainForm_btnFastSync");
            this.lblBoardSize.Text = getLangStr("MainForm_lblBoardSize");
            this.btnKomi65.Text = getLangStr("MainForm_btnKomi65");
            this.chkBothSync.Text = getLangStr("MainForm_chkBothSync");
            this.chkAutoPlay.Text = getLangStr("MainForm_chkAutoPlay");
            this.radioBlack.Text = getLangStr("MainForm_radioBlack");
            this.radioWhite.Text = getLangStr("MainForm_radioWhite");
            this.lblPlayCondition.Text = getLangStr("MainForm_lblPlayCondition");
            this.lblTime.Text = getLangStr("MainForm_lblTime");
            this.lblTotalVisits.Text = getLangStr("MainForm_lblTotalVisits");
            this.lblBestMoveVisits.Text=getLangStr("MainForm_lblBestMoveVisits");
            this.btnClickBoard.Text = getLangStr("MainForm_btnClickBoard");
            this.btnCircleBoard.Text = getLangStr("MainForm_btnCircleBoard");
            this.btnCircleRow1.Text = getLangStr("MainForm_btnCircleRow1");
            this.btnTogglePonder.Text = getLangStr("MainForm_btnTogglePonder");
            this.chkShowInBoard.Text = getLangStr("MainForm_chkShowInBoard");
            this.btnKeepSync.Text = getLangStr("MainForm_btnKeepSync");
            this.btnOneTimeSync.Text = getLangStr("MainForm_btnOneTimeSync");
            this.btnExchange.Text = getLangStr("MainForm_btnExchange");
            this.btnClearBoard.Text = getLangStr("MainForm_btnClearBoard");
            this.Text = getLangStr("MainForm_title");
            var toolTip1 = new ToolTip();
            toolTip1.SetToolTip(this.chkShowInBoard, "Ctrl+X");
            Send("ready");
            sendPonderStatus();
        }

        private String getLangStr(String itemName)
        {
            String result  = "";
            try {
                result = Program.langItems[itemName].ToString();
            }
            catch (Exception e)
            {
                SendError(e.ToString());              
            }
            return result;
        }

        public void sendPonderStatus()
        {
            if (Program.playPonder)
                Send("playponder on");
            else
                Send("playponder off");
        }

        MouseHookListener mh;
        /// <summary>
        /// 注册
        /// </summary>
        /// <param name="strCmd"></param>
        /// <returns></returns>
        static void AutoRegCom(string strCmd)
        {
            // string rInfo;
            try
            {
                Process proc = new Process();
                proc.StartInfo.CreateNoWindow = true;
                proc.StartInfo.FileName = "cmd.exe";
                proc.StartInfo.Arguments = "C:\\Windows\\System32\\cmd.exe";
                proc.StartInfo.UseShellExecute = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.StartInfo.RedirectStandardInput = true;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.Verb = "RunAs";
                proc.StartInfo.UseShellExecute = false;
                proc.Start();
                proc.StandardInput.WriteLine(strCmd);
                proc.Close();
            }
            catch (Exception)
            {
                return;
            }
        }

        public static MainForm
           pcurrentWin = null;

        private void Form1_Load(object sender, EventArgs e)
        {
            string startup = Application.ExecutablePath;
            int pp = startup.LastIndexOf("\\");
            startup = startup.Substring(0, pp);

            mh = new MouseHookListener(new GlobalHooker());

            mh.MouseMove += mh_MouseMoveEvent;
            mh.MouseClick += mh_MouseMoveEvent2;
            mh.Enabled = false;
            this.btnKeepSync.Text = getLangStr("keepSync") + "(" + Program.timename + "ms)";
        }

        //[DllImport("user32.dll")]
        //static extern void BlockInput(bool Block);
        public void Snap(int x1, int y1, int x2, int y2)
        {
            pressed = true;
            ox1 = Math.Min(x1, x2);
            oy1 = Math.Min(y1, y2);
            ox2 = Math.Max(x1, x2);
            oy2 = Math.Max(y1, y2);
            if (!isMannulCircle)
            {
                if (!reCalculateRow1(ox1, oy1, ox2, oy2))
                    MessageBox.Show(getLangStr("recgnizeFaild"));// Program.isChn ? "不能识别棋盘,请调整被同步棋盘大小后重新选择或尝试[框选1路线]" : "Can not detect board,Please zoom the board and try again or use [CircleRow1]");
                else if (type == 3)
                {
                    int curX = Control.MousePosition.X;
                    int curY = Control.MousePosition.Y;
                    // BlockInput(true);
                    SetCursorPos((ox1 + ox2) / 2, (oy1 + oy2) / 2);
                    Task.Factory.StartNew(() =>
                    {
                        Thread.Sleep(35);
                        hwnd = getMousePointHwnd();
                        Console.WriteLine(hwnd);
                    });
                    Task.Factory.StartNew(() =>
                    {
                        Thread.Sleep(30);
                        SetCursorPos((ox1 + ox2) / 2, (oy1 + oy2) / 2);
                        Thread.Sleep(20);
                        SetCursorPos((ox1 + ox2) / 2, (oy1 + oy2) / 2);
                        Thread.Sleep(50);
                        SetCursorPos((ox1 + ox2) / 2, (oy1 + oy2) / 2);
                        SetCursorPos(curX, curY);
                    });
                    //  BlockInput(false);                   
                }
            }
            else
            {
                int gapX = (int)Math.Round((ox2 - ox1) / ((boardW - 1) * 2f));
                int gapY = (int)Math.Round((oy2 - oy1) / ((boardH - 1) * 2f));
                ox1 = ox1 - gapX;
                oy1 = oy1 - gapY;
                ox2 = ox2 + gapX;
                oy2 = oy2 + gapY;
            }
            this.WindowState = FormWindowState.Normal;
            //mh.Enabled = false;
        }

        void mh_MouseMoveEvent(object sender, MouseEventArgs e)
        {
            if (type == 3 && !isMannulCircle)
                return;
            if (pressed)
            {
                pressed = false;
                hwnd = getMousePointHwnd();
            }
        }

        void mh_MouseMoveEvent2(object sender, MouseEventArgs e)
        {
            if (type == 3 && !isMannulCircle)
                return;
            if (clicked)
            {
                //if (!isKuangxuan)
                //     mh.Enabled = false;
                clicked = false;
                hwnd = getMousePointHwnd();
            }


        }

        private void Form1_MouseClick(object sender, MouseEventArgs e)
        {

        }

        private void button3_Click(object sender, EventArgs e)
        {
            mh.Enabled = true;
            clicked = true;
        }

        private Boolean CaptureScreen(int x, int y, int x2, int y2)
        {
            if (x2 > x && y2 > y)
            {
                int width = x2 - x;
                int height = y2 - y;
                Program.bitmap = new Bitmap(width, height);
                using (System.Drawing.Graphics graphics = Graphics.FromImage(Program.bitmap))
                {
                    //  graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size((int)(width * factor), (int)(height * factor)));
                    graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
                    //bitmap.Save("screen.bmp");
                }
                //  dm2.Capture(x, y, x2, y2, "screen.bmp");
                return true;
            }
            else
            {
                MessageBox.Show(getLangStr("noSelectedBoard"));// (Program.isChn ? "未选择棋盘" : "No board has been choosen");
                return false;
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            canUsePrintWindow = true;
            isFirstGetPos = true;
            isSecondTime = false;
            oneTimeSync();
        }

        [DllImport("Kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        private const int S_OK = 0;
        private enum PROCESS_DPI_AWARENESS
        {
            PROCESS_DPI_UNAWARE = 0,
            PROCESS_SYSTEM_DPI_AWARE = 1,
            PROCESS_PER_MONITOR_DPI_AWARE = 2
        }
        [DllImport("Shcore.dll")]
        private static extern int GetProcessDpiAwareness(IntPtr hprocess, out PROCESS_DPI_AWARENESS value);

        [DllImport("user32", EntryPoint = "GetWindowThreadProcessId")]
        private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);

        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(Point Point);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out Point lpPoint);

        private IntPtr getMousePointHwnd()
        {
            Point p;
            GetCursorPos(out p);
            return WindowFromPoint(p);
        }

        private Boolean GetSupportDpiState(IntPtr hwnd)
        {
            try
            {
                int pid = -1;
                GetWindowThreadProcessId(hwnd, out pid);
                System.Diagnostics.Process proc = Process.GetProcessById(pid);
                PROCESS_DPI_AWARENESS value;
                int res = GetProcessDpiAwareness(proc.Handle, out value);
                if (res == 0)
                {
                    if (value == PROCESS_DPI_AWARENESS.PROCESS_DPI_UNAWARE)
                        return false;
                    else
                        return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void oneTimeSync()
        {
            if (type == 5)
            {
                isJavaFrame = false;
                sx1 = ox1;
                sy1 = oy1;
                width = ox2 - ox1;
                height = oy2 - oy1;
                OutPut3(true);
                if (width <= this.boardW)
                {
                    MessageBox.Show(getLangStr("noSelectedBoardAndFailed"));// (Program.isChn ? "未选择棋盘,同步失败" : "No board has been choosen,Sync failed");
                    return;
                }
            }
            else
            {
                if ((int)hwnd <= 0)
                {
                    MessageBox.Show(getLangStr("noSelectedBoardAndFailed"));// (Program.isChn ? "未选择棋盘,同步失败" : "No board has been choosen,Sync failed");
                    return;
                }
                int x1;
                int y1;
                int x2;
                int y2;

                RECT rect = new RECT();
                GetWindowRect(hwnd, ref rect);
                x2 = rect.Right;
                y2 = rect.Bottom;
                x1 = rect.Left;
                y1 = rect.Top;

                if ((int)x1 == 0 && (int)x2 == 0 && (int)y1 == 0 && (int)y2 == 0)
                {
                    MessageBox.Show(getLangStr("noSelectedBoardAndFailed"));// (Program.isChn ? "未选择棋盘,同步失败" : "No board has been choosen,Sync failed");
                    return;
                }
                String classNameStr = GetClassNameStr(hwnd);
                if (classNameStr.Equals("SunAwtFrame"))
                {
                    isJavaFrame = true;
                }
                else
                {
                    isJavaFrame = false;
                }
                RgbInfo[] rgbArray = new RgbInfo[0];
                int totalWidth = -1;
                if (IsFoxSyncType(type))
                {
                    if (!classNameStr.ToLower().Equals("#32770"))
                    {
                        MessageBox.Show(getLangStr("notRightBoard"));// (Program.isChn ? "未选择正确的棋盘" : "Not right board");
                    }
                    if (!getFoxPos(hwnd, out rgbArray, out totalWidth))
                        return;
                }
                if (type == 1)
                {
                    if (!classNameStr.ToLower().Equals("afxwnd140u"))
                    {
                        MessageBox.Show(getLangStr("notRightBoard"));// (Program.isChn ? "未选择正确的棋盘" : "Not right board");
                    }
                    sx1 = 0;
                    sy1 = 0;
                    width = x2 - x1;
                    height = y2 - y1;
                    if (!GetSupportDpiState(hwnd))
                    {
                        width = (int)(width / factor);
                        height = (int)(height / factor);
                    }
                }
                if (type == 2)
                {
                    if (!(classNameStr.ToLower().Equals("tlmdsimplepanel") || classNameStr.Equals("tpanel")))
                    {
                        MessageBox.Show(getLangStr("notRightBoard"));// (Program.isChn ? "未选择正确的棋盘" : "Not right board");
                    }
                    if (!getSinaPos(hwnd, out rgbArray, out totalWidth))
                        return;
                }
                if (type == 3)
                {

                    sx1 = (int)(ox1 - x1);
                    sy1 = (int)(oy1 - y1);
                    width = (int)((ox2 - ox1));
                    height = (int)((oy2 - oy1));
                    if (!GetSupportDpiState(hwnd))
                    {
                        sx1 = (int)(sx1 / factor);
                        sy1 = (int)(sy1 / factor);
                        width = (int)(width / factor);
                        height = (int)(height / factor);
                    }
                    OutPut3(true);
                }
                else
                    OutPut(true, rgbArray, null, totalWidth);
            }
        }

        private void minWindow(String a)
        {
            this.WindowState = FormWindowState.Minimized;
        }
        private void startKeepingSync(String a)
        {
            this.btnKeepSync.Text = getLangStr("stopSync");//  Program.isChn ? "停止同步" : "StopSync";
            this.btnFastSync.Text = getLangStr("stopSync");//Program.isChn ? "停止同步" : "StopSync";
            startedSync = true;
            this.rdoFox.Enabled = false;
            this.rdoFoxBack.Enabled = false;
            this.rdoTygem.Enabled = false;
            //checkBox1.Enabled = false;
            // chkAutoPlay.Enabled = checkBox1.Checked;
            this.rdoBack.Enabled = false;
            this.rdoSina.Enabled = false;
            this.rdo19x19.Enabled = false;
            this.rdo13x13.Enabled = false;
            this.rdo9x9.Enabled = false;
            this.rdoOtherBoard.Enabled = false;
            this.rdoFore.Enabled = false;
            this.btnCircleRow1.Enabled = false;
            this.btnCircleBoard.Enabled = false;
            this.btnClickBoard.Enabled = false;
            this.btnOneTimeSync.Enabled = false;
        }

        private void setContinuousSync(String a)
        {
            //this.button5.Text = "停止同步";
            this.btnFastSync.Text = getLangStr("stopSync");//Program.isChn ? "停止同步" : "StopSync";
            //  button5click = true;
            this.rdoFox.Enabled = false;
            this.rdoFoxBack.Enabled = false;
            this.rdoTygem.Enabled = false;
            //checkBox1.Enabled = false;
            // chkAutoPlay.Enabled = checkBox1.Checked;
            this.rdoBack.Enabled = false;
            this.rdoSina.Enabled = false;
            this.rdo19x19.Enabled = false;
            this.rdo13x13.Enabled = false;
            this.rdo9x9.Enabled = false;
            this.rdoOtherBoard.Enabled = false;
            this.rdoFore.Enabled = false;
            //  this.button2.Enabled = false;
            //    this.button3.Enabled = false;
            //    this.button4.Enabled = false;
        }

        public void Action2Test(String t)
        {
            this.btnKeepSync.Text = t;
            if (!isContinuousSyncing)
                this.btnFastSync.Text = getLangStr("fastSync");//Program.isChn ? "一键同步" : "FastSync";
            //if (this.factor <= 1)
            //{ 
            this.btnOneTimeSync.Enabled = true;
            this.btnKeepSync.Enabled = true;
            this.rdoFox.Enabled = true;
            this.rdoFoxBack.Enabled = true;
            this.rdoSina.Enabled = true;
            //} 
            this.rdoTygem.Enabled = true;
            this.rdoBack.Enabled = true;
            this.rdo19x19.Enabled = true;
            // checkBox1.Enabled = true;
            //  chkAutoPlay.Enabled = true;
            this.rdo13x13.Enabled = true;
            this.rdo9x9.Enabled = true;
            this.rdoOtherBoard.Enabled = true;
            this.rdoFore.Enabled = true;
            //
            if (UsesManualSelectionType(type))
            {
                this.btnCircleRow1.Enabled = true;
                this.btnCircleBoard.Enabled = true;
            }
            else
                this.btnClickBoard.Enabled = true;
            this.btnOneTimeSync.Enabled = true;
        }

        private void stopKeepingSync()
        {
            Action2<String> a = new Action2<String>(Action2Test);
            if (!isContinuousSyncing)
            {
                Invoke(a, getLangStr("keepSync") + "(" + Program.timename + "ms)");
            }
            startedSync = false;
            if (needForceUnbind)
            {
                needForceUnbind = false;
                LwInterop.lwsoft lwh = new LwInterop.lwsoft();
                lwh.ForceUnBindWindow((int)hwnd);
            }
        }

        public void resetBtnKeepSyncName()
        {
            if (!startedSync)
                this.btnKeepSync.Text = getLangStr("keepSync") + "("+ Program.timename + "ms)";
        }

        [DllImport("user32", SetLastError = true)]
        private static extern int GetWindowText(
            IntPtr hWnd,//窗口句柄
            StringBuilder lpString,//标题
            int nMaxCount //最大值
            );

        //获取类的名字
        [DllImport("user32.dll")]
        private static extern int GetClassName(
            IntPtr hWnd,//句柄
            StringBuilder lpString, //类名
            int nMaxCount //最大值
            );

        [DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("USER32.DLL")]
        static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("USER32.DLL")]
        static extern IntPtr GetShellWindow();
        [DllImport("User32.dll")]
        static extern bool IsIconic(IntPtr hwnd);

        [DllImport("USER32.DLL")]
        static extern bool EnumWindows(EnumWindowsProc enumFunc, int lParam);

        delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);
        /// <summary>Returns a dictionary that contains the handle and title of all opened windows.</summary>
        /// <returns>A dictionary that contains the handle and title of all opened windows.</returns>
        public static IDictionary<IntPtr, string> GetOpenWindows()
        {
            IntPtr lShellWindow = GetShellWindow();
            Dictionary<IntPtr, string> lWindows = new Dictionary<IntPtr, string>();
            EnumWindows(delegate (IntPtr hWnd, int lParam)
            {
                if (hWnd == lShellWindow) return true;
                if (!IsWindowVisible(hWnd)) return true;
                if (IsIconic(hWnd)) return true;
                lWindows[hWnd] = "";
                return true;
            }, 0);

            return lWindows;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(IntPtr window, EnumWindowProc callback, IntPtr i);

        /// <summary>
        /// Delegate for the EnumChildWindows method
        /// </summary>
        /// <param name="hWnd">Window handle</param>
        /// <param name="parameter">Caller-defined variable; we use it for a pointer to our list</param>
        /// <returns>True to continue enumerating, false to bail.</returns>
        public delegate bool EnumWindowProc(IntPtr hWnd, IntPtr parameter);
        public static List<IntPtr> GetChildWindows(IntPtr parent)
        {
            List<IntPtr> result = new List<IntPtr>();
            GCHandle listHandle = GCHandle.Alloc(result);
            try
            {
                EnumWindowProc childProc = new EnumWindowProc(EnumWindow);
                EnumChildWindows(parent, childProc, GCHandle.ToIntPtr(listHandle));
            }
            finally
            {
                if (listHandle.IsAllocated)
                    listHandle.Free();
            }
            return result;
        }

        private static bool EnumWindow(IntPtr handle, IntPtr pointer)
        {
            GCHandle gch = GCHandle.FromIntPtr(pointer);
            List<IntPtr> list = gch.Target as List<IntPtr>;
            if (list == null)
            {
                throw new InvalidCastException("GCHandle Target could not be cast as List<IntPtr>");
            }
            list.Add(handle);
            //  You can modify this to check to see if you want to cancel the operation, then return a null here
            return true;
        }

        private String EnumWindowByProcess(String processName, String likeClassName)
        {
            String hwnds = "";
            IDictionary<IntPtr, string> winds = GetOpenWindows();
            foreach (var item in winds)
            {
                int pid = -1;
                GetWindowThreadProcessId(item.Key, out pid);
                Process proc = Process.GetProcessById(pid);
                if (proc.ProcessName.Contains(processName))
                {
                    List<IntPtr> allHwnds = new List<IntPtr>();
                    allHwnds.Add(item.Key);
                    foreach (IntPtr subHwnd in GetChildWindows(item.Key))
                    {
                        if (!IsWindowVisible(subHwnd))
                            continue;
                        if (IsIconic(subHwnd))
                            continue;
                        allHwnds.Add(subHwnd);                     
                    }
                    foreach (IntPtr oneHwnd in allHwnds)
                    {
                        if(GetClassNameStr(oneHwnd).Contains(likeClassName))
                        hwnds += oneHwnd + ",";
                    }
                }
            }
            if (hwnds.EndsWith(","))
                hwnds = hwnds.Substring(0, hwnds.Length - 1);
            return hwnds;
        }

        private void startContinuous()
        {
            Action2<String> a = new Action2<String>(setContinuousSync);
            Invoke(a, "");
            RECT rect = new RECT();
            while (isContinuousSyncing)
            {
                if (threadKeepSync != null && threadKeepSync.IsAlive)
                {
                    Thread.Sleep(100);
                    continue;
                }
                if (startedSync)
                {
                    Thread.Sleep(100);
                    continue;
                }
                hwnd = new IntPtr(-1);
                int finalWidth = 0;
                int x1;
                int y1;
                int x2;
                int y2;
                if (Program.showInBoard)
                    Send("noinboard");
                if (IsFoxSyncType(type))
                {
                    // hwnd = dm.FindWindow("#32770", "");
                    // hwndFoxPlace = -1;
                    String hwnds = EnumWindowByProcess("foxwq", "#32770");
                    string[] hwndArray = hwnds.Split(',');
                    foreach (string oneHwnd in hwndArray)
                    {
                        if (oneHwnd.Length == 0)
                            continue;

                        String titleStr = GetTitleStr(new IntPtr(int.Parse(oneHwnd)));
                        if (titleStr.Equals("CChessboardPanel"))
                        {
                            hwnd = new IntPtr(int.Parse(oneHwnd));
                            break;
                            //int hwndTemp = int.Parse(oneHwnd);
                            //p = new IntPtr(hwndTemp);
                            //GetWindowRect(p, ref rect);
                            //x2 = rect.Right;
                            //y2 = rect.Bottom;
                            //x1 = rect.Left;
                            //y1 = rect.Top;
                            //if (x1 >= -9999 && y1 >= -9999 && x2 - x1 > finalWidth || finalWidth == 0)
                            //{
                            //    hwndFoxPlace = hwndTemp;
                            //    finalWidth = x2 - x1;
                            //}
                        }
                    }
                }
                else if (type == 1)
                {
                    String hwnds = EnumWindowByProcess("Tygem", "AfxWnd140u");
                    string[] hwndArray = hwnds.Split(',');
                    foreach (string oneHwnd in hwndArray)
                    {
                        if (oneHwnd.Length == 0)
                            continue;
                        String titleStr = GetTitleStr(new IntPtr(int.Parse(oneHwnd)));
                        String classNameStr = GetClassNameStr(new IntPtr(int.Parse(oneHwnd)));
                        if (classNameStr.Equals("AfxWnd140u") && titleStr.Length == 0)
                        {
                            int hwndTemp = int.Parse(oneHwnd);
                            GetWindowRect(new IntPtr(hwndTemp), ref rect);
                            x2 = rect.Right;
                            y2 = rect.Bottom;
                            x1 = rect.Left;
                            y1 = rect.Top;
                            if (x1 >= -9999 && y1 >= -9999 && (x2 - x1) > 0 && (y2 - y1) > 0 && (float)(x2 - x1) / (float)(y2 - y1) < 1.05 && (float)(x2 - x1) / (float)(y2 - y1) > 0.95)
                            {
                                if (x2 - x1 < finalWidth || finalWidth == 0)
                                {
                                    hwnd = new IntPtr(hwndTemp);
                                    finalWidth = x2 - x1;
                                }
                            }
                        }
                    }
                }
                else if (type == 2)
                {
                    String hwnds = EnumWindowByProcess("Sina", "TLMDSimplePanel");
                    string[] hwndArray = hwnds.Split(',');
                    foreach (string oneHwnd in hwndArray)
                    {
                        if (oneHwnd.Length == 0)
                            continue;
                        int hwndTemp = int.Parse(oneHwnd);
                        GetWindowRect(new IntPtr(hwndTemp), ref rect);
                        x2 = rect.Right;
                        y2 = rect.Bottom;
                        x1 = rect.Left;
                        y1 = rect.Top;
                        String classNameStr = GetClassNameStr(new IntPtr(int.Parse(oneHwnd)));
                        if (!classNameStr.Equals("TLMDSimplePanel"))
                            continue;

                        if (x1 >= -9999 && y1 >= -9999 && x2 - x1 > finalWidth || finalWidth == 0)
                        {
                            hwnd = new IntPtr(hwndTemp);
                            finalWidth = x2 - x1;
                        }

                    }
                }
                if ((int)hwnd > 0 && isContinuousSyncing)
                {
                    startContinuousSync(true);
                }
                Thread.Sleep(100);
            }
        }

        private void startContinuousSync(Boolean isSimpleSync)
        {
            Boolean isRightGoban = true;
            isFirstGetPos = true;
            canUsePrintWindow = true;
            if (Program.showInBoard && type == 0 && !canUseLW)
                Send("foreFoxWithInBoard");
            else
                Send("notForeFoxWithInBoard");
            if (type == 5)
            {
                isJavaFrame = false;
                Action2<String> a = new Action2<String>(startKeepingSync);
                Invoke(a, "");
                sx1 = ox1;
                sy1 = oy1;
                width = ox2 - ox1;
                height = oy2 - oy1;
                if (width <= this.boardW)
                {
                    MessageBox.Show(getLangStr("noSelectedBoardAndFailed"));// (Program.isChn ? "未选择棋盘,同步失败" : "No board has been choosen,Sync failed");
                    stopKeepingSync();
                    return;
                }
            }
            else
            {
                int x1;
                int y1;
                int x2;
                int y2;
                if ((int)hwnd <= 0)
                {
                    MessageBox.Show(getLangStr("noSelectedBoardAndFailed"));//(Program.isChn ? "未选择棋盘,同步失败" : "No board has been choosen,Sync failed");
                    stopKeepingSync();
                    return;
                }

                RECT rect = new RECT();
                GetWindowRect(hwnd, ref rect);
                x2 = rect.Right;
                y2 = rect.Bottom;
                x1 = rect.Left;
                y1 = rect.Top;
                if ((int)x1 == 0 && (int)x2 == 0 && (int)y1 == 0 && (int)y2 == 0)
                {
                    if (!isSimpleSync && startedSync)
                        MessageBox.Show(getLangStr("noSelectedBoardAndFailed"));//(Program.isChn ? "未选择棋盘,同步失败" : "No board has been choosen,Sync failed");
                    stopKeepingSync();
                    return;
                }
                StringBuilder className = new StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);
                if (className.ToString().Equals("SunAwtFrame"))
                {
                    isJavaFrame = true;
                    javaX = x1;
                    javaY = y1;
                }
                else
                {
                    isJavaFrame = false;
                }
                Action2<String> a = new Action2<String>(startKeepingSync);
                Invoke(a, "");
                RgbInfo[] detectedRgbArray;
                int detectedTotalWidth;
                if (IsFoxSyncType(type))
                {
                    rectX1 = x1;
                    rectY1 = y1;
                    if (!GetClassNameStr(hwnd).Equals("#32770"))
                    {
                        if (!isSimpleSync && startedSync)
                            MessageBox.Show(getLangStr("notRightBoard"));
                        isRightGoban = false;
                    }
                    if (!getFoxPos(hwnd, out detectedRgbArray, out detectedTotalWidth))
                    {
                        sx1 = 0;
                        sy1 = 0;
                        width = 0;
                        height = 0;
                        if (isContinuousSyncing)
                        {
                            startedSync = false;
                            return;
                        }
                    }
                }

                if (type == 1)
                {
                    if (!GetClassNameStr(hwnd).ToLower().Equals("afxwnd140u"))
                    {
                        if (!isSimpleSync && startedSync)
                            MessageBox.Show(getLangStr("notRightBoard"));
                        isRightGoban = false;
                    }
                    sx1 = 0;
                    sy1 = 0;
                    width = x2 - x1;
                    height = y2 - y1;
                    if (!GetSupportDpiState(hwnd))
                    {
                        width = (int)(width / factor);
                        height = (int)(height / factor);
                    }
                    if (!getTygemPos(hwnd, out detectedRgbArray, out detectedTotalWidth))
                    {
                        sx1 = 0;
                        sy1 = 0;
                        width = 0;
                        height = 0;
                        if (isContinuousSyncing)
                        {
                            startedSync = false;
                            return;
                        }
                    }
                }
                if (type == 2)
                {
                    if (!(GetClassNameStr(hwnd).ToLower().Equals("tlmdsimplepanel") || GetClassNameStr(hwnd).ToLower().Equals("tpanel")))
                    {
                        if (!isSimpleSync)
                            MessageBox.Show(getLangStr("notRightBoard"));
                        isRightGoban = false;
                    }
                    if (!getSinaPos(hwnd, out detectedRgbArray, out detectedTotalWidth))
                    {
                        sx1 = 0;
                        sy1 = 0;
                        width = 0;
                        height = 0;
                        if (isContinuousSyncing)
                        {
                            startedSync = false;
                            return;
                        }
                    }
                }
                if (type == 3)
                {
                    sx1 = (int)(ox1 - x1);
                    sy1 = (int)(oy1 - y1);
                    width = (int)((ox2 - ox1));
                    height = (int)((oy2 - oy1));
                    if (!GetSupportDpiState(hwnd))
                    {
                        sx1 = (int)(sx1 / factor);
                        sy1 = (int)(sy1 / factor);
                        width = (int)(width / factor);
                        height = (int)(height / factor);
                    }
                }
            }

            keepSync = true;
            widthMagrin = width / (float)boardW;
            heightMagrin = height / (float)boardH;
            if (syncBoth)
            {
                if (radioBlack.Checked)
                {
                    Send("play>black>" + (textBox1.Text.Equals("") ? "0" : textBox1.Text) + " " + (textBox2.Text.Equals("") ? "0" : textBox2.Text) + " " + (textBox3.Text.Equals("") ? "0" : textBox3.Text));
                }
                else if (radioWhite.Checked)
                {
                    Send("play>white>" + (textBox1.Text.Equals("") ? "0" : textBox1.Text) + " " + (textBox2.Text.Equals("") ? "0" : textBox2.Text) + " " + (textBox3.Text.Equals("") ? "0" : textBox3.Text));
                }
            }
            if (!isContinuousSyncing && Program.autoMin && isRightGoban && this.WindowState != FormWindowState.Minimized)
            {
                Action2<String> a = new Action2<String>(minWindow);
                Invoke(a, "");
            }
            if (!isRightGoban && !isContinuousSyncing)
            {
                stopSync();
            }
            threadKeepSync = new Thread(OutPutTime);
            threadKeepSync.SetApartmentState(ApartmentState.STA);
            threadKeepSync.Start();
        }
        private void button5_Click(object sender, EventArgs e)
        {
            if (isContinuousSyncing)
            {
                isContinuousSyncing = false;
                this.btnFastSync.Text = getLangStr("fastSync"); //Program.isChn ? "一键同步" : "FastSync";
            }
            if (!startedSync)
            {
                startContinuousSync(false);
            }
            else
            {
                stopSync();
            }
        }

        private void stopSync()
        {
            Send("stopsync");
            stopKeepingSync();
            keepSync = false;
        }

        public delegate void Action2<in T>(T t);

        private void OutPutTime()
        {
            LwInterop.lwsoft lwh = null;
            Send("sync");
            Boolean firstTime = true;
            while (keepSync)
            {
                int x1;
                int y1;
                int x2;
                int y2;
                RECT rect = new RECT();
                GetWindowRect(hwnd, ref rect);
                x2 = rect.Right;
                y2 = rect.Bottom;
                x1 = rect.Left;
                y1 = rect.Top;
                if (Program.showInBoard && type != 5)
                {
                    //    if (!Program.isAdvScale)
                    //   {
                    //if (type == 1)
                    //        Send("inboard " + (int)(sx1 + x1 / factor) + " " + (int)(sy1 + y1 / factor) + " " + (int)(width) + " " + (int)(height) + " " + (factor > 1 ? "99_" + factor + "_" + type.ToString() : type.ToString()));
                    //    else if (type == 2)
                    //        Send("inboard " + (int)(sx1 + x1 / factor) + " " + (int)((sy1 + y1 / factor)) + " " + (int)(width) + " " + (int)(height) + " " + (factor > 1 ? "99_" + factor + "_" + type.ToString() : type.ToString()));
                    //    else if (type == 3)
                    if (GetSupportDpiState(hwnd))
                        Send("inboard " + (int)((sx1 + x1) / factor) + " " + (int)((sy1 + y1) / factor) + " " + Math.Round(width / factor) + " " + Math.Round(height / factor) + " " + (factor > 1 ? "99_" + factor + "_" + type.ToString() : type.ToString()));
                    else
                        Send("inboard " + (sx1 + (int)(x1 / factor)) + " " + (sy1 + (int)(y1 / factor)) + " " + width + " " + height + " " + (factor > 1 ? "99_" + factor + "_" + type.ToString() : type.ToString()));

                    //else
                    //    Send("inboard " + (int)((sx1 + x1) / factor) + " " + (int)((sy1 +y1) / factor) + " " + (int)(width / factor) + " " + (int)(height / factor) + " " + (factor > 1 ? "99_" + factor + "_" + type.ToString() : type.ToString()));
                    ////}
                    //else
                    //{
                    //    if (type == 1)
                    //        Send("inboard " + (int)((sx1 + (int)startX) / factor) + " " + (int)((sy1 + (int)startY) / factor) + " " + (int)(width / factor) + " " + (int)(height / factor) + " " + (factor > 1 ? "99_" + factor + "_" + type.ToString() : type.ToString()));
                    //    else if (type == 2)
                    //        Send("inboard " + (int)((sx1 + (int)startX) / factor) + " " + (int)((sy1 + (int)startY) / factor) + " " + (int)(width / factor) + " " + (int)(height / factor) + " " + (factor > 1 ? "99_" + factor + "_" + type.ToString() : type.ToString()));
                    //    else
                    //        Send("inboard " + (int)((sx1 + (int)startX) / factor) + " " + (int)((sy1 + (int)startY) / factor) + " " + (int)(width / factor) + " " + (int)(height / factor) + " " + (factor > 1 ? "99_" + factor + "_" + type.ToString() : type.ToString()));
                    //}
                    //    sx1, sy1,width,height
                }
                if (type == 5)
                {
                    OutPut3(firstTime);
                }
                else
                {

                    int size = width * height;
                    if ((int)x1 == 0 && (int)x2 == 0 && (int)y1 == 0 && (int)y2 == 0)
                    {
                        //MessageBox.Show("请选择棋盘,同步停止");
                        Send("stopsync");
                        stopKeepingSync();
                        return;
                    }
                    if (isJavaFrame)
                    {
                        javaX = x1;
                        javaY = y1;
                    }
                    RgbInfo[] rgbArray = new RgbInfo[0];
                    int totalWidth = -1;
                    if (IsFoxSyncType(type))
                    {
                        rectX1 = x1;
                        rectY1 = y1;

                        if (!getFoxPos(hwnd, out rgbArray, out totalWidth))
                        {
                            sx1 = 0;
                            sy1 = 0;
                            width = 0;
                            height = 0;
                            if (isContinuousSyncing)
                            {
                                stopKeepingSync();
                                return;
                            }
                        }
                    }
                    if (type == 1)
                    {
                        sx1 = 0;
                        sy1 = 0;
                        width = x2 - x1;
                        height = y2 - y1;
                        if (!GetSupportDpiState(hwnd))
                        {
                            width = (int)(width / factor);
                            height = (int)(height / factor);
                        }
                        if (!getTygemPos(hwnd, out rgbArray, out totalWidth))
                        {
                            sx1 = 0;
                            sy1 = 0;
                            width = 0;
                            height = 0;
                            if (isContinuousSyncing)
                            {
                                stopKeepingSync();
                                return;
                            }
                        }
                    }
                    if (type == 2)
                    {
                        if (!getSinaPos(hwnd, out rgbArray, out totalWidth))
                        {
                            sx1 = 0;
                            sy1 = 0;
                            width = 0;
                            height = 0;
                            if (isContinuousSyncing)
                            {
                                stopKeepingSync();
                                return;
                            }
                        }
                    }
                    Boolean changedSize = size != width * height;
                    if (changedSize)
                    {
                        Send("clear");
                    }
                    if (type == 3)
                    {
                        OutPut3(firstTime);
                    }
                    else
                    {
                        if (canUseLW && type == 0)
                        {
                            OutPut(firstTime, rgbArray, lwh, totalWidth);
                        }
                        else
                            OutPut(firstTime, rgbArray, null, totalWidth);
                    }
                }
                try
                {
                    Thread.Sleep(Program.timeinterval);
                }
                catch (Exception) { }
                firstTime = false;
            }
            Send("stopsync");
        }

        class RgbInfo
        {
            public Byte r;
            public Byte g;
            public Byte b;
        }

        private Boolean recognizeMove(Bitmap input, int x, int y)
        {
            if (input == null)
                return true;
            Rectangle rect = new Rectangle(0, 0, input.Width, input.Height);
            const int PixelWidth = 3;
            const PixelFormat PixelFormat = PixelFormat.Format24bppRgb;
            BitmapData data = input.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat);
            RgbInfo[] rgbArray = new RgbInfo[data.Height * data.Width];
            byte[] pixelData = new Byte[data.Stride];
            for (int scanline = 0; scanline < data.Height; scanline++)
            {
                Marshal.Copy(data.Scan0 + (scanline * data.Stride), pixelData, 0, data.Stride);
                for (int pixeloffset = 0; pixeloffset < data.Width; pixeloffset++)
                {
                    rgbArray[scanline * data.Width + pixeloffset] = new RgbInfo();
                    rgbArray[scanline * data.Width + pixeloffset].r = pixelData[pixeloffset * PixelWidth + 2];
                    rgbArray[scanline * data.Width + pixeloffset].g = pixelData[pixeloffset * PixelWidth + 1];
                    rgbArray[scanline * data.Width + pixeloffset].b = pixelData[pixeloffset * PixelWidth];
                }
            }
            input.UnlockBits(data);
            int blackPercentStandard = Program.blackZB;
            int whitePercentStandard = Program.whiteZB;
            int blackOffsetStandard = Program.blackPC;
            int whiteOffsetStandard = Program.whitePC;
            int grayOffsetStandard = Program.grayOffset;
            if (type != 3 && type != 5)
            {
                blackPercentStandard = 37;

                whitePercentStandard = 30;
                blackOffsetStandard = 96;
                if (type == 1)
                    whiteOffsetStandard = 80;
                else
                    whiteOffsetStandard = 112;
                grayOffsetStandard = 50;
            }

            int blackPercent =
                       getWhiteBlackColorPercent(
                           rgbArray, 0, 0, input.Width, input.Height, true, blackOffsetStandard, grayOffsetStandard, input.Width, input.Height);
            if (blackPercent >= blackPercentStandard)
            {
                input.Dispose();
                return true;
            }
            else
            {
                Boolean isWhite = false;
                int whitePercent =
                    getWhiteBlackColorPercent(
                        rgbArray, 0, 0, input.Width, input.Height, false, whiteOffsetStandard, grayOffsetStandard, input.Width, input.Height);                
                if (whitePercent >= whitePercentStandard)
                {
                    int pureWhitePercent = getWhiteBlackColorPercent(
                        rgbArray, 0, 0, input.Width, input.Height, false, 30, grayOffsetStandard, input.Width, input.Height);
                    int almostWhitePercent = getWhiteBlackColorPercent(
                        rgbArray, 0, 0, input.Width, input.Height, false, 65, grayOffsetStandard, input.Width, input.Height);
                    Boolean trueWhite = getTrueWhite(rgbArray, 0, 0, input.Width, input.Height, input.Width, input.Height);
                    if (!trueWhite && almostWhitePercent - pureWhitePercent < 10) isWhite = false;
                    else isWhite = true; 
                }
                input.Dispose();
                return isWhite;
            }
        }

        private void recognizeBoard(RgbInfo[] rgbArray)
        {
            recognizeBoard(rgbArray, -1);
        }

        private void recognizeBoard(RgbInfo[] rgbArray, int totalWidth)
        {
            int startX = 0;
            int startY = 0;
            int allWidth;
            int allHeight;
            if (rgbArray == null || rgbArray.Length == 0 || totalWidth < 0)
            {
                Bitmap input = null;
                if (type == 5)
                {
                    input = new Bitmap(width, height);
                    using (System.Drawing.Graphics graphics = Graphics.FromImage(input))
                    {
                        graphics.CopyFromScreen(sx1, sy1, 0, 0, new System.Drawing.Size(width, height));
                    }
                }
                else input = GetWindowBmp(hwnd, sx1, sy1, width, height);
                if (input == null || input.Width <= boardW || input.Height <= boardH)
                    return;
                allWidth = width;
                allHeight = height;
                Rectangle rect = new Rectangle(0, 0, input.Width, input.Height);
                const int PixelWidth = 3;
                const PixelFormat PixelFormat = PixelFormat.Format24bppRgb;
                BitmapData data = input.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat);
                rgbArray = new RgbInfo[data.Height * data.Width];
                byte[] pixelData = new Byte[data.Stride];
                for (int scanline = 0; scanline < data.Height; scanline++)
                {
                    Marshal.Copy(data.Scan0 + (scanline * data.Stride), pixelData, 0, data.Stride);
                    for (int pixeloffset = 0; pixeloffset < data.Width; pixeloffset++)
                    {
                        // PixelFormat.Format32bppRgb means the data is stored
                        // in memory as BGR. We want RGB, so we must do some 
                        // bit-shuffling.
                        rgbArray[scanline * data.Width + pixeloffset] = new RgbInfo();
                        rgbArray[scanline * data.Width + pixeloffset].r = pixelData[pixeloffset * PixelWidth + 2];
                        rgbArray[scanline * data.Width + pixeloffset].g = pixelData[pixeloffset * PixelWidth + 1];
                        rgbArray[scanline * data.Width + pixeloffset].b = pixelData[pixeloffset * PixelWidth];
                    }
                }
                input.UnlockBits(data);
                input.Dispose();
            }
            else
            {
                startX = sx1;
                startY = sy1;
                allWidth = totalWidth;
                allHeight = sy1 + height;
            }
            float hGap = height / (float)boardH;
            float vGap = width / (float)boardW;
            int hGapInt = (int)Math.Round(hGap);
            int vGapInt = (int)Math.Round(vGap);

            int blackMinPercent = 200;
            int blackTotalPercent = 0;
            int blackMinX = -1;
            int blackMinY = -1;
            int blackCounts = 0;

            int whiteMinPercent = 200;
            int whiteTotalPercent = 0;
            int whiteMinX = -1;
            int whiteMinY = -1;
            int whiteCounts = 0;
            int[] resultValue = new int[boardH * boardW];

            int blackPercentStandard = Program.blackZB;
            int whitePercentStandard = Program.whiteZB;
            int blackOffsetStandard = Program.blackPC;
            int whiteOffsetStandard = Program.whitePC;
            int grayOffsetStandard = Program.grayOffset;
            //Boolean isPlatform = false;
            Boolean needCheckRedBlue = true;
            int redCount = 0;
            int blueCount = 0;
            int blueRedX = -1;
            int blueRedY = -1;
            String result = "";
            if (type != 3 && type != 5)
            {
                //isPlatform = true;
                blackPercentStandard = 37;

                whitePercentStandard = 30;
                blackOffsetStandard = 96;
                if (type == 1)
                    whiteOffsetStandard = 80;
                else
                    whiteOffsetStandard = 112;
                grayOffsetStandard = 50;
            }
            for (int y = 0; y < boardH; y++)
            {
                for (int x = 0; x < boardW; x++)
                {
                    Boolean isBlack = false;
                    Boolean isWhite = false;
                    Boolean isLastMove = false;
                    int blackPercent =
                        getWhiteBlackColorPercent(
                            rgbArray, startX + (int)Math.Round(x * vGap), startY + (int)Math.Round(y * hGap), vGapInt, hGapInt, true, blackOffsetStandard, grayOffsetStandard, allWidth, allHeight);
                    if (blackPercent >= blackPercentStandard)
                    {
                        isBlack = true;
                        blackTotalPercent += blackPercent;
                        blackCounts++;
                        if (blackPercent < blackMinPercent)
                        {
                            blackMinPercent = blackPercent;
                            blackMinX = x;
                            blackMinY = y;
                        }
                    }
                    else
                    {
                        int whitePercent =
                            getWhiteBlackColorPercent(
                                rgbArray, startX + (int)Math.Round(x * vGap), startY + (int)Math.Round(y * hGap), vGapInt, hGapInt, false, whiteOffsetStandard, grayOffsetStandard, allWidth, allHeight);
                        if (whitePercent >= whitePercentStandard)
                        {
                            if (whitePercent >= whitePercentStandard)
                            {
                                int pureWhitePercent = getWhiteBlackColorPercent(
                                rgbArray, startX + (int)Math.Round(x * vGap), startY + (int)Math.Round(y * hGap), vGapInt, hGapInt, false, 30, grayOffsetStandard, allWidth, allHeight);
                                int almostWhitePercent = getWhiteBlackColorPercent(
                                rgbArray, startX + (int)Math.Round(x * vGap), startY + (int)Math.Round(y * hGap), vGapInt, hGapInt, false, 65, grayOffsetStandard, allWidth, allHeight);
                                Boolean trueWhite = getTrueWhite(rgbArray, startX + (int)Math.Round(x * vGap), startY + (int)Math.Round(y * hGap), vGapInt, hGapInt, allWidth, allHeight);
                                if (!trueWhite && almostWhitePercent - pureWhitePercent < 10) isWhite = false;
                                else isWhite = true;
                            }
                        }
                        if (isWhite)
                        {

                            whiteTotalPercent += whitePercent;
                            whiteCounts++;
                            if (whitePercent < whiteMinPercent)
                            {
                                whiteMinPercent = whitePercent;
                                whiteMinX = x;
                                whiteMinY = y;
                            }
                        }
                    }
                    if (needCheckRedBlue && (isWhite || isBlack))
                    {
                        int redPercent = getRedBlueColorPercent(
                                rgbArray, startX + (int)Math.Round(x * vGap), startY + (int)Math.Round(y * hGap), vGapInt, hGapInt, false, allWidth, allHeight);
                        int bluePercent = getRedBlueColorPercent(
                                rgbArray, startX + (int)Math.Round(x * vGap), startY + (int)Math.Round(y * hGap), vGapInt, hGapInt, true, allWidth, allHeight);
                        if (bluePercent >= 1)
                        {
                            blueCount++;
                            if (redCount > 1 && blueCount > 1) needCheckRedBlue = false;
                            else
                            {
                                blueRedX = x;
                                blueRedY = y;
                            }
                        }
                        if (redPercent >= 1)
                        {
                            redCount++;
                            if (redCount > 1 && blueCount > 1) needCheckRedBlue = false;
                            else
                            {
                                blueRedX = x;
                                blueRedY = y;
                            }
                        }
                    }
                    if (isBlack)
                        if (isLastMove)
                            resultValue[y * boardW + x] = 3;
                        else
                            resultValue[y * boardW + x] = 1;
                    else if (isWhite)
                        if (isLastMove)
                            resultValue[y * boardW + x] = 4;
                        else
                            resultValue[y * boardW + x] = 2;
                    else
                        resultValue[y * boardW + x] = 0;
                }
            }
            if ((redCount == 1 && blueCount != 1) || (redCount != 1 && blueCount == 1))
            {
                if (resultValue[blueRedY * boardW + blueRedX] == 1)
                    resultValue[blueRedY * boardW + blueRedX] = 3;
                else if (resultValue[blueRedY * boardW + blueRedX] == 2)
                    resultValue[blueRedY * boardW + blueRedX] = 4;
            }
            else
            {
                if (blackCounts >= 2 && whiteCounts >= 2)
                {
                    float blackMaxOffset =
                        Math.Abs(
                            blackMinPercent - (blackTotalPercent - blackMinPercent) / (float)(blackCounts - 1));
                    float whiteMaxOffset =
                        Math.Abs(
                            whiteMinPercent - (whiteTotalPercent - whiteMinPercent) / (float)(whiteCounts - 1));
                    if (blackMaxOffset >= whiteMaxOffset)
                    {
                        if (blackMinY >= 0 && blackMinX >= 0)
                            resultValue[blackMinY * boardW + blackMinX] = 3;
                    }
                    else
                    {
                        if (whiteMinY >= 0 && whiteMinX >= 0)
                            resultValue[whiteMinY * boardW + whiteMinX] = 4;
                    }
                }
                else if (blackCounts > 0 && whiteCounts > 0)
                {
                    if (blackCounts > whiteCounts)
                    {
                        if (blackMinY >= 0 && blackMinX >= 0)
                            resultValue[blackMinY * boardW + blackMinX] = 3;
                    }
                    if (blackCounts < whiteCounts)
                    {
                        if (whiteMinY >= 0 && whiteMinX >= 0)
                            resultValue[whiteMinY * boardW + whiteMinX] = 4;
                    }
                }
            }
            Boolean allBlack = true;
            Boolean allWhite = true;
            List<String> sendList = new List<string>();
            for (int i = 0; i < boardH; i++)
            {
                for (int j = 0; j < boardW; j++)
                {
                    int resultHere = resultValue[i * boardW + j];
                    if (resultHere == 0)
                    {
                        allBlack = false;
                        allWhite = false;
                    }
                    if (resultHere == 2 || resultHere == 4)
                        allBlack = false;
                    if (resultHere == 1 || resultHere == 3)
                        allWhite = false;
                    result += resultHere + ",";
                    if (j == (boardW - 1))
                    {
                        result = result.Substring(0, result.Length - 1);
                        //Send("re=" + result);
                        sendList.Add("re=" + result);
                        result = "";
                    }
                }
            }
            if (!allWhite && !allBlack)
            {
                foreach (String line in sendList)
                {
                    Send(line);
                }
                Send("end");
            }
            if (allBlack)
            {
                canUsePrintWindow = false;
                if (!startedSync && !isSecondTime)
                {
                    isSecondTime = true;
                    oneTimeSync();
                }
            }
        }

        private int getRedBlueColorPercent(RgbInfo[] rgbArray, int startX, int startY, int width, int height, Boolean isBlue, int totalWidth, int totalHeight)
        {
            int sum = 0;
            if (startX + width > totalWidth) startX = totalWidth - width;
            if (startY + height > totalHeight) startY = totalHeight - height;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (startY + y) * totalWidth + startX + x;
                    if (index >= rgbArray.Length)
                        break;
                    RgbInfo rgbInfo = rgbArray[index];
                    int red = rgbInfo.r;
                    int green = rgbInfo.g;
                    int blue = rgbInfo.b;
                    if (isBlue)
                    {
                        if (red <= 50 && green <= 50 && blue >= 150)
                        {
                            sum++;
                        }
                    }
                    else
                    {
                        if (red >= 150 && green <= 50 && blue <= 50)
                        {
                            sum++;
                        }
                    }
                }
            }
            return (100 * sum) / (width * height);
        }



        private bool getTrueWhite(RgbInfo[] rgbArray, int startX, int startY, int width, int height, int totalWidth, int totalHeight)
        {
            Boolean trueWhite = false;
            int pureWhiteValue = 255 - 30;
            if (startX + width > totalWidth)
                startX = totalWidth - width;
            if (startY + height > totalHeight)
                startY = totalHeight - height;           
            int y = (int)Math.Round(height / 4.0);
                for (int x = 0; x < Math.Round(width * 2.0 / 6.0); x++)
                {
                    int index = (startY + y) * totalWidth + startX + x;
                    if (index >= rgbArray.Length)
                        break;
                    RgbInfo rgbInfo = rgbArray[index];
                    int red = rgbInfo.r;
                    int green = rgbInfo.g;
                    int blue = rgbInfo.b;
                    if (red < pureWhiteValue || blue < pureWhiteValue || green < pureWhiteValue)
                    {
                        trueWhite = true;
                        break;
                    }                   
                }            
            return trueWhite;
        }

        private int getWhiteBlackColorPercent(RgbInfo[] rgbArray, int startX, int startY, int width, int height, Boolean isBlack, int offset, int grayOffset, int totalWidth, int totalHeight)
        {
            int sum = 0;
            if (startX + width > totalWidth) startX = totalWidth - width;
            if (startY + height > totalHeight) startY = totalHeight - height;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (startY + y) * totalWidth + startX + x;
                    if (index >= rgbArray.Length)
                        break;
                    RgbInfo rgbInfo = rgbArray[index];
                    int red = rgbInfo.r;
                    int green = rgbInfo.g;
                    int blue = rgbInfo.b;
                    if (Math.Abs(red - green) < grayOffset
                        && Math.Abs(green - blue) < grayOffset
                        && Math.Abs(blue - red) < grayOffset)
                    {
                        if (isBlack)
                        {
                            if (red <= offset && green <= offset && blue <= offset)
                            {
                                sum++;
                            }
                        }
                        else
                        {
                            int value = 255 - offset;
                            if (red >= value && green >= value && blue >= value)
                            {
                                sum++;
                            }
                        }
                    }
                }
            }
            return (100 * sum) / (width * height);
        }

        private void OutPut(Boolean first, RgbInfo[] rgbArray, LwInterop.lwsoft lwh, int totalWidth)
        {
            if (width < this.boardW || height < this.boardH)
                return;
            if (savedPlace && syncBoth)
            {
                lwh = new LwInterop.lwsoft();
                lwh.BindWindow((int)hwnd, 0, 4, 0, 0, 0);
                needForceUnbind = true;
                savedPlace = false;
                int times = 10;
                do
                {
                    lwh.MoveTo((int)Math.Round(sx1 + widthMagrin * (savedX + 0.5)), (int)Math.Round(sy1 + heightMagrin * (savedY + 0.5)));
                    lwh.LeftClick();
                    times--;
                } while (Program.verifyMove && !VerifyMove(savedX, savedY, true) && times > 0);
                lwh.UnBindWindow();
            }
            if (first)
                Send("start " + boardW + " " + boardH + " " + hwnd);
            recognizeBoard(rgbArray, totalWidth);
        }

        private void OutPut3(Boolean first)
        {
            if (width < this.boardW || height < this.boardH)
                return;
            if (first)
                Send("start " + boardW + " " + boardH);
            recognizeBoard(null);
        }

        private void button6_Click(object sender, EventArgs e)
        {
            Send("clear");
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoFox.Checked)
                setNativeBoardMode(TYPE_FOX);
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoTygem.Checked)
                setNativeBoardMode(TYPE_TYGEM);
        }

        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoBack.Checked)
                setManualSelectionMode(TYPE_BACKGROUND);
        }

        private void radioButton4_CheckedChanged(object sender, EventArgs e)
        {

            if (this.rdoSina.Checked)
                setNativeBoardMode(TYPE_SINA);

        }

        private void radioButtonFoxBack_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoFoxBack.Checked)
                setNativeBoardMode(TYPE_FOX_BACKGROUND_PLACE);
        }

        public void saveOtherConfig()
        {
            string result1 = "config_readboard_others.txt";
            FileStream fs = new FileStream(result1, FileMode.Create);
            StreamWriter wr = null;
            wr = new StreamWriter(fs);
            int customW = -1;
            int customH = -1;
            posX = this.Location.X;
            posY = this.Location.Y;
            try
            {
                customW = int.Parse(txtBoardWidth.Text);
                customH = int.Parse(txtBoardHeight.Text);
            }
            catch (Exception)
            {
            }
            wr.WriteLine(Program.version+"_"+this.boardW + "_" + this.boardH + "_" + customW + "_" + customH + "_" + Program.timeinterval + "_" + (syncBoth ? "1" : "0") + "_" + Program.grayOffset + "_" + posX + "_" + posY + "_" + (Program.useEnhanceScreen ? "1" : "0") + "_" + (Program.playPonder ? "1" : "0"));
            wr.Close();
        }

        public void shutdown()
        {
            isContinuousSyncing = false;
            keepSync = false;
            string result1 = "config_readboard.txt";
            FileStream fs = new FileStream(result1, FileMode.Create);
            StreamWriter wr = null;
            wr = new StreamWriter(fs);
            wr.WriteLine(Program.blackPC + "_" + Program.blackZB + "_" + Program.whitePC + "_" + Program.whiteZB + "_" + (Program.useMag ? "1" : "0") + "_" + (Program.verifyMove ? "1" : "0") + "_" + (Program.showScaleHint ? "1" : "0") + "_" + (Program.showInBoard ? "1" : "0") + "_" + (Program.showInBoardHint ? "1" : "0") + "_" + (Program.autoMin ? "1" : "0") + "_" + Environment.GetEnvironmentVariable("computername").Replace("_", "") + "_" + type);
            wr.Close();
            saveOtherConfig();
            mh.Enabled = false;
            Send("stopsync");
            Send("nobothSync");
            Send("endsync");
            if (needForceUnbind)
            {
                LwInterop.lwsoft lwh = new LwInterop.lwsoft();
                lwh.ForceUnBindWindow((int)hwnd);
            }
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            Application.Exit();
        }

        private void form_closing(object sender, FormClosingEventArgs e)
        {
            shutdown();
        }

        private void radioButton5_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdo19x19.Checked)
            {
                boardW = 19;
                boardH = 19;
                this.txtBoardHeight.BackColor = System.Drawing.SystemColors.Menu;
                this.txtBoardWidth.BackColor = System.Drawing.SystemColors.Menu;
            }
            saveOtherConfig();
        }

        private void radioButton6_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdo13x13.Checked)
            {
                boardW = 13;
                boardH = 13;
                this.txtBoardHeight.BackColor = System.Drawing.SystemColors.Menu;
                this.txtBoardWidth.BackColor = System.Drawing.SystemColors.Menu;
            }
            saveOtherConfig();
        }

        private void radioButton7_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdo9x9.Checked)
            {
                boardW = 9;
                boardH = 9;
                this.txtBoardHeight.BackColor = System.Drawing.SystemColors.Menu;
                this.txtBoardWidth.BackColor = System.Drawing.SystemColors.Menu;
            }
            saveOtherConfig();
        }

        public void sendVersion()
        {
            Send("version: " + Program.version);
        }

        public void stopInBoard()
        {
            this.chkShowInBoard.Checked = false;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetForegroundWindow", CharSet = System.Runtime.InteropServices.CharSet.Auto, ExactSpelling = true)]
        public static extern IntPtr GetF();

        public void lossFocus()
        {
            if (GetF() != FindWindow("SunAwtDialog", "FloatBoard"))//dm.FindWindow("SunAwtDialog", "FloatBoard"))              
            {
                mouse_event((int)(MouseEventFlags.MiddleDown | MouseEventFlags.Absolute), 0, 0, 0, IntPtr.Zero);
                mouse_event((int)(MouseEventFlags.MiddleUp | MouseEventFlags.Absolute), 0, 0, 0, IntPtr.Zero);
            }
        }

        //class MoveInfo
        //{
        //    public int x;
        //    public int y;
        //}

        [DllImport("USER32.DLL")]
        public static extern void SwitchToThisWindow(IntPtr hwnd, Boolean fAltTab);

        [DllImport("USER32.DLL")]
        public static extern IntPtr GetParent(IntPtr hwnd);



        private void placeStone(int x, int y)
        {
            if (isJavaFrame)
            {
                if (type != 5)
                    SwitchToThisWindow(hwnd, true);
                foreMouseClick(javaX + sx1 + (int)(widthMagrin * (x + 0.5)), javaY + sy1 + (int)(heightMagrin * (y + 0.5)), true, false);
            }
            else if (type == 5)
            {
                foreMouseClick(sx1 + (int)(widthMagrin * (x + 0.5)), sy1 + (int)(heightMagrin * (y + 0.5)), true, false);
            }
            else if (type == 0 && !canUseLW)
            {
                IntPtr fatherHwnd = FindWindow("#32770", "");// new IntPtr(dm.FindWindow("#32770", ""));
                SwitchToThisWindow(fatherHwnd, true);
                foreMouseClick((int)Math.Round(rectX1 + sx1 + widthMagrin * (x + 0.5)), (int)Math.Round(rectY1 + sy1 + heightMagrin * (y + 0.5)), true,true);
            }
            else if (type == TYPE_FOX_BACKGROUND_PLACE)
            {
                int clientX = (int)Math.Round(sx1 + widthMagrin * (x + 0.5));
                int clientY = (int)Math.Round(sy1 + heightMagrin * (y + 0.5));
                backMouseClickSync(clientX, clientY, hwnd);
            }
            else
            {
                int xx = 0;
                int yy = 0;
                Boolean isScaled = GetSupportDpiState(hwnd);
                xx = (int)Math.Round((sx1 + widthMagrin * (x + 0.5)) * (isScaled ? 1 : factor));
                yy = (int)Math.Round((sy1 + heightMagrin * (y + 0.5)) * (isScaled ? 1 : factor));
                backMouseClick(xx, yy, hwnd);
            }
        }

        private Boolean VerifyMove(int x, int y, Boolean isLw)
        {
            Thread.Sleep(200);
            int startX = (int)Math.Round(sx1 + widthMagrin * x);
            int startY = (int)Math.Round(sy1 + heightMagrin * y);
            int width = (int)Math.Round(widthMagrin);
            int height = (int)Math.Round(heightMagrin);
            Bitmap bmp = null;
            if (type == 5)
            {
                bmp = new Bitmap(width, height);
                using (System.Drawing.Graphics graphics = Graphics.FromImage(bmp))
                {
                    graphics.CopyFromScreen(startX, startY, 0, 0, new System.Drawing.Size(width, height));
                }
            }
            else
                bmp = GetWindowBmp(hwnd, startX, startY, width, height);
            return recognizeMove(bmp, x, y);
        }

        public void placeMove(int x, int y)
        {
            if (!keepSync || !syncBoth || width < boardW)
                return;
            int times = 10;
            bool placed = false;
            if ((type == 0) && canUseLW)
            {
                savedPlace = true;
                savedX = x;
                savedY = y;
                placed = true;
            }
            else
            {
                do
                {
                    placeStone(x, y);
                    times--;
                    if (!Program.verifyMove)
                    {
                        placed = true;
                        break;
                    }
                    placed = VerifyMove(x, y, false);
                    if (placed)
                        break;
                } while (times > 0);
            }
            if (placed)
                Send("placeComplete");
            else
                Send("error place failed");
        }

        private const int MK_LBUTTON = 0x0001;
        uint WM_MOUSEMOVE = 0x200;
        uint WM_LBUTTONDOWN = 0x201;
        uint WM_LBUTTONUP = 0x202;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        private void backMouseClick(int x, int y, IntPtr hwnd)
        {
            int lParam = x + (y << 16);
            PostMessage(hwnd, WM_LBUTTONDOWN, 0, lParam);
            PostMessage(hwnd, WM_LBUTTONUP, 0, lParam);
        }

        private void backMouseClickSync(int x, int y, IntPtr hwnd)
        {
            int lParam = x + (y << 16);
            SendMessage(hwnd, WM_MOUSEMOVE, 0, lParam);
            SendMessage(hwnd, WM_LBUTTONDOWN, MK_LBUTTON, lParam);
            SendMessage(hwnd, WM_LBUTTONUP, 0, lParam);
        }

        public enum MouseEventFlags
        {
            Move = 0x0001,
            LeftDown = 0x0002,
            LeftUp = 0x0004,
            RightDown = 0x0008,
            RightUp = 0x0010,
            MiddleDown = 0x0020,
            MiddleUp = 0x0040,
            Wheel = 0x0800,
            Absolute = 0x8000
        }
        [DllImport("user32.dll")]
        private static extern int SetCursorPos(int x, int y);
        [DllImport("User32")]
        public extern static void mouse_event(int dwFlags, int dx, int dy, int dwData, IntPtr dwExtraInfo);

        private void foreMouseClick(int x, int y, Boolean moveBack, Boolean isFox)
        {
            if (moveBack)
            {
                int curX = Control.MousePosition.X;
                int curY = Control.MousePosition.Y;
                SetCursorPos(x, y);
                mouse_event((int)(MouseEventFlags.LeftUp | MouseEventFlags.Absolute), 0, 0, 0, IntPtr.Zero);
                mouse_event((int)(MouseEventFlags.LeftDown | MouseEventFlags.Absolute), 0, 0, 0, IntPtr.Zero);
                if (isFox)
                    Thread.Sleep(50);
                mouse_event((int)(MouseEventFlags.LeftUp | MouseEventFlags.Absolute), 0, 0, 0, IntPtr.Zero);
                SetCursorPos(curX, curY);
            }
            else
            {
                mouse_event((int)(MouseEventFlags.LeftUp | MouseEventFlags.Absolute), 0, 0, 0, IntPtr.Zero);
                mouse_event((int)(MouseEventFlags.LeftDown | MouseEventFlags.Absolute), 0, 0, 0, IntPtr.Zero);
                mouse_event((int)(MouseEventFlags.LeftUp | MouseEventFlags.Absolute), 0, 0, 0, IntPtr.Zero);
            }
        }

        private void textbox1_TextChanged(object sender, EventArgs e)
        {
            var reg = new Regex("^[0-9]*$");
            var str = textBox1.Text.Trim();
            var sb = new StringBuilder();
            if (!reg.IsMatch(str))
            {
                for (int i = 0; i < str.Length; i++)
                {
                    if (reg.IsMatch(str[i].ToString()))
                    {
                        sb.Append(str[i].ToString());
                    }
                }
                textBox1.Text = sb.ToString();
                textBox1.SelectionStart = textBox1.Text.Length;
            }
            Send("timechanged " + (textBox1.Text.Equals("") ? "0" : textBox1.Text));
        }

        private void button7_Click_1(object sender, EventArgs e)
        {
            Send("noponder");
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            var reg = new Regex("^[0-9]*$");
            var str = textBox2.Text.Trim();
            var sb = new StringBuilder();
            if (!reg.IsMatch(str))
            {
                for (int i = 0; i < str.Length; i++)
                {
                    if (reg.IsMatch(str[i].ToString()))
                    {
                        sb.Append(str[i].ToString());
                    }
                }
                textBox2.Text = sb.ToString();
                textBox2.SelectionStart = textBox2.Text.Length;
            }
            Send("playoutschanged " + (textBox2.Text.Equals("") ? "0" : textBox2.Text));
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
            var reg = new Regex("^[0-9]*$");
            var str = textBox3.Text.Trim();
            var sb = new StringBuilder();
            if (!reg.IsMatch(str))
            {
                for (int i = 0; i < str.Length; i++)
                {
                    if (reg.IsMatch(str[i].ToString()))
                    {
                        sb.Append(str[i].ToString());
                    }
                }
                textBox3.Text = sb.ToString();
                textBox3.SelectionStart = textBox3.Text.Length;
            }
            Send("firstchanged " + (textBox3.Text.Equals("") ? "0" : textBox3.Text));
        }

        private void checkBox1_CheckedChanged_1(object sender, EventArgs e)
        {
            syncBoth = chkBothSync.Checked;
            if (syncBoth)
            {
                Send("bothSync");
                chkAutoPlay.Enabled = true;
                if (Program.showInBoard && type == 0 && !canUseLW)
                    Send("foreFoxWithInBoard");
            }
            else
            {
                Send("nobothSync");
                if (Program.showInBoard && type == 0 && !canUseLW)
                    Send("notForeFoxWithInBoard");
                chkAutoPlay.Enabled = false;
            }
            if (keepSync)
            {
                Send("sync");
                if (radioBlack.Checked)
                {
                    Send("play>black>" + (textBox1.Text.Equals("") ? "0" : textBox1.Text) + " " + (textBox2.Text.Equals("") ? "0" : textBox2.Text) + " " + (textBox3.Text.Equals("") ? "0" : textBox3.Text));
                }
                else if (radioWhite.Checked)
                {
                    Send("play>white>" + (textBox1.Text.Equals("") ? "0" : textBox1.Text) + " " + (textBox2.Text.Equals("") ? "0" : textBox2.Text) + " " + (textBox3.Text.Equals("") ? "0" : textBox3.Text));
                }
            }
            this.saveOtherConfig();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                Process process1 = new Process();
                process1.StartInfo.FileName = getLangStr("helpFile");
                process1.StartInfo.Arguments = "";
                process1.StartInfo.WindowStyle = ProcessWindowStyle.Maximized;
                process1.Start();
            }
            catch (Exception)
            {
                MessageBox.Show(getLangStr("noHelpFile")); //(Program.isChn ? "找不到说明文档,请检查Lizzie目录下[readboard]文件夹内的[readme.rtf]文件是否存在" : "Can not find file,Please check [readme.rtf] file is in the folder [readboard]");
            }
        }

        private void rdoqiantai_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoFore.Checked)
                setManualSelectionMode(TYPE_FOREGROUND);
        }

        private void chkAutoPlay_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutoPlay.Checked)
            {
                radioWhite.Enabled = true;
                radioBlack.Enabled = true;
                textBox1.Enabled = true;
                textBox2.Enabled = true;
                textBox3.Enabled = true;
            }
            else
            {
                radioWhite.Checked = false;
                radioBlack.Checked = false;
                radioWhite.Enabled = false;
                radioBlack.Enabled = false;
                textBox1.Enabled = false;
                textBox2.Enabled = false;
                textBox3.Enabled = false;
                Send("stopAutoPlay");
            }
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {
            SettingsForm form4 = new SettingsForm();
            form4.Show();
        }

        private void radioButton8_CheckedChanged(object sender, EventArgs e)
        {
            if (this.rdoOtherBoard.Checked)
            {
                try
                {
                    this.boardW = int.Parse(txtBoardWidth.Text);
                    this.boardH = int.Parse(txtBoardHeight.Text);
                }
                catch (Exception)
                {
                    // MessageBox.Show(Program.isChn?"错误的棋盘大小":"Wrong goban size!");
                }
            }
            saveOtherConfig();
        }



        private void parseWidth(object sender, EventArgs e)
        {
            try
            {
                if (this.rdoOtherBoard.Checked)
                {
                    this.boardW = int.Parse(txtBoardWidth.Text);
                    saveOtherConfig();
                }
                else
                {
                    int w = int.Parse(txtBoardWidth.Text);
                }
            }
            catch (Exception)
            {
            }
        }

        private void parseHeight(object sender, EventArgs e)
        {
            try
            {
                if (this.rdoOtherBoard.Checked)
                {
                    this.boardH = int.Parse(txtBoardHeight.Text);
                    saveOtherConfig();
                }
                else
                {
                    int h = int.Parse(txtBoardHeight.Text);
                }
            }
            catch (Exception)
            {
            }
        }

        private void tb_KeyPressWidth(object sender, KeyPressEventArgs e)
        {
            if (!(e.KeyChar == '\b' || (e.KeyChar >= '0' && e.KeyChar <= '9')))
            {
                e.Handled = true;
            }
            txtBoardWidth.BackColor = System.Drawing.SystemColors.Menu;
        }

        private void tb_KeyPressHeight(object sender, KeyPressEventArgs e)
        {
            if (!(e.KeyChar == '\b' || (e.KeyChar >= '0' && e.KeyChar <= '9')))
            {
                e.Handled = true;
            }
            txtBoardHeight.BackColor = System.Drawing.SystemColors.Menu;
        }

        private void radioBlack_CheckedChanged(object sender, EventArgs e)
        {
            if (radioBlack.Checked)
            radioWhite.Checked = false;
            if (keepSync)
                if (radioBlack.Checked)
                {
                    Send("play>black>" + (textBox1.Text.Equals("") ? "0" : textBox1.Text) + " " + (textBox2.Text.Equals("") ? "0" : textBox2.Text) + " " + (textBox3.Text.Equals("") ? "0" : textBox3.Text));
                }
                else if (radioWhite.Checked)
                {
                    Send("play>white>" + (textBox1.Text.Equals("") ? "0" : textBox1.Text) + " " + (textBox2.Text.Equals("") ? "0" : textBox2.Text) + " " + (textBox3.Text.Equals("") ? "0" : textBox3.Text));
                }           
        }

        private void radioWhite_CheckedChanged(object sender, EventArgs e)
        {
            if (radioWhite.Checked)
                radioBlack.Checked = false;
            if (keepSync)
                if (radioBlack.Checked)
                {
                    Send("play>black>" + (textBox1.Text.Equals("") ? "0" : textBox1.Text) + " " + (textBox2.Text.Equals("") ? "0" : textBox2.Text) + " " + (textBox3.Text.Equals("") ? "0" : textBox3.Text));
                }
                else if (radioWhite.Checked)
                {
                    Send("play>white>" + (textBox1.Text.Equals("") ? "0" : textBox1.Text) + " " + (textBox2.Text.Equals("") ? "0" : textBox2.Text) + " " + (textBox3.Text.Equals("") ? "0" : textBox3.Text));
                }
        }


        private void button8_Click(object sender, EventArgs e)
        {
            Send("pass");
        }

        private void chkShowInBoard_CheckedChanged(object sender, EventArgs e)
        {
            Program.showInBoard = chkShowInBoard.Checked;
            string result1 = "config_readboard.txt";
            FileStream fs = new FileStream(result1, FileMode.Create);
            StreamWriter wr = null;
            wr = new StreamWriter(fs);
            wr.WriteLine(Program.blackPC + "_" + Program.blackZB + "_" + Program.whitePC + "_" + Program.whiteZB + "_" + (Program.useMag ? "1" : "0") + "_" + (Program.verifyMove ? "1" : "0") + "_" + (Program.showScaleHint ? "1" : "0") + "_" + (Program.showInBoard ? "1" : "0") + "_" + (Program.showInBoardHint ? "1" : "0") + "_" + (Program.autoMin ? "1" : "0") + "_" + Environment.GetEnvironmentVariable("computername").Replace("_", "") + "_" + type);
            wr.Close();
            if (chkShowInBoard.Checked)
            {
                if (Program.showInBoardHint)
                {

                    TipsForm form7 = new TipsForm();
                    form7.ShowDialog();

                }
            }
            else
            {
                Send("notinboard");
            }
        }

        private void button9_Click(object sender, EventArgs e)
        {
            MessageBox.Show(getLangStr("komi65Describe")); 
            //if (!Program.isChn)
            //    MessageBox.Show("Because of lack of move history,captured stone number will be incorrectly,So only area scoring can be used.You can set rules [area scoring + 7.0 komi + hasbutton] to simulate Japanese rule.");
            //else
            //MessageBox.Show("由于同步时无法获取提子数,日本规则(数目)将变得不准确,需要同步日本规则贴6.5目的棋局时可在Katago中使用[数子+贴目7.0+收后方贴还0.5目]规则模拟");

            //else {
            //    try
            //    {
            //        Process process1 = new Process();
            //        process1.StartInfo.FileName = "readboard\\65komi.rtf";
            //        process1.StartInfo.Arguments = "";
            //        process1.StartInfo.WindowStyle = ProcessWindowStyle.Maximized;
            //        process1.Start();
            //    }
            //    catch (Exception)
            //    {
            //        MessageBox.Show(Program.isChn ? "找不到说明文档,请检查Lizzie目录下[readboard]文件夹内的[65komi.rtf]文件是否存在" : "Can not find file,Please check [65komi.rtf] file is in the folder [readboard]");
            //    }
            //}            
        }

        private void button10_Click(object sender, EventArgs e)
        {
            if (!isContinuousSyncing && !startedSync)
            {
                while (threadFastSync != null && threadFastSync.IsAlive)
                {
                    isContinuousSyncing = false;
                    Thread.Sleep(50);
                }
                isContinuousSyncing = true;
                this.btnClickBoard.Enabled = false;
                this.btnOneTimeSync.Enabled = false;
                this.btnKeepSync.Enabled = false;
                threadFastSync = new Thread(startContinuous);
                threadFastSync.SetApartmentState(ApartmentState.STA);
                threadFastSync.Start();
                if (Program.autoMin && this.WindowState != FormWindowState.Minimized)
                {
                    minWindow("");
                }
            }
            else
            {
                stopSync();
                this.btnFastSync.Text = getLangStr("fastSync");  //Program.isChn ? "一键同步" : "FastSync";
                isContinuousSyncing = false;
                Action2<String> a = new Action2<String>(Action2Test);
                Invoke(a, getLangStr("keepSync") +"("+Program.timename + "ms)");
            }
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            isMannulCircle = false;
            //Form5 form5 = new Form5();
            //object x;
            //object y;
            //dm.GetCursorPos(out x, out y);

            //form5.setPic((int)x, (int)y);
            //form5.Show();
            selectBoard();
        }

        private void button11_Click(object sender, EventArgs e)
        {
            isMannulCircle = true;
            selectBoard();
        }

        private Boolean reCalculateRow1(int x, int y, int x2, int y2)
        {
            if (x2 > x && y2 > y)
            {
                int width = x2 - x;
                int height = y2 - y;
                Program.bitmap = new Bitmap(width, height);
                using (System.Drawing.Graphics graphics = Graphics.FromImage(Program.bitmap))
                {
                    graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
                    //  bitmap.Save("screen.bmp");
                }
                try
                {
                    boardLineAjust(boardW, boardH);
                }
                catch (Exception e)
                {
                    SendError(e.ToString());
                    return false;
                }
                //  dm2.Capture(x, y, x2, y2, "screen.bmp");
                return true;
            }
            else
            {
                //   MessageBox.Show(Program.isChn ? "未选择棋盘" : "No board has been choosen");
                return false;
            }
        }

        private void selectBoard()
        {
            mh.Enabled = true;
            this.WindowState = FormWindowState.Minimized;
            form2 = new Form2(isMannulCircle);
            form2.ShowDialog();
        }

        private List<verticalLine> verticalLines;
        private List<horizonLine> horizonLines;

        private void boardLineAjust(int boardWidth1, int boardHeight1)
        {
            verticalLines = new List<verticalLine>();
            horizonLines = new List<horizonLine>();
            Mat srcImage = OpenCvSharp.Extensions.BitmapConverter.ToMat(Program.bitmap);
            //Mat src_gray = new Mat();
            //   Mat copy = new Mat();
            //  Mat copy2 = new Mat();
            //if (showDebugImage)
            //{
            //    srcImage.CopyTo(copy);
            //    srcImage.CopyTo(copy2);
            //Cv2.CvtColor(srcImage, srcImage, ColorConversionCodes.BGR2HSV);  // 色彩空间转换为hsv，便于分离
            //Scalar lowerb = new Scalar(0, 0,0);
            //Scalar upperb = new Scalar(50, 50, 50);
            //Cv2.InRange(srcImage, lowerb, upperb, src_gray);
            //Cv2.ImShow("黑色", src_gray);
            double lowCanny = 200; double upCanny = 550;

            LineSegmentPoint[] linesPoint = FindLines(srcImage, boardWidth1, boardHeight1, (int)lowCanny, (int)upCanny);
            CalculateLines(linesPoint);
            int lines = verticalLines.Count + horizonLines.Count;
            int maxTimes = 10;
            while (lines < 10 && maxTimes > 0)
            {
                lowCanny = lowCanny * 0.9;
                upCanny = upCanny * 0.9;
                linesPoint = FindLines(srcImage, boardWidth1, boardHeight1, (int)lowCanny, (int)upCanny);
                CalculateLines(linesPoint);
                lines = verticalLines.Count + horizonLines.Count;
                maxTimes--;
            }

            //if (showDebugImage)
            //{
            //    for (int i = 0; i < horizonLines.Count; i++)
            //    {
            //        Cv2.Line(copy, horizonLines[i].startPoint, horizonLines[i].endPoint, new Scalar(0, 255, 0), 1);
            //    }
            //    for (int i = 0; i < verticalLines.Count; i++)
            //    {
            //        Cv2.Line(copy, verticalLines[i].startPoint, verticalLines[i].endPoint, new Scalar(0, 255, 0), 1);
            //    }
            //    Cv2.ImShow("line", copy);
            //}
            List<int> verticalGaps = new List<int>();
            for (int s = 0; s < verticalLines.Count - 1; s++)
            {
                verticalLine line = verticalLines[s];
                verticalLine line2 = verticalLines[s + 1];
                int gap = line2.position - line.position;
                verticalGaps.Add(gap);
            }

            List<gapCount> verticalGapCounts = new List<gapCount>();
            for (int s = 0; s < verticalGaps.Count; s++)
            {
                int gap = verticalGaps[s];
                Boolean isNew = true;
                for (int n = 0; n < verticalGapCounts.Count; n++)
                {
                    gapCount gapCount = verticalGapCounts[n];
                    if (gap == gapCount.gap)
                    {
                        isNew = false;
                    }
                }
                if (isNew)
                {
                    gapCount gapCount = new gapCount();
                    gapCount.gap = gap;
                    verticalGapCounts.Add(gapCount);
                }
            }
            for (int s = 0; s < verticalGaps.Count; s++)
            {
                int gap = verticalGaps[s];
                for (int m = 0; m < verticalGapCounts.Count; m++)
                {
                    gapCount gapCount = verticalGapCounts[m];
                    if (gap == gapCount.gap)
                    {
                        gapCount.looseCounts++;
                        gapCount.uniqueCounts++;
                    }
                    else if (Math.Abs(gap - gapCount.gap) <= 3)
                    {
                        gapCount.looseCounts++;
                    }
                }
            }

            verticalGapCounts.Sort(delegate (gapCount obj1, gapCount obj2) {
                int res = 0;
                if ((null == obj1) && (null == obj2)) return 0;
                else if ((null != obj1) && (null == obj2)) return 1;
                else if ((null == obj1) && (null != obj2)) return -1;

                if (obj1.uniqueCounts > obj2.uniqueCounts) res = -1;
                else if (obj1.uniqueCounts == obj2.uniqueCounts)
                {
                    if (obj1.looseCounts > obj2.looseCounts) res = -1;
                    else res = 1;
                }
                else res = 1;
                return res;
            });

            List<int> horizonGaps = new List<int>();
            for (int s = 0; s < horizonLines.Count - 1; s++)
            {
                horizonLine line = horizonLines[s];
                horizonLine line2 = horizonLines[s + 1];
                int gap = line2.position - line.position;
                horizonGaps.Add(gap);
            }

            List<gapCount> horizonGapCounts = new List<gapCount>();
            for (int s = 0; s < horizonGaps.Count; s++)
            {
                int gap = horizonGaps[s];
                Boolean isNew = true;
                for (int n = 0; n < horizonGapCounts.Count; n++)
                {
                    gapCount gapCount = horizonGapCounts[n];
                    if (gap == gapCount.gap)
                    {
                        isNew = false;
                    }
                }
                if (isNew)
                {
                    gapCount gapCount = new gapCount();
                    gapCount.gap = gap;
                    horizonGapCounts.Add(gapCount);
                }
            }
            for (int s = 0; s < horizonGaps.Count; s++)
            {
                int gap = horizonGaps[s];
                for (int m = 0; m < horizonGapCounts.Count; m++)
                {
                    gapCount gapCount = horizonGapCounts[m];
                    if (gap == gapCount.gap)
                    {
                        gapCount.looseCounts++;
                        gapCount.uniqueCounts++;
                    }
                    else if (Math.Abs(gap - gapCount.gap) <= 3)
                    {
                        gapCount.looseCounts++;
                    }
                }
            }

            horizonGapCounts.Sort(delegate (gapCount obj1, gapCount obj2) {
                int res = 0;
                if ((null == obj1) && (null == obj2)) return 0;
                else if ((null != obj1) && (null == obj2)) return 1;
                else if ((null == obj1) && (null != obj2)) return -1;

                if (obj1.uniqueCounts > obj2.uniqueCounts) res = -1;
                else if (obj1.uniqueCounts == obj2.uniqueCounts)
                {
                    if (obj1.looseCounts > obj2.looseCounts) res = -1;
                    else res = 1;
                }
                else res = 1;
                return res;
            });

            int verticalGap = verticalGapCounts[0].gap;
            int horizonGap = horizonGapCounts[0].gap;

            if (verticalGapCounts.Count >= 3)
            {
                if ((Math.Abs(verticalGapCounts[0].gap - verticalGapCounts[1].gap) <= 2) && (Math.Abs(verticalGapCounts[0].gap - verticalGapCounts[2].gap) <= 2))
                    if (((verticalGapCounts[1].uniqueCounts / (float)verticalGapCounts[0].uniqueCounts) > 0.5) && ((verticalGapCounts[2].uniqueCounts / (float)verticalGapCounts[0].uniqueCounts) > 0.5))
                    {
                        int gap0 = verticalGapCounts[0].gap;
                        int gap1 = verticalGapCounts[1].gap;
                        int gap2 = verticalGapCounts[2].gap;
                        if ((gap0 > gap1 && gap1 > gap2) || (gap2 > gap1 && gap1 > gap0))
                        {
                            verticalGap = gap1;
                        }
                        else if ((gap1 > gap0 && gap0 > gap2) || (gap2 > gap0 && gap0 > gap1))
                        {
                            verticalGap = gap0;
                        }
                        else
                        {
                            verticalGap = gap2;
                        }
                    }
            }

            if (horizonGapCounts.Count >= 3)
            {
                if ((Math.Abs(horizonGapCounts[0].gap - horizonGapCounts[1].gap) <= 2) && (Math.Abs(horizonGapCounts[0].gap - horizonGapCounts[2].gap) <= 2))
                    if (((horizonGapCounts[1].uniqueCounts / (float)horizonGapCounts[0].uniqueCounts) > 0.5) && ((horizonGapCounts[2].uniqueCounts / (float)horizonGapCounts[0].uniqueCounts) > 0.5))
                    {
                        int gap0 = horizonGapCounts[0].gap;
                        int gap1 = horizonGapCounts[1].gap;
                        int gap2 = horizonGapCounts[2].gap;
                        if ((gap0 > gap1 && gap1 > gap2) || (gap2 > gap1 && gap1 > gap0))
                        {
                            horizonGap = gap1;
                        }
                        else if ((gap1 > gap0 && gap0 > gap2) || (gap2 > gap0 && gap0 > gap1))
                        {
                            horizonGap = gap0;
                        }
                        else
                        {
                            horizonGap = gap2;
                        }
                    }
            }
            //    Console.WriteLine("V gap: " + verticalGap);
            //      Console.WriteLine("H gap: " + horizonGap);
            for (int n = 0; n < verticalLines.Count; n++)
            {
                verticalLine line1 = verticalLines[n];
                for (int m = n + 1; m < verticalLines.Count; m++)
                {
                    verticalLine line2 = verticalLines[m];
                    if (Math.Abs(line2.position - line1.position - verticalGap) <= Math.Max(1, verticalGap / 8))
                    {
                        line1.validate = true;
                        line2.validate = true;
                    }
                }
            }

            for (int n = 0; n < horizonLines.Count; n++)
            {
                horizonLine line1 = horizonLines[n];
                for (int m = n + 1; m < horizonLines.Count; m++)
                {
                    horizonLine line2 = horizonLines[m];
                    if (Math.Abs(line2.position - line1.position - horizonGap) <= Math.Max(1, horizonGap / 8))
                    {
                        line1.validate = true;
                        line2.validate = true;
                    }
                }
            }


            int boardW = verticalGap * (boardWidth1 - 1);
            int boardH = horizonGap * (boardHeight1 - 1);

            List<wholeOffset> verticalOffsets = new List<wholeOffset>();
            for (int s = 0; s < verticalLines.Count; s++)
            {
                verticalLine line = verticalLines[s];
                for (int n = s + 1; n < verticalLines.Count; n++)
                {
                    verticalLine line2 = verticalLines[n];
                    wholeOffset vOffset = new wholeOffset();
                    vOffset.minPos = line.position;
                    vOffset.maxPos = line2.position;
                    vOffset.offset = Math.Abs(line2.position - line.position - boardW);
                    //  Console.WriteLine(bais);   
                    verticalOffsets.Add(vOffset);
                }
            }
            verticalOffsets.Sort(delegate (wholeOffset a, wholeOffset b) {
                return a.offset.CompareTo(b.offset);
            });

            while (verticalOffsets.Count >= 2 && Math.Abs(verticalOffsets[0].offset - verticalOffsets[1].offset) <= verticalGap / 3)
            {
                int v0Min = verticalOffsets[0].minPos + verticalGap / 2;
                int v0MinCounts = 0;
                int v0Max = verticalOffsets[0].maxPos - verticalGap / 2;
                int v0MaxCounts = 0;

                int v1Min = verticalOffsets[1].minPos + verticalGap / 2;
                int v1MinCounts = 0;
                int v1Max = verticalOffsets[1].maxPos - verticalGap / 2;
                int v1MaxCounts = 0;

                for (int s = 0; s < horizonLines.Count; s++)
                {
                    horizonLine line = horizonLines[s];
                    if (!line.validate)
                        continue;
                    if (line.startPoint.X < v0Min && line.endPoint.X > v0Min)
                        v0MinCounts++;
                    if (line.endPoint.X > v0Max && line.startPoint.X < v0Max)
                        v0MaxCounts++;
                    if (line.startPoint.X < v1Min && line.endPoint.X > v1Min)
                        v1MinCounts++;
                    if (line.endPoint.X > v1Max && line.startPoint.X < v1Max)
                        v1MaxCounts++;
                }

                int v0MinOutLinesCounts = Math.Min(v0MinCounts, v0MaxCounts);
                int v1MinOutLinesCounts = Math.Min(v1MinCounts, v1MaxCounts);
                if (v0MinOutLinesCounts < v1MinOutLinesCounts)
                    verticalOffsets.RemoveAt(0);
                else
                    verticalOffsets.RemoveAt(1);
            }

            List<wholeOffset> horizonOffsets = new List<wholeOffset>();
            for (int s = 0; s < horizonLines.Count; s++)
            {
                horizonLine line = horizonLines[s];
                for (int n = s + 1; n < horizonLines.Count; n++)
                {
                    horizonLine line2 = horizonLines[n];
                    wholeOffset hOffset = new wholeOffset();
                    hOffset.minPos = line.position;
                    hOffset.maxPos = line2.position;
                    hOffset.offset = Math.Abs(line2.position - line.position - boardH);
                    //  Console.WriteLine(bais);   
                    horizonOffsets.Add(hOffset);
                }
            }
            horizonOffsets.Sort(delegate (wholeOffset a, wholeOffset b) {
                return a.offset.CompareTo(b.offset);
            });

            while (horizonOffsets.Count >= 2 && Math.Abs(horizonOffsets[0].offset - horizonOffsets[1].offset) <= horizonGap / 3)
            {
                int h0Min = horizonOffsets[0].minPos + horizonGap / 2;
                int h0MinCounts = 0;
                int h0Max = horizonOffsets[0].maxPos - horizonGap / 2;
                int h0MaxCounts = 0;

                int h1Min = horizonOffsets[1].minPos + horizonGap / 2;
                int h1MinCounts = 0;
                int h1Max = horizonOffsets[1].maxPos - horizonGap / 2;
                int h1MaxCounts = 0;

                for (int s = 0; s < verticalLines.Count; s++)
                {
                    verticalLine line = verticalLines[s];
                    if (!line.validate)
                        continue;
                    if (line.startPoint.Y < h0Min && line.endPoint.Y > h0Min)
                        h0MinCounts++;
                    if (line.endPoint.Y > h0Max && line.startPoint.Y < h0Max)
                        h0MaxCounts++;
                    if (line.startPoint.Y < h1Min && line.endPoint.Y > h1Min)
                        h1MinCounts++;
                    if (line.endPoint.Y > h1Max && line.startPoint.Y < h0Max)
                        h1MaxCounts++;
                }

                int h0MinOutLinesCounts = Math.Min(h0MinCounts, h0MaxCounts);
                int h1MinOutLinesCounts = Math.Min(h1MinCounts, h1MaxCounts);
                if (h0MinOutLinesCounts < h1MinOutLinesCounts)
                    horizonOffsets.RemoveAt(0);
                else
                    horizonOffsets.RemoveAt(1);
            }

            int leftPos = verticalOffsets[0].minPos;
            int rightPos = verticalOffsets[0].maxPos;
            int topPos = horizonOffsets[0].minPos;
            int bottomPos = horizonOffsets[0].maxPos;

            int vGap = (int)Math.Ceiling(verticalGap / 2f);
            int hGap = (int)Math.Ceiling(horizonGap / 2f);
            ox2 = ox1 + rightPos + hGap;
            oy2 = oy1 + bottomPos + vGap;

            ox1 = ox1 + leftPos - hGap;
            oy1 = oy1 + topPos - vGap;
            //  Cv2.Canny(srcImage, contours, 100, 250);
            //Rect roi = new Rect(leftPos - vGap, topPos - vGap, rightPos-leftPos + hGap*2, bottomPos- topPos + vGap * 2);//首先要用个rect确定我们的兴趣区域在哪
            //Mat ImageROI = new Mat(contours, roi);
            //Mat ImageShow = new Mat(srcImage, roi);
            //CircleSegment[] circles = Cv2.HoughCircles(ImageROI, HoughMethods.Gradient,1,550);
            //foreach (CircleSegment circle in circles)
            //{
            //    // Point2f center = circle.Center;//圆心
            //    int radius = (int)circle.Radius;//半径

            //    ImageShow.Circle((int)circle.Center.X, (int)circle.Center.Y, radius, new Scalar(0, 255, 0), 1);
            //}
            //Cv2.ImShow("兴趣区域", ImageShow);

            //if (showDebugImage)
            //{
            //    Cv2.Line(copy2, new Point(leftPos, topPos), new Point(rightPos, topPos), new Scalar(0, 255, 0), 1);
            //    Cv2.Line(copy2, new Point(leftPos, bottomPos), new Point(rightPos, bottomPos), new Scalar(0, 255, 0), 1);
            //    Cv2.Line(copy2, new Point(leftPos, topPos), new Point(leftPos, bottomPos), new Scalar(0, 255, 0), 1);
            //    Cv2.Line(copy2, new Point(rightPos, topPos), new Point(rightPos, bottomPos), new Scalar(0, 255, 0), 1);
            //    Cv2.ImShow("1路线", copy2);
            //}
            //for (int s = 0; s < verticalLines.Count; s++)
            //{
            //    verticalLine line = verticalLines[s];
            //    Cv2.Line(copy, line.startPoint, line.endPoint, new Scalar(0, 255, 0), 1);
            //   // Console.WriteLine(line.startPoint.X);
            //}

            //for (int s = 0; s < horizonLines.Count; s++)
            //{
            //    horizonLine line = horizonLines[s];
            //    Cv2.Line(copy, line.startPoint, line.endPoint, new Scalar(0, 255, 0), 1);
            //}
        }

        private void CalculateLines(LineSegmentPoint[] linesPoint)
        {
            for (int i = 0; i < linesPoint.Length; i++)
            {
                int x1 = linesPoint[i].P1.X;
                int x2 = linesPoint[i].P2.X;

                int y1 = linesPoint[i].P1.Y;
                int y2 = linesPoint[i].P2.Y;
                if (Math.Abs(x1 - x2) >= 2 && Math.Abs(y1 - y2) >= 2)
                    continue;

                if (Math.Abs(y1 - y2) >= 2)
                {
                    verticalLine newLine = new verticalLine();
                    newLine.position = (x1 + x2) / 2;
                    newLine.startPoint = new Point(newLine.position, Math.Min(y1, y2));
                    newLine.endPoint = new Point(newLine.position, Math.Max(y1, y2));
                    verticalLines.Add(newLine);
                }
                else
                {
                    horizonLine newLine = new horizonLine();
                    newLine.position = (y1 + y2) / 2;
                    newLine.startPoint = new Point(Math.Min(x1, x2), newLine.position);
                    newLine.endPoint = new Point(Math.Max(x1, x2), newLine.position);
                    horizonLines.Add(newLine);
                }
                // Cv2.Line(copy, linesPoint[i].P1, linesPoint[i].P2, new Scalar(0, 255, 0), 1);
            }
            verticalLines.Sort(delegate (verticalLine a, verticalLine b) {
                return a.position.CompareTo(b.position);
            });
            for (int s = 0; s < verticalLines.Count / 2; s++)
            {
                verticalLine line = verticalLines[s];
                verticalLine line2 = verticalLines[s + 1];
                if (Math.Abs(line.position - line2.position) <= 4)
                {
                    line2.position = (line.position + line2.position) / 2;
                    line2.startPoint.X = line2.position;
                    line2.startPoint.Y = Math.Min(line.startPoint.Y, line2.startPoint.Y);
                    line2.endPoint.X = line2.position;
                    line2.endPoint.Y = Math.Max(line.endPoint.Y, line2.endPoint.Y);
                    line.needDelete = true;
                }
            }

            for (int s = verticalLines.Count; s > verticalLines.Count / 2; s--)
            {
                verticalLine line = verticalLines[s - 1];
                verticalLine line2 = verticalLines[s - 2];
                if (line2.needDelete)
                    continue;
                if (Math.Abs(line.position - line2.position) <= 4)
                {
                    line2.position = (line.position + line2.position) / 2;
                    line2.startPoint.X = line2.position;
                    line2.startPoint.Y = Math.Min(line.startPoint.Y, line2.startPoint.Y);
                    line2.endPoint.X = line2.position;
                    line2.endPoint.Y = Math.Max(line.endPoint.Y, line2.endPoint.Y);
                    line.needDelete = true;
                }
            }
            for (int s = 0; s < verticalLines.Count; s++)
            {
                if (verticalLines[s].needDelete)
                {
                    verticalLines.RemoveAt(s);
                    s--;
                }
            }

            horizonLines.Sort(delegate (horizonLine a, horizonLine b) {
                return a.position.CompareTo(b.position);
            });
            for (int s = 0; s < horizonLines.Count / 2; s++)
            {
                horizonLine line = horizonLines[s];
                horizonLine line2 = horizonLines[s + 1];
                if (Math.Abs(line.position - line2.position) <= 4)
                {
                    line2.position = (line.position + line2.position) / 2;
                    line2.startPoint.Y = line2.position;
                    line2.startPoint.X = Math.Min(line.startPoint.X, line2.startPoint.X);
                    line2.endPoint.Y = line2.position;
                    line2.endPoint.X = Math.Max(line.endPoint.X, line2.endPoint.X);
                    line.needDelete = true;
                }
            }
            for (int s = horizonLines.Count; s > horizonLines.Count / 2; s--)
            {
                horizonLine line = horizonLines[s - 1];
                horizonLine line2 = horizonLines[s - 2];
                if (line2.needDelete)
                    continue;
                if (Math.Abs(line.position - line2.position) <= 4)
                {
                    line2.position = (line.position + line2.position) / 2;
                    line2.startPoint.Y = line2.position;
                    line2.startPoint.X = Math.Min(line.startPoint.X, line2.startPoint.X);
                    line2.endPoint.Y = line2.position;
                    line2.endPoint.X = Math.Max(line.endPoint.X, line2.endPoint.X);
                    line.needDelete = true;
                }
            }
            for (int s = 0; s < horizonLines.Count; s++)
            {
                if (horizonLines[s].needDelete)
                {
                    horizonLines.RemoveAt(s);
                    s--;
                }
            }
        }

        private LineSegmentPoint[] FindLines(Mat srcImage, int boardWidth1, int boardHeight1, int lowCanny, int upCanny)
        {
            Mat contours = new Mat();
            Cv2.Canny(srcImage, contours, lowCanny, upCanny);
            int length = Math.Min(srcImage.Width, srcImage.Height);
            //  Cv2.ImShow("轮廓", contours);         
            int size = Math.Min(boardWidth1, boardHeight1);
            LineSegmentPoint[] linesPoint = Cv2.HoughLinesP(contours, 1, Cv2.PI / 180, (int)(length / (((size + 1) * 1.5f))), (int)(length / (((size + 1) * 1.5f))), 1);
            //  Console.WriteLine(linesPoint.Length);
            return linesPoint;
        }

        private Boolean getTygemPos(IntPtr hwnd, out RgbInfo[] rgbArray, out int totalWidth)
        {
            Bitmap input = GetWindowImage(hwnd, isFirstGetPos);
            if (input == null || input.Width <= boardW || input.Height <= boardH)
            {
                rgbArray = null;
                totalWidth = -1;
                return false;
            }
            Rectangle rect2 = new Rectangle(0, 0, input.Width, input.Height);
            const int PixelWidth = 3;
            const PixelFormat PixelFormat = PixelFormat.Format24bppRgb;
            BitmapData data = input.LockBits(rect2, ImageLockMode.ReadOnly, PixelFormat);
            rgbArray = new RgbInfo[data.Height * data.Width];
            totalWidth = data.Width;
            byte[] pixelData = new Byte[data.Stride];
            for (int scanline = 0; scanline < data.Height; scanline++)
            {
                Marshal.Copy(data.Scan0 + (scanline * data.Stride), pixelData, 0, data.Stride);
                for (int pixeloffset = 0; pixeloffset < data.Width; pixeloffset++)
                {
                    // PixelFormat.Format32bppRgb means the data is stored
                    // in memory as BGR. We want RGB, so we must do some 
                    // bit-shuffling.
                    rgbArray[scanline * data.Width + pixeloffset] = new RgbInfo();
                    rgbArray[scanline * data.Width + pixeloffset].r = pixelData[pixeloffset * PixelWidth + 2];
                    rgbArray[scanline * data.Width + pixeloffset].g = pixelData[pixeloffset * PixelWidth + 1];
                    rgbArray[scanline * data.Width + pixeloffset].b = pixelData[pixeloffset * PixelWidth];
                }
            }
            int width = data.Width;
            int height = data.Height;
            input.UnlockBits(data);
            input.Dispose();
            Boolean found = false;
            try
            {
                RgbInfo cl = rgbArray[0];
                if (cl.r > 233 && cl.g > 203 && cl.b > 120 && cl.b < 180)
                {
                    found = true;
                    if (cansetFirstGetPos)
                        isFirstGetPos = false;
                }
            }
            catch (Exception)
            {
            }
            return found;
        }
        private Boolean getSinaPos(IntPtr hwnd, out RgbInfo[] rgbArray, out int totalWidth)
        {
            Bitmap input = GetWindowImage(hwnd, isFirstGetPos);
            if (input == null || input.Width <= boardW || input.Height <= boardH)
            {
                rgbArray = null;
                totalWidth = -1;
                return false;
            }
            Rectangle rect2 = new Rectangle(0, 0, input.Width, input.Height);
            const int PixelWidth = 3;
            const PixelFormat PixelFormat = PixelFormat.Format24bppRgb;
            BitmapData data = input.LockBits(rect2, ImageLockMode.ReadOnly, PixelFormat);
            rgbArray = new RgbInfo[data.Height * data.Width];
            totalWidth = data.Width;
            byte[] pixelData = new Byte[data.Stride];
            for (int scanline = 0; scanline < data.Height; scanline++)
            {
                Marshal.Copy(data.Scan0 + (scanline * data.Stride), pixelData, 0, data.Stride);
                for (int pixeloffset = 0; pixeloffset < data.Width; pixeloffset++)
                {
                    // PixelFormat.Format32bppRgb means the data is stored
                    // in memory as BGR. We want RGB, so we must do some 
                    // bit-shuffling.
                    rgbArray[scanline * data.Width + pixeloffset] = new RgbInfo();
                    rgbArray[scanline * data.Width + pixeloffset].r = pixelData[pixeloffset * PixelWidth + 2];
                    rgbArray[scanline * data.Width + pixeloffset].g = pixelData[pixeloffset * PixelWidth + 1];
                    rgbArray[scanline * data.Width + pixeloffset].b = pixelData[pixeloffset * PixelWidth];
                }
            }
            int width = data.Width;
            int height = data.Height;
            input.UnlockBits(data);
            input.Dispose();
            //251,218,162 左到右 上到下 251,218,162 向下1,2 偏移 < 5(左上角)(实际找到正数第二的点sx1 - 1, sy1 - 1)
            //251,218,162 左到右 下到上251,218,162 向下1,2 偏移 < 5(左下角)(实际找到倒数第三的点, height + 4)
            // "FBDAA2-050505", "0|1|FBDAA2-050505,0|2|FBDAA2-050505", 1.0, 0, out qx1, out qy1);      
            // "FBDAA2-050505", "0|1|FBDAA2-050505,0|2|FBDAA2-050505", 1.0, 1, out qx4, out qy4);
            List<ColorInfo> colorInfos = new List<ColorInfo>();
            ColorInfo colorInfo1 = new ColorInfo();
            colorInfo1.r = 251;
            colorInfo1.g = 218;
            colorInfo1.b = 162;
            colorInfo1.rOffset = 5;
            colorInfo1.gOffset = 5;
            colorInfo1.bOffset = 5;
            colorInfos.Add(colorInfo1);
            ColorInfo colorInfo2 = new ColorInfo();
            colorInfo2.r = 251;
            colorInfo2.g = 218;
            colorInfo2.b = 162;
            colorInfo2.rOffset = 5;
            colorInfo2.gOffset = 5;
            colorInfo2.bOffset = 5;
            colorInfo2.xOffset = 0;
            colorInfo2.yOffset = 1;
            colorInfos.Add(colorInfo2);
            ColorInfo colorInfo3 = new ColorInfo();
            colorInfo3.r = 251;
            colorInfo3.g = 218;
            colorInfo3.b = 162;
            colorInfo3.rOffset = 5;
            colorInfo3.gOffset = 5;
            colorInfo3.bOffset = 5;
            colorInfo3.xOffset = 0;
            colorInfo3.yOffset = 2;
            colorInfos.Add(colorInfo3);

            Point upLeft = getMultiColorPos(rgbArray, width, height, colorInfos, 0);
            Point downLeft = getMultiColorPos(rgbArray, width, height, colorInfos, 2);
            if (upLeft.X < 0 || upLeft.Y < 0 || downLeft.X < 0 || downLeft.Y <= 0)
                return false;
            sx1 = upLeft.X - 1;
            sy1 = upLeft.Y - 1;
            this.height = downLeft.Y - upLeft.Y + 4;
            this.width = this.height;
            if (cansetFirstGetPos)
                isFirstGetPos = false;
            return true;
        }

        private Boolean getFoxPos(IntPtr hwnd, out RgbInfo[] rgbArray, out int totalWidth)
        {
            Bitmap input = GetWindowImage(hwnd, isFirstGetPos);
            if (input == null || input.Width <= boardW || input.Height <= boardH)
            {
                rgbArray = null;
                totalWidth = -1;
                return false;
            }
            Rectangle rect2 = new Rectangle(0, 0, input.Width, input.Height);
            const int PixelWidth = 3;
            const PixelFormat PixelFormat = PixelFormat.Format24bppRgb;
            BitmapData data = input.LockBits(rect2, ImageLockMode.ReadOnly, PixelFormat);
            rgbArray = new RgbInfo[data.Height * data.Width];
            totalWidth = data.Width;
            byte[] pixelData = new Byte[data.Stride];
            for (int scanline = 0; scanline < data.Height; scanline++)
            {
                Marshal.Copy(data.Scan0 + (scanline * data.Stride), pixelData, 0, data.Stride);
                for (int pixeloffset = 0; pixeloffset < data.Width; pixeloffset++)
                {
                    // PixelFormat.Format32bppRgb means the data is stored
                    // in memory as BGR. We want RGB, so we must do some 
                    // bit-shuffling.
                    rgbArray[scanline * data.Width + pixeloffset] = new RgbInfo();
                    rgbArray[scanline * data.Width + pixeloffset].r = pixelData[pixeloffset * PixelWidth + 2];
                    rgbArray[scanline * data.Width + pixeloffset].g = pixelData[pixeloffset * PixelWidth + 1];
                    rgbArray[scanline * data.Width + pixeloffset].b = pixelData[pixeloffset * PixelWidth];
                }
            }
            int width = data.Width;
            int height = data.Height;
            input.UnlockBits(data);
            input.Dispose();
            //49,49,49 左到右 上到下 46,46,46 向下1,2 向右1,2 偏移 < 5(左上角)

            //  "313131-050505", "2|1|-2E2E2E-050505,1|1|-2E2E2E-050505,1|2|-2E2E2E-050505", 1.0, 0, out qx4, out qy4);


            List<ColorInfo> colorInfos = new List<ColorInfo>();
            ColorInfo colorInfo1 = new ColorInfo();
            colorInfo1.r = 49;
            colorInfo1.g = 49;
            colorInfo1.b = 49;
            colorInfo1.rOffset = 5;
            colorInfo1.gOffset = 5;
            colorInfo1.bOffset = 5;
            colorInfos.Add(colorInfo1);
            ColorInfo colorInfo2 = new ColorInfo();
            colorInfo2.r = 46;
            colorInfo2.g = 46;
            colorInfo2.b = 46;
            colorInfo2.rOffset = 5;
            colorInfo2.gOffset = 5;
            colorInfo2.bOffset = 5;
            colorInfo2.xOffset = 0;
            colorInfo2.yOffset = 1;
            colorInfos.Add(colorInfo2);
            ColorInfo colorInfo3 = new ColorInfo();
            colorInfo3.r = 46;
            colorInfo3.g = 46;
            colorInfo3.b = 46;
            colorInfo3.rOffset = 5;
            colorInfo3.gOffset = 5;
            colorInfo3.bOffset = 5;
            colorInfo3.xOffset = 0;
            colorInfo3.yOffset = 2;
            colorInfos.Add(colorInfo3);
            ColorInfo colorInfo4 = new ColorInfo();
            colorInfo4.r = 46;
            colorInfo4.g = 46;
            colorInfo4.b = 46;
            colorInfo4.rOffset = 5;
            colorInfo4.gOffset = 5;
            colorInfo4.bOffset = 5;
            colorInfo4.xOffset = 1;
            colorInfo4.yOffset = 0;
            colorInfos.Add(colorInfo4);
            ColorInfo colorInfo5 = new ColorInfo();
            colorInfo5.r = 46;
            colorInfo5.g = 46;
            colorInfo5.b = 46;
            colorInfo5.rOffset = 5;
            colorInfo5.gOffset = 5;
            colorInfo5.bOffset = 5;
            colorInfo5.xOffset = 2;
            colorInfo5.yOffset = 0;
            colorInfos.Add(colorInfo5);
            ColorInfo colorInfo6 = new ColorInfo();
            colorInfo6.r = 46;
            colorInfo6.g = 46;
            colorInfo6.b = 46;
            colorInfo6.rOffset = 5;
            colorInfo6.gOffset = 5;
            colorInfo6.bOffset = 5;
            colorInfo6.xOffset = 2;
            colorInfo6.yOffset = 1;
            colorInfo6.isReverse = true;
            colorInfos.Add(colorInfo6);
            ColorInfo colorInfo7 = new ColorInfo();
            colorInfo7.r = 46;
            colorInfo7.g = 46;
            colorInfo7.b = 46;
            colorInfo7.rOffset = 5;
            colorInfo7.gOffset = 5;
            colorInfo7.bOffset = 5;
            colorInfo7.xOffset = 1;
            colorInfo7.yOffset = 1;
            colorInfo7.isReverse = true;
            colorInfos.Add(colorInfo7);
            ColorInfo colorInfo8 = new ColorInfo();
            colorInfo8.r = 46;
            colorInfo8.g = 46;
            colorInfo8.b = 46;
            colorInfo8.rOffset = 5;
            colorInfo8.gOffset = 5;
            colorInfo8.bOffset = 5;
            colorInfo8.xOffset = 1;
            colorInfo8.yOffset = 2;
            colorInfo8.isReverse = true;
            colorInfos.Add(colorInfo8);
            // "313131-050505", "-2|1|-2E2E2E-050505,-1|1|-2E2E2E-050505,-1|2|-2E2E2E-050505", 1.0, 2, out qx1, out qy1);
            //49,49,49  右到左 上到下 46,46,46 向下1,2 向左1,2 偏移 < 5(右上角)
            List<ColorInfo> colorInfos2 = new List<ColorInfo>();
            ColorInfo colorInfo21 = new ColorInfo();
            colorInfo21.r = 49;
            colorInfo21.g = 49;
            colorInfo21.b = 49;
            colorInfo21.rOffset = 5;
            colorInfo21.gOffset = 5;
            colorInfo21.bOffset = 5;
            colorInfos2.Add(colorInfo21);
            ColorInfo colorInfo22 = new ColorInfo();
            colorInfo22.r = 46;
            colorInfo22.g = 46;
            colorInfo22.b = 46;
            colorInfo22.rOffset = 5;
            colorInfo22.gOffset = 5;
            colorInfo22.bOffset = 5;
            colorInfo22.xOffset = 0;
            colorInfo22.yOffset = 1;
            colorInfos2.Add(colorInfo22);
            ColorInfo colorInfo23 = new ColorInfo();
            colorInfo23.r = 46;
            colorInfo23.g = 46;
            colorInfo23.b = 46;
            colorInfo23.rOffset = 5;
            colorInfo23.gOffset = 5;
            colorInfo23.bOffset = 5;
            colorInfo23.xOffset = 0;
            colorInfo23.yOffset = 2;
            colorInfos2.Add(colorInfo23);
            ColorInfo colorInfo24 = new ColorInfo();
            colorInfo24.r = 46;
            colorInfo24.g = 46;
            colorInfo24.b = 46;
            colorInfo24.rOffset = 5;
            colorInfo24.gOffset = 5;
            colorInfo24.bOffset = 5;
            colorInfo24.xOffset = -1;
            colorInfo24.yOffset = 0;
            colorInfos2.Add(colorInfo24);
            ColorInfo colorInfo25 = new ColorInfo();
            colorInfo25.r = 46;
            colorInfo25.g = 46;
            colorInfo25.b = 46;
            colorInfo25.rOffset = 5;
            colorInfo25.gOffset = 5;
            colorInfo25.bOffset = 5;
            colorInfo25.xOffset = -2;
            colorInfo25.yOffset = 0;
            colorInfos2.Add(colorInfo25);
            ColorInfo colorInfo26 = new ColorInfo();
            colorInfo26.r = 46;
            colorInfo26.g = 46;
            colorInfo26.b = 46;
            colorInfo26.rOffset = 5;
            colorInfo26.gOffset = 5;
            colorInfo26.bOffset = 5;
            colorInfo26.xOffset = -2;
            colorInfo26.yOffset = 1;
            colorInfo26.isReverse = true;
            colorInfos2.Add(colorInfo26);
            ColorInfo colorInfo27 = new ColorInfo();
            colorInfo27.r = 46;
            colorInfo27.g = 46;
            colorInfo27.b = 46;
            colorInfo27.rOffset = 5;
            colorInfo27.gOffset = 5;
            colorInfo27.bOffset = 5;
            colorInfo27.xOffset = -1;
            colorInfo27.yOffset = 1;
            colorInfo27.isReverse = true;
            colorInfos2.Add(colorInfo27);
            ColorInfo colorInfo28 = new ColorInfo();
            colorInfo28.r = 46;
            colorInfo28.g = 46;
            colorInfo28.b = 46;
            colorInfo28.rOffset = 5;
            colorInfo28.gOffset = 5;
            colorInfo28.bOffset = 5;
            colorInfo28.xOffset = -1;
            colorInfo28.yOffset = 2;
            colorInfo28.isReverse = true;
            colorInfos2.Add(colorInfo28);

            Point upLeft = getMultiColorPos(rgbArray, width, height, colorInfos, 0);
            Point upRight = getMultiColorPos(rgbArray, width, height, colorInfos2, 1);
            if (upLeft.X < 0 || upLeft.Y < 0 || upRight.X < 0 || upRight.Y <= 0)
                return false;

            sx1 = upLeft.X;
            sy1 = upLeft.Y;
            this.width = upRight.X - upLeft.X;
            this.height = this.width;

            if (cansetFirstGetPos)
                isFirstGetPos = false;
            return true;
        }

        private Point getMultiColorPos(RgbInfo[] rgbArray, int width, int height, List<ColorInfo> colorInfos, int direction)
        {
            Point returnPoint = new Point();
            returnPoint.X = -1;
            returnPoint.Y = -1;
            switch (direction)
            {
                case 0:
                    for (int x = 0; x < width; x++)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            if (getMultiColorPixel(rgbArray, x, y, width, height, colorInfos))
                            {
                                returnPoint.X = x;
                                returnPoint.Y = y;
                                return returnPoint;
                            }
                        }
                    }
                    break;
                case 1:
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = width - 1; x >= 0; x--)
                        {
                            if (getMultiColorPixel(rgbArray, x, y, width, height, colorInfos))
                            {
                                returnPoint.X = x;
                                returnPoint.Y = y;
                                return returnPoint;
                            }
                        }
                    }
                    break;
                case 2:
                    for (int x = 0; x < width; x++)
                    {
                        for (int y = height - 1; y >= 0; y--)
                        {
                            if (getMultiColorPixel(rgbArray, x, y, width, height, colorInfos))
                            {
                                returnPoint.X = x;
                                returnPoint.Y = y;
                                return returnPoint;
                            }
                        }
                    }
                    break;
                case 3:
                    for (int x = width - 1; x >= 0; x--)
                    {
                        for (int y = height - 1; y >= 0; y--)
                        {
                            if (getMultiColorPixel(rgbArray, x, y, width, height, colorInfos))
                            {
                                returnPoint.X = x;
                                returnPoint.Y = y;
                                return returnPoint;
                            }
                        }
                    }
                    break;
            }
            return returnPoint;
        }
        private Boolean getMultiColorPixel(RgbInfo[] rgbArray, int x, int y, int width, int heigt, List<ColorInfo> colorInfos)
        {
            RgbInfo rgb1 = rgbArray[y * width + x];
            ColorInfo cl1 = colorInfos[0];
            if (Math.Abs(cl1.r - rgb1.r) < cl1.rOffset && Math.Abs(cl1.g - rgb1.g) < cl1.gOffset && Math.Abs(cl1.b - rgb1.b) < cl1.bOffset)
            {
                for (int i = 0; i < colorInfos.Count; i++)
                {
                    ColorInfo cl = colorInfos[i];
                    if (x + cl.xOffset >= 0 && y + cl.yOffset >= 0)
                    {
                        int index = (y + cl.yOffset) * width + x + cl.xOffset;
                        if (index < rgbArray.Length)
                        {
                            RgbInfo rgb = rgbArray[index];
                            if (cl.isReverse)
                            {
                                if (Math.Abs(cl.r - rgb.r) < cl.rOffset && Math.Abs(cl.g - rgb.g) < cl.gOffset && Math.Abs(cl.b - rgb.b) < cl.bOffset)
                                {
                                    return false;
                                }
                            }
                            else
                            if (Math.Abs(cl.r - rgb.r) >= cl.rOffset || Math.Abs(cl.g - rgb.g) >= cl.gOffset || Math.Abs(cl.b - rgb.b) >= cl.bOffset)
                            {
                                return false;
                            }
                        }
                        else
                            return false;
                    }
                    else
                        return false;
                }
                return true;
            }
            else
                return false;
        }
    }
}

class ColorInfo
{
    public int xOffset;
    public int yOffset;
    public int r;
    public int g;
    public int b;
    public int rOffset;
    public int gOffset;
    public int bOffset;
    public Boolean isReverse = false;
}

class wholeOffset
{
    public int minPos;
    public int maxPos;
    public int offset;
}

class gapCount
{
    public int gap;
    public int looseCounts = 1;
    public int uniqueCounts = 1;
}

class verticalLine
{
    public Point startPoint;
    public Point endPoint;
    public int position;
    public Boolean needDelete = false;
    public Boolean validate = false;
}

class horizonLine
{
    public Point startPoint;
    public Point endPoint;
    public int position;
    public Boolean needDelete = false;
    public Boolean validate = false;
}

