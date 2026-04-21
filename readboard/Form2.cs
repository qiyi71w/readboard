using System;
using System.Drawing;
using System.Windows.Forms;

namespace readboard
{
    public partial class Form2 : Form
    {
        private const int SelectionInvalidatePadding = 2;
        private const int MagnifierLogicalMargin = 12;

        private int x1;
        private int y1;
        private int x2;
        private int y2;
        private bool isMouthDown = false;
        private Rectangle selectionBoundsScreen = Rectangle.Empty;
        private MagnifierForm form5;
        private readonly MainForm host;
        private readonly bool needMag;

        internal Form2(MainForm host, Boolean needMag)
        {
            InitializeComponent();
            this.host = RequireHost(host);
            this.needMag = needMag;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            UpdateStyles();
            ApplySelectionOverlayBounds();
            if (Program.CurrentConfig.UseMagnifier && needMag)
            {
                form5 = new MagnifierForm(this.host);
                form5.StartPosition = FormStartPosition.Manual;
                PositionMagnifier(MousePosition);
                form5.Show();
            }
        }

        private void Form2_MouseDown(object sender, MouseEventArgs e)
        {
            x1 = MousePosition.X;
            y1 = MousePosition.Y;
            isMouthDown = true;
            selectionBoundsScreen = Rectangle.Empty;
        }

        private void Form2_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMouthDown)
            {
                UpdateSelectionBounds(MousePosition);
            }
            if (Program.CurrentConfig.UseMagnifier && needMag && form5 != null && !form5.IsDisposed)
            {
                Point mousePosition = MousePosition;
                PositionMagnifier(mousePosition);
                form5.setPic(mousePosition.X, mousePosition.Y);
            }
        }

        private void Form2_MouseUp(object sender, MouseEventArgs e)
        {
            x2 = MousePosition.X + 1;
            y2 = MousePosition.Y + 1;
            CloseMagnifier();
            this.Hide();
            this.Close();

            MainForm mainForm = TryGetHost();
            if (mainForm == null)
                return;
            mainForm.Snap(x1, y1, x2, y2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!isMouthDown || selectionBoundsScreen.IsEmpty)
            {
                return;
            }

            Rectangle selectionBoundsClient = ScreenRectangleToClient(selectionBoundsScreen);
            e.Graphics.FillRectangle(Brushes.DarkBlue, selectionBoundsClient);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CloseMagnifier();
            base.OnFormClosed(e);
        }

        private void UpdateSelectionBounds(Point currentScreenPoint)
        {
            Rectangle previousBounds = selectionBoundsScreen;
            selectionBoundsScreen = CreateSelectionBounds(currentScreenPoint);
            InvalidateSelection(previousBounds, selectionBoundsScreen);
        }

        private Rectangle CreateSelectionBounds(Point currentScreenPoint)
        {
            int left = Math.Min(x1, currentScreenPoint.X);
            int top = Math.Min(y1, currentScreenPoint.Y);
            int width = Math.Abs(currentScreenPoint.X - x1) + 1;
            int height = Math.Abs(currentScreenPoint.Y - y1) + 1;
            return new Rectangle(left, top, width, height);
        }

        private void InvalidateSelection(Rectangle previousBoundsScreen, Rectangle currentBoundsScreen)
        {
            Rectangle invalidateBoundsScreen = Rectangle.Union(previousBoundsScreen, currentBoundsScreen);
            if (invalidateBoundsScreen.IsEmpty)
            {
                return;
            }

            Rectangle invalidateBoundsClient = ScreenRectangleToClient(invalidateBoundsScreen);
            invalidateBoundsClient.Inflate(SelectionInvalidatePadding, SelectionInvalidatePadding);
            Invalidate(invalidateBoundsClient);
        }

        private Rectangle ScreenRectangleToClient(Rectangle screenBounds)
        {
            if (screenBounds.IsEmpty)
            {
                return Rectangle.Empty;
            }

            return new Rectangle(PointToClient(screenBounds.Location), screenBounds.Size);
        }

        private void CloseMagnifier()
        {
            if (form5 == null)
            {
                return;
            }

            if (!form5.IsDisposed)
            {
                form5.Hide();
                form5.Close();
            }

            form5 = null;
        }

        private void ApplySelectionOverlayBounds()
        {
            PixelRect virtualScreenBounds = DisplayScaling.GetVirtualScreenBounds();
            Bounds = new Rectangle(
                virtualScreenBounds.X,
                virtualScreenBounds.Y,
                virtualScreenBounds.Width,
                virtualScreenBounds.Height);
            WindowState = FormWindowState.Normal;
        }

        private void PositionMagnifier(Point anchorPoint)
        {
            if (form5 == null || form5.IsDisposed)
                return;

            Rectangle workingArea = Screen.FromPoint(anchorPoint).WorkingArea;
            int margin = Math.Max(8, (int)Math.Round(DisplayScaling.GetScaleForPoint(anchorPoint) * MagnifierLogicalMargin));
            Point preferredLocation = new Point(
                workingArea.Left + margin,
                workingArea.Bottom - form5.Height - margin);
            form5.Location = ClampToWorkingArea(preferredLocation, form5.Size, workingArea);
        }

        private static Point ClampToWorkingArea(Point location, Size windowSize, Rectangle workingArea)
        {
            int maxX = Math.Max(workingArea.Left, workingArea.Right - windowSize.Width);
            int maxY = Math.Max(workingArea.Top, workingArea.Bottom - windowSize.Height);
            return new Point(
                Math.Min(Math.Max(location.X, workingArea.Left), maxX),
                Math.Min(Math.Max(location.Y, workingArea.Top), maxY));
        }

        private MainForm TryGetHost()
        {
            if (host.IsDisposed)
                return null;
            return host;
        }

        private static MainForm RequireHost(MainForm host)
        {
            if (host == null || host.IsDisposed)
                throw new InvalidOperationException("MainForm host is unavailable.");
            return host;
        }

    }
}
