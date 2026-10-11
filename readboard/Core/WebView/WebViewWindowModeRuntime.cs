using System;
using System.Drawing;

namespace readboard
{
    internal interface IWebViewWindowModeAdapter
    {
        int Dpi { get; }
        WebViewWindowState State { get; }
        Rectangle Bounds { get; }
        Rectangle RestoreBounds { get; }
        Size ClientSize { get; }
        Size NonClientSize { get; }
        Rectangle VirtualScreen { get; }
        Rectangle GetWorkingArea(Point location);
        void SetState(WebViewWindowState state);
        void SetSizeLimits(Size minimum, Size maximum);
        void SetBounds(Rectangle bounds);
        void ApplyChrome(bool miniMode);
    }

    internal sealed class WebViewWindowModeIntent
    {
        private WebViewWindowModeIntent(bool miniMode, WebViewPage page)
        {
            MiniMode = miniMode;
            Page = page;
        }

        public bool MiniMode { get; private set; }
        public WebViewPage Page { get; private set; }

        public static WebViewWindowModeIntent EnterMini()
        {
            return new WebViewWindowModeIntent(true, WebViewPage.ControlCenter);
        }

        public static WebViewWindowModeIntent ExitMini(WebViewPage page)
        {
            return new WebViewWindowModeIntent(false, page);
        }
    }

    // Owns the main window's display form. Full-mode placement is recorded on entering
    // Mini Mode so persistence and the return trip never see the fixed mini size.
    internal sealed class WebViewWindowModeRuntime
    {
        private sealed class FullWindowPlacement
        {
            public FullWindowPlacement(Rectangle normalBounds, Size logicalClientSize, bool maximized, int dpi)
            {
                NormalBounds = normalBounds;
                LogicalClientSize = logicalClientSize;
                Maximized = maximized;
                Dpi = dpi;
            }

            public Rectangle NormalBounds { get; private set; }
            public Size LogicalClientSize { get; private set; }
            public bool Maximized { get; private set; }
            public int Dpi { get; private set; }
        }

        private static readonly Point UnsetLocation = new Point(-1, -1);

        private readonly IWebViewWindowModeAdapter adapter;
        private FullWindowPlacement fullPlacement;
        private Point? miniLocation;

        public WebViewWindowModeRuntime(IWebViewWindowModeAdapter adapter)
        {
            this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public bool MiniMode { get; private set; }

        public void Initialize(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            int dpi = adapter.Dpi;
            Size fullMinimumSize = ResolveFullMinimumOuterSize(dpi);
            Size desiredSize = ToOuterSize(
                WebViewWindowLayoutPolicy.ScaleLogicalSize(
                    new Size(config.WindowClientWidth, config.WindowClientHeight),
                    dpi));
            Point desiredLocation = IsUnset(config.WindowPosX, config.WindowPosY)
                ? adapter.Bounds.Location
                : new Point(config.WindowPosX, config.WindowPosY);
            Rectangle workingArea = adapter.GetWorkingArea(desiredLocation);
            Rectangle fullBounds = WebViewWindowLayoutPolicy.ClampBoundsToWorkingArea(
                new Rectangle(desiredLocation, desiredSize),
                workingArea,
                fullMinimumSize);
            miniLocation = IsUnset(config.MiniWindowPosX, config.MiniWindowPosY)
                ? (Point?)null
                : new Point(config.MiniWindowPosX, config.MiniWindowPosY);

            if (config.MiniMode)
            {
                fullPlacement = new FullWindowPlacement(
                    fullBounds,
                    new Size(config.WindowClientWidth, config.WindowClientHeight),
                    config.WindowMaximized,
                    dpi);
                MiniMode = true;
                ApplyMiniWindow(fullBounds.Location);
                return;
            }

            fullPlacement = null;
            MiniMode = false;
            adapter.ApplyChrome(false);
            adapter.SetSizeLimits(LimitToWorkingArea(fullMinimumSize, workingArea), Size.Empty);
            adapter.SetBounds(fullBounds);
            if (config.WindowMaximized)
                adapter.SetState(WebViewWindowState.Maximized);
        }

        public bool Apply(WebViewWindowModeIntent intent, Action<WebViewPage> showPage)
        {
            if (intent == null)
                throw new ArgumentNullException(nameof(intent));
            if (showPage == null)
                throw new ArgumentNullException(nameof(showPage));

            if (intent.MiniMode)
                return EnterMini();
            if (!ExitMini())
                return false;
            showPage(intent.Page);
            return true;
        }

        public void RefreshForDpi()
        {
            int dpi = adapter.Dpi;
            if (MiniMode)
            {
                Size miniSize = ResolveMiniOuterSize(dpi);
                adapter.SetSizeLimits(miniSize, miniSize);
                Rectangle bounds = adapter.Bounds;
                if (adapter.State == WebViewWindowState.Normal && bounds.Size != miniSize)
                    adapter.SetBounds(new Rectangle(bounds.Location, miniSize));
                return;
            }

            adapter.SetSizeLimits(
                LimitToWorkingArea(
                    ResolveFullMinimumOuterSize(dpi),
                    adapter.GetWorkingArea(adapter.Bounds.Location)),
                Size.Empty);
        }

        public void WriteTo(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            FullWindowPlacement full = MiniMode ? fullPlacement : CaptureFullPlacement();
            Point fullLocation = ResolvePersistableLocation(full.NormalBounds);
            config.WindowPosX = fullLocation.X;
            config.WindowPosY = fullLocation.Y;
            config.WindowClientWidth = full.LogicalClientSize.Width;
            config.WindowClientHeight = full.LogicalClientSize.Height;
            config.WindowMaximized = full.Maximized;

            Point persistedMiniLocation = MiniMode
                ? ResolvePersistableLocation(ResolveNormalBounds())
                : miniLocation.HasValue
                    ? ResolvePersistableLocation(
                        new Rectangle(miniLocation.Value, ResolveMiniOuterSize(adapter.Dpi)))
                    : UnsetLocation;
            config.MiniMode = MiniMode;
            config.MiniWindowPosX = persistedMiniLocation.X;
            config.MiniWindowPosY = persistedMiniLocation.Y;
        }

        private bool EnterMini()
        {
            if (MiniMode)
                return false;

            // A maximized full window's visible top-left is the first mini position;
            // its restore bounds stay recorded for the return trip.
            Point fullTopLeft = adapter.State == WebViewWindowState.Maximized
                ? adapter.Bounds.Location
                : ResolveNormalBounds().Location;
            fullPlacement = CaptureFullPlacement();
            MiniMode = true;
            ApplyMiniWindow(fullTopLeft);
            return true;
        }

        private bool ExitMini()
        {
            if (!MiniMode)
                return false;

            miniLocation = ResolveNormalBounds().Location;
            FullWindowPlacement full = fullPlacement;
            fullPlacement = null;
            MiniMode = false;
            if (adapter.State != WebViewWindowState.Normal)
                adapter.SetState(WebViewWindowState.Normal);

            adapter.ApplyChrome(false);
            if (full.Dpi != adapter.Dpi)
            {
                // Land on the original monitor first so its DPI applies before sizing;
                // sizing across the DPI boundary would be rescaled by Windows afterwards.
                adapter.SetSizeLimits(Size.Empty, Size.Empty);
                adapter.SetBounds(new Rectangle(full.NormalBounds.Location, adapter.Bounds.Size));
            }

            int dpi = adapter.Dpi;
            Size fullMinimumSize = ResolveFullMinimumOuterSize(dpi);
            Rectangle desiredBounds = full.Dpi == dpi
                ? full.NormalBounds
                : new Rectangle(
                    full.NormalBounds.Location,
                    ToOuterSize(WebViewWindowLayoutPolicy.ScaleLogicalSize(full.LogicalClientSize, dpi)));
            Rectangle workingArea = adapter.GetWorkingArea(desiredBounds.Location);
            adapter.SetSizeLimits(LimitToWorkingArea(fullMinimumSize, workingArea), Size.Empty);
            adapter.SetBounds(
                WebViewWindowLayoutPolicy.ClampBoundsToWorkingArea(
                    desiredBounds,
                    workingArea,
                    fullMinimumSize));
            if (full.Maximized)
                adapter.SetState(WebViewWindowState.Maximized);
            return true;
        }

        private void ApplyMiniWindow(Point firstLocation)
        {
            if (adapter.State != WebViewWindowState.Normal)
                adapter.SetState(WebViewWindowState.Normal);

            // Chrome first: the mini window style has a different non-client frame.
            adapter.ApplyChrome(true);
            Size miniSize = ResolveMiniOuterSize(adapter.Dpi);
            Point desiredLocation = miniLocation ?? firstLocation;
            Rectangle bounds = WebViewWindowLayoutPolicy.ClampBoundsToWorkingArea(
                new Rectangle(desiredLocation, miniSize),
                adapter.GetWorkingArea(desiredLocation),
                miniSize);
            adapter.SetSizeLimits(miniSize, miniSize);
            adapter.SetBounds(bounds);
        }

        private FullWindowPlacement CaptureFullPlacement()
        {
            WebViewWindowState state = adapter.State;
            Rectangle normalBounds = ResolveNormalBounds();
            Size clientSize = adapter.ClientSize;
            Size physicalClientSize =
                state == WebViewWindowState.Normal && clientSize.Width > 0 && clientSize.Height > 0
                    ? clientSize
                    : WebViewWindowLayoutPolicy.ResolveClientSizeFromOuterBounds(
                        normalBounds.Size,
                        adapter.NonClientSize);
            int dpi = adapter.Dpi;
            Size logicalClientSize = WebViewWindowLayoutPolicy.UnscalePhysicalSize(physicalClientSize, dpi);
            return new FullWindowPlacement(
                normalBounds,
                new Size(
                    Math.Max(AppConfig.MinimumWindowClientWidth, logicalClientSize.Width),
                    Math.Max(AppConfig.MinimumWindowClientHeight, logicalClientSize.Height)),
                state == WebViewWindowState.Maximized,
                dpi);
        }

        private Rectangle ResolveNormalBounds()
        {
            Rectangle bounds = adapter.Bounds;
            return adapter.State == WebViewWindowState.Normal && bounds.Width > 0 && bounds.Height > 0
                ? bounds
                : adapter.RestoreBounds;
        }

        private Point ResolvePersistableLocation(Rectangle bounds)
        {
            Rectangle virtualScreen = adapter.VirtualScreen;
            if (bounds.X <= -16000
                || bounds.Y <= -16000
                || bounds.Width <= 0
                || bounds.Height <= 0
                || virtualScreen.Width <= 0
                || virtualScreen.Height <= 0
                || !virtualScreen.Contains(bounds.Location))
                return UnsetLocation;
            return bounds.Location;
        }

        private Size ResolveMiniOuterSize(int dpi)
        {
            return ToOuterSize(
                WebViewWindowLayoutPolicy.ScaleLogicalSize(
                    WebViewWindowLayoutPolicy.MiniLogicalClientSize,
                    dpi));
        }

        private Size ResolveFullMinimumOuterSize(int dpi)
        {
            return ToOuterSize(
                WebViewWindowLayoutPolicy.ScaleLogicalSize(
                    WebViewWindowLayoutPolicy.MinimumLogicalClientSize,
                    dpi));
        }

        private Size ToOuterSize(Size clientSize)
        {
            Size nonClientSize = adapter.NonClientSize;
            return new Size(
                clientSize.Width + nonClientSize.Width,
                clientSize.Height + nonClientSize.Height);
        }

        private static Size LimitToWorkingArea(Size size, Rectangle workingArea)
        {
            if (workingArea.Width <= 0 || workingArea.Height <= 0)
                return size;
            return new Size(
                Math.Min(size.Width, workingArea.Width),
                Math.Min(size.Height, workingArea.Height));
        }

        private static bool IsUnset(int x, int y)
        {
            return x == -1 || y == -1;
        }
    }
}
