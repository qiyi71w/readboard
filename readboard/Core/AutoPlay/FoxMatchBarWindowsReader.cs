using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace readboard
{
    internal static class FoxMatchBarWindowsReader
    {
        private const int MaxUiaNodes = 200;
        private const int MaxUiaDepth = 8;
        private const string RoomInfoPanelTitle = "CRoomInfoPanel";
        private const string PlayerListPanelTitle = "CRoomPlayerListPanel";

        private static readonly object EngineGate = new object();
        private static OcrEngine cachedEngine;
        private static bool engineResolved;

        public static FoxMatchBarReading TryRead(IntPtr boardHandle, IBoardCapturePlatform capture)
        {
            if (boardHandle == IntPtr.Zero || capture == null)
                return FoxMatchBarReading.Empty;

            try
            {
                if (!IsWindow(boardHandle))
                    return FoxMatchBarReading.Empty;

                IntPtr root = GetRoot(boardHandle);
                if (root == IntPtr.Zero || IsMinimized(root))
                    return FoxMatchBarReading.Empty;

                IntPtr infoHandle = FindNamedChild(root, RoomInfoPanelTitle);
                IntPtr listHandle = FindNamedChild(root, PlayerListPanelTitle);
                IList<string> directory = ReadDirectory(listHandle);
                if (infoHandle == IntPtr.Zero)
                    return new FoxMatchBarReading(string.Empty, string.Empty, directory);

                using (Bitmap info = CapturePanel(infoHandle, capture))
                {
                    if (info == null || info.Width < 4 || info.Height < 20)
                        return new FoxMatchBarReading(string.Empty, string.Empty, directory);

                    OcrEngine engine = TryGetEngine();
                    if (engine == null)
                        return new FoxMatchBarReading(string.Empty, string.Empty, directory);

                    Rectangle leftBounds;
                    Rectangle rightBounds;
                    if (!TryGetSeatBounds(info.Size, out leftBounds, out rightBounds))
                        return new FoxMatchBarReading(string.Empty, string.Empty, directory);

                    using (Bitmap left = Crop(info, leftBounds))
                    using (Bitmap right = Crop(info, rightBounds))
                    {
                        return new FoxMatchBarReading(
                            Recognize(left, engine),
                            Recognize(right, engine),
                            directory);
                    }
                }
            }
            catch
            {
                return FoxMatchBarReading.Empty;
            }
        }

        internal static bool TryGetSeatBounds(Size infoSize, out Rectangle leftBounds, out Rectangle rightBounds)
        {
            leftBounds = Rectangle.Empty;
            rightBounds = Rectangle.Empty;
            if (infoSize.Width < 4 || infoSize.Height < 20)
                return false;

            int y = infoSize.Height * 55 / 100;
            int h = Math.Max(20, infoSize.Height * 22 / 100);
            if (y + h > infoSize.Height)
                h = infoSize.Height - y;
            if (h < 16)
                return false;

            int mid = infoSize.Width / 2;
            if (mid < 2 || infoSize.Width - mid < 2)
                return false;

            leftBounds = new Rectangle(0, y, mid, h);
            rightBounds = new Rectangle(mid, y, infoSize.Width - mid, h);
            return true;
        }

        private static Bitmap CapturePanel(IntPtr handle, IBoardCapturePlatform capture)
        {
            Bitmap bitmap = capture.CaptureWindow(handle);
            if (bitmap != null && !IsMostlyBlack(bitmap))
                return bitmap;

            Bitmap printed = capture.CapturePrintWindow(handle);
            if (printed != null)
            {
                if (bitmap != null)
                    bitmap.Dispose();
                return printed;
            }

            return bitmap;
        }

        private static bool IsMostlyBlack(Bitmap bitmap)
        {
            if (bitmap == null)
                return true;

            int stepX = Math.Max(1, bitmap.Width / 20);
            int stepY = Math.Max(1, bitmap.Height / 20);
            int dark = 0;
            int total = 0;
            for (int y = 0; y < bitmap.Height; y += stepY)
            {
                for (int x = 0; x < bitmap.Width; x += stepX)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    total++;
                    if (pixel.R < 24 && pixel.G < 24 && pixel.B < 24)
                        dark++;
                }
            }

            return total > 0 && dark * 10 >= total * 9;
        }

        private static Bitmap Crop(Bitmap source, Rectangle bounds)
        {
            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, bounds.Width, bounds.Height),
                    bounds,
                    GraphicsUnit.Pixel);
            }

            return bitmap;
        }

        private static OcrEngine TryGetEngine()
        {
            lock (EngineGate)
            {
                if (engineResolved)
                    return cachedEngine;

                cachedEngine = OcrEngine.TryCreateFromLanguage(new Language("zh-Hans"));
                engineResolved = true;
                return cachedEngine;
            }
        }

        private static string Recognize(Bitmap bitmap, OcrEngine engine)
        {
            if (bitmap == null || engine == null)
                return string.Empty;

            return Task.Run(async () =>
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Bmp);
                    stream.Position = 0;
                    using (IRandomAccessStream ras = stream.AsRandomAccessStream())
                    {
                        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(ras);
                        SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();
                        OcrResult result = await engine.RecognizeAsync(softwareBitmap);
                        return JoinLines(result);
                    }
                }
            }).GetAwaiter().GetResult();
        }

        private static string JoinLines(OcrResult result)
        {
            if (result == null || result.Lines == null)
                return string.Empty;

            StringBuilder builder = new StringBuilder();
            foreach (OcrLine line in result.Lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.Text))
                    continue;
                builder.Append(line.Text.Trim());
            }

            return builder.ToString();
        }

        private static IList<string> ReadDirectory(IntPtr listHandle)
        {
            List<string> names = new List<string>();
            if (listHandle == IntPtr.Zero)
                return names;

            AutomationElement root;
            try
            {
                root = AutomationElement.FromHandle(listHandle);
            }
            catch
            {
                return names;
            }

            if (root == null)
                return names;

            int count = 0;
            WalkAutomation(root, 0, names, ref count);
            return names;
        }

        private static void WalkAutomation(
            AutomationElement element,
            int depth,
            List<string> names,
            ref int count)
        {
            if (element == null || depth > MaxUiaDepth || count >= MaxUiaNodes)
                return;

            string name = Safe(() => element.Current.Name);
            if (!string.IsNullOrWhiteSpace(name)
                && !string.Equals(name, PlayerListPanelTitle, StringComparison.Ordinal))
            {
                names.Add(name);
                count++;
            }

            AutomationElement child;
            try
            {
                child = TreeWalker.ControlViewWalker.GetFirstChild(element);
            }
            catch
            {
                return;
            }

            while (child != null && count < MaxUiaNodes)
            {
                WalkAutomation(child, depth + 1, names, ref count);
                try
                {
                    child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                }
                catch
                {
                    break;
                }
            }
        }

        private static string Safe(Func<string> read)
        {
            try
            {
                return read() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsMinimized(IntPtr handle)
        {
            if (handle == IntPtr.Zero || IsIconic(handle) || !IsWindowVisible(handle))
                return true;

            RECT native;
            if (!GetWindowRect(handle, out native))
                return true;

            int width = native.Right - native.Left;
            int height = native.Bottom - native.Top;
            return native.Left <= -10000
                || native.Top <= -10000
                || width <= 1
                || height <= 1;
        }

        private static IntPtr GetRoot(IntPtr handle)
        {
            IntPtr root = handle;
            IntPtr parent = GetParent(root);
            while (parent != IntPtr.Zero)
            {
                root = parent;
                parent = GetParent(root);
            }

            return root;
        }

        private static IntPtr FindNamedChild(IntPtr root, string title)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(root, delegate(IntPtr child, IntPtr parameter)
            {
                if (!IsWindowVisible(child))
                    return true;
                if (!string.Equals(GetWindowTitle(child), title, StringComparison.Ordinal))
                    return true;
                found = child;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private static string GetWindowTitle(IntPtr handle)
        {
            StringBuilder builder = new StringBuilder(256);
            GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private delegate bool EnumProc(IntPtr handle, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
