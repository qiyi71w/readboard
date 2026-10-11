using System;
using System.Collections.Generic;
using System.Drawing;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class WebViewWindowModeRuntimeTests
    {
        private static readonly Size NonClient = new Size(16, 39);
        private static readonly Rectangle PrimaryWorkingArea = new Rectangle(0, 0, 1920, 1040);

        [Theory]
        [InlineData(96, 680, 218)]
        [InlineData(144, 1020, 327)]
        public void EnterMini_FixesClientSizeToScaledMiniSizeAndPinsWindow(int dpi, int width, int height)
        {
            FakeWindow window = new FakeWindow(dpi);
            WebViewWindowModeRuntime runtime = CreateFull(window, new Rectangle(100, 80, 1200, 760));

            Assert.True(Apply(runtime, WebViewWindowModeIntent.EnterMini()));

            Assert.True(runtime.MiniMode);
            Assert.Equal(new Size(width, height), window.ClientSize);
            Assert.Equal(window.Bounds.Size, window.MinimumSize);
            Assert.Equal(window.Bounds.Size, window.MaximumSize);
            Assert.True(window.TopMost);
            Assert.Equal(new Point(100, 80), window.Bounds.Location);
        }

        [Fact]
        public void DpiChangeInMini_RecomputesFixedSizeAtNewDpi()
        {
            FakeWindow window = new FakeWindow(96);
            WebViewWindowModeRuntime runtime = CreateFull(window, new Rectangle(100, 80, 1200, 760));
            Apply(runtime, WebViewWindowModeIntent.EnterMini());

            window.Dpi = 144;
            runtime.RefreshForDpi();

            Assert.Equal(new Size(1020, 327), window.ClientSize);
            Assert.Equal(window.Bounds.Size, window.MinimumSize);
            Assert.Equal(window.Bounds.Size, window.MaximumSize);
            Assert.Equal(new Point(100, 80), window.Bounds.Location);
        }

        [Fact]
        public void StartupInMini_ClampsRecordedMiniPositionIntoAvailableScreenWhenOriginalScreenIsGone()
        {
            FakeWindow window = new FakeWindow(96);
            WebViewWindowModeRuntime runtime = new WebViewWindowModeRuntime(window);
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.MiniMode = true;
            config.MiniWindowPosX = 3500;
            config.MiniWindowPosY = 1500;

            runtime.Initialize(config);

            Assert.True(runtime.MiniMode);
            Assert.True(PrimaryWorkingArea.Contains(window.Bounds));
            Assert.Equal(new Size(680, 218), window.ClientSize);
            Assert.True(window.TopMost);
        }

        [Fact]
        public void ReenteringMini_ClampsPartiallyOffscreenRecordedMiniPositionIntoWorkingArea()
        {
            FakeWindow window = new FakeWindow(96);
            WebViewWindowModeRuntime runtime = CreateFull(window, new Rectangle(100, 80, 1200, 760));
            Apply(runtime, WebViewWindowModeIntent.EnterMini());
            window.SetBounds(new Rectangle(1800, 1000, window.Bounds.Width, window.Bounds.Height));
            Apply(runtime, WebViewWindowModeIntent.ExitMini(WebViewPage.ControlCenter));

            Apply(runtime, WebViewWindowModeIntent.EnterMini());

            Assert.True(PrimaryWorkingArea.Contains(window.Bounds));
            Assert.Equal(new Size(680, 218), window.ClientSize);
        }

        [Fact]
        public void MaximizedFullToMiniAndBack_RestoresMaximizedStateAndOriginalBounds()
        {
            FakeWindow window = new FakeWindow(144);
            Rectangle fullBounds = new Rectangle(120, 90, 1516, 939);
            WebViewWindowModeRuntime runtime = CreateFull(window, fullBounds);
            window.SetState(WebViewWindowState.Maximized);

            Apply(runtime, WebViewWindowModeIntent.EnterMini());
            Assert.Equal(WebViewWindowState.Normal, window.State);
            Assert.Equal(new Size(1020, 327), window.ClientSize);
            window.SetBounds(new Rectangle(400, 300, window.Bounds.Width, window.Bounds.Height));

            WebViewPage shown = WebViewPage.About;
            Assert.True(runtime.Apply(WebViewWindowModeIntent.ExitMini(WebViewPage.Rules), page => shown = page));

            Assert.False(runtime.MiniMode);
            Assert.Equal(WebViewPage.Rules, shown);
            Assert.Equal(WebViewWindowState.Maximized, window.State);
            Assert.Equal(fullBounds, window.RestoreBounds);
            Assert.False(window.TopMost);
            Assert.Equal(Size.Empty, window.MaximumSize);

            Apply(runtime, WebViewWindowModeIntent.EnterMini());
            Assert.Equal(new Point(400, 300), window.Bounds.Location);
        }

        [Fact]
        public void PersistingWhileMini_WritesRecordedFullWindowNotMiniSize()
        {
            FakeWindow window = new FakeWindow(144);
            WebViewWindowModeRuntime runtime = CreateFull(window, new Rectangle(120, 90, 1516, 939));
            Apply(runtime, WebViewWindowModeIntent.EnterMini());
            window.SetBounds(new Rectangle(400, 300, window.Bounds.Width, window.Bounds.Height));
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");

            runtime.WriteTo(config);

            Assert.True(config.MiniMode);
            Assert.Equal(120, config.WindowPosX);
            Assert.Equal(90, config.WindowPosY);
            Assert.Equal(1000, config.WindowClientWidth);
            Assert.False(config.WindowMaximized);
            Assert.Equal(600, config.WindowClientHeight);
            Assert.Equal(400, config.MiniWindowPosX);
            Assert.Equal(300, config.MiniWindowPosY);
        }

        [Fact]
        public void PersistedMiniConfig_RestartsInMiniAndReturnsToPersistedFullWindow()
        {
            FakeWindow window = new FakeWindow(96);
            WebViewWindowModeRuntime runtime = new WebViewWindowModeRuntime(window);
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.WindowPosX = 200;
            config.WindowPosY = 100;
            config.WindowClientWidth = 1234;
            config.WindowClientHeight = 777;
            config.WindowMaximized = true;
            config.MiniMode = true;
            config.MiniWindowPosX = 500;
            config.MiniWindowPosY = 400;

            runtime.Initialize(config);
            Assert.Equal(new Point(500, 400), window.Bounds.Location);
            Assert.Equal(new Size(680, 218), window.ClientSize);

            AppConfig rewritten = AppConfig.CreateDefault("220430", "TEST");
            runtime.WriteTo(rewritten);
            Assert.Equal(1234, rewritten.WindowClientWidth);
            Assert.Equal(777, rewritten.WindowClientHeight);
            Assert.True(rewritten.WindowMaximized);

            Apply(runtime, WebViewWindowModeIntent.ExitMini(WebViewPage.ControlCenter));
            Assert.Equal(WebViewWindowState.Maximized, window.State);
            Assert.Equal(new Rectangle(200, 100, 1234 + NonClient.Width, 777 + NonClient.Height), window.RestoreBounds);
        }

        [Fact]
        public void StartupWithDefaultConfig_StaysInFullModeWithoutTopMost()
        {
            FakeWindow window = new FakeWindow(96);
            WebViewWindowModeRuntime runtime = new WebViewWindowModeRuntime(window);

            runtime.Initialize(AppConfig.CreateDefault("220430", "TEST"));

            Assert.False(runtime.MiniMode);
            Assert.False(window.TopMost);
            Assert.Equal(new Size(1100, 680), window.ClientSize);
            AppConfig rewritten = AppConfig.CreateDefault("220430", "TEST");
            runtime.WriteTo(rewritten);
            Assert.False(rewritten.MiniMode);
            Assert.Equal(-1, rewritten.MiniWindowPosX);
            Assert.Equal(-1, rewritten.MiniWindowPosY);
        }

        [Fact]
        public void ModeCommands_PublishOneSnapshotWithModeAndPageTogetherAndNoOpInTargetMode()
        {
            FakeWindow window = new FakeWindow(96);
            WebViewWindowModeRuntime runtime = CreateFull(window, new Rectangle(100, 80, 1200, 760));
            WebViewPage currentPage = WebViewPage.ControlCenter;
            List<string> published = new List<string>();
            WebViewStatePublisher publisher = new WebViewStatePublisher(
                () => published.Add(runtime.MiniMode + ":" + currentPage));
            // Window resizes post snapshot requests, as the production Resize handler does.
            window.Changed = publisher.Request;
            Func<WebViewWindowModeIntent, bool> dispatch = intent => publisher.Dispatch(
                () => runtime.Apply(intent, page => currentPage = page));

            Assert.True(dispatch(WebViewWindowModeIntent.EnterMini()));
            Assert.Equal(new[] { "True:ControlCenter" }, published);

            Assert.False(dispatch(WebViewWindowModeIntent.EnterMini()));
            Assert.Single(published);

            Assert.True(dispatch(WebViewWindowModeIntent.ExitMini(WebViewPage.Settings)));
            Assert.Equal(new[] { "True:ControlCenter", "False:Settings" }, published);

            Assert.False(dispatch(WebViewWindowModeIntent.ExitMini(WebViewPage.Rules)));
            Assert.Equal(2, published.Count);
            Assert.Equal(WebViewPage.Settings, currentPage);
        }

        [Fact]
        public void FirstEntryFromMaximizedFull_PlacesMiniAtVisibleFullWindowTopLeft()
        {
            FakeWindow window = new FakeWindow(96);
            Rectangle fullBounds = new Rectangle(300, 200, 1216, 799);
            WebViewWindowModeRuntime runtime = CreateFull(window, fullBounds);
            window.SetState(WebViewWindowState.Maximized);

            Apply(runtime, WebViewWindowModeIntent.EnterMini());

            Assert.Equal(PrimaryWorkingArea.Location, window.Bounds.Location);
            Apply(runtime, WebViewWindowModeIntent.ExitMini(WebViewPage.ControlCenter));
            Assert.Equal(WebViewWindowState.Maximized, window.State);
            Assert.Equal(fullBounds, window.RestoreBounds);
        }

        [Fact]
        public void ExitFromMiniOnHigherDpiMonitor_RestoresOriginalFullBoundsOnOriginalMonitor()
        {
            FakeWindow window = new FakeWindow(96);
            window.SecondScreen = new Rectangle(1920, 0, 2560, 1400);
            window.SecondScreenDpi = 144;
            Rectangle fullBounds = new Rectangle(100, 80, 1234 + NonClient.Width, 777 + NonClient.Height);
            WebViewWindowModeRuntime runtime = CreateFull(window, fullBounds);
            window.DpiChanged = runtime.RefreshForDpi;
            Apply(runtime, WebViewWindowModeIntent.EnterMini());

            window.SetBounds(new Rectangle(2000, 100, window.Bounds.Width, window.Bounds.Height));
            Assert.Equal(144, window.Dpi);
            Assert.Equal(new Size(1020, 327), window.ClientSize);

            Apply(runtime, WebViewWindowModeIntent.ExitMini(WebViewPage.ControlCenter));

            Assert.Equal(96, window.Dpi);
            Assert.Equal(fullBounds, window.Bounds);
        }

        [Fact]
        public void StartupInMiniWithRepeatedInitialize_RestoresFullClientSizeIncludingFrame()
        {
            FakeWindow window = new FakeWindow(96);
            WebViewWindowModeRuntime runtime = new WebViewWindowModeRuntime(window);
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.WindowPosX = 200;
            config.WindowPosY = 100;
            config.WindowClientWidth = 1234;
            config.WindowClientHeight = 777;
            config.MiniMode = true;

            // Production applies saved bounds before and again after handle creation.
            runtime.Initialize(config);
            runtime.Initialize(config);
            Assert.Equal(new Size(680, 218), window.ClientSize);
            Apply(runtime, WebViewWindowModeIntent.ExitMini(WebViewPage.ControlCenter));

            Assert.Equal(new Size(1234, 777), window.ClientSize);
            AppConfig rewritten = AppConfig.CreateDefault("220430", "TEST");
            runtime.WriteTo(rewritten);
            Assert.Equal(1234, rewritten.WindowClientWidth);
            Assert.Equal(777, rewritten.WindowClientHeight);
        }

        private static WebViewWindowModeRuntime CreateFull(FakeWindow window, Rectangle bounds)
        {
            WebViewWindowModeRuntime runtime = new WebViewWindowModeRuntime(window);
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            Size logical = WebViewWindowLayoutPolicy.UnscalePhysicalSize(
                WebViewWindowLayoutPolicy.ResolveClientSizeFromOuterBounds(bounds.Size, NonClient),
                window.Dpi);
            config.WindowPosX = bounds.X;
            config.WindowPosY = bounds.Y;
            config.WindowClientWidth = logical.Width;
            config.WindowClientHeight = logical.Height;
            runtime.Initialize(config);
            return runtime;
        }

        private static bool Apply(WebViewWindowModeRuntime runtime, WebViewWindowModeIntent intent)
        {
            return runtime.Apply(intent, delegate { });
        }

        private sealed class FakeWindow : IWebViewWindowModeAdapter
        {
            private Rectangle normalBounds = new Rectangle(10, 10, 800, 600);
            private readonly int primaryDpi;

            public FakeWindow(int dpi)
            {
                Dpi = dpi;
                primaryDpi = dpi;
            }

            public int Dpi { get; set; }
            public WebViewWindowState State { get; private set; }
            public Size MinimumSize { get; private set; }
            public Size MaximumSize { get; private set; }
            public bool TopMost { get; private set; }
            public Action Changed { get; set; }
            // A second monitor with its own DPI; landing on another monitor rescales the
            // window by the DPI ratio (the suggested rectangle) and raises the DPI callback.
            public Rectangle SecondScreen { get; set; }
            public int SecondScreenDpi { get; set; }
            public Action DpiChanged { get; set; }

            public Rectangle Bounds
            {
                get { return State == WebViewWindowState.Maximized ? PrimaryWorkingArea : normalBounds; }
            }

            public Rectangle RestoreBounds { get { return normalBounds; } }

            public Size ClientSize
            {
                get
                {
                    return WebViewWindowLayoutPolicy.ResolveClientSizeFromOuterBounds(Bounds.Size, NonClientSize);
                }
            }

            // Mini chrome has no sizing frame, as in production.
            public Size NonClientSize { get { return TopMost ? Size.Empty : NonClient; } }

            public Rectangle VirtualScreen { get { return Rectangle.Union(PrimaryWorkingArea, SecondScreen); } }

            public Rectangle GetWorkingArea(Point location)
            {
                return !SecondScreen.IsEmpty && SecondScreen.Contains(location) ? SecondScreen : PrimaryWorkingArea;
            }

            public void SetState(WebViewWindowState state)
            {
                State = state;
                Changed?.Invoke();
            }

            public void SetSizeLimits(Size minimum, Size maximum)
            {
                MinimumSize = minimum;
                MaximumSize = maximum;
            }

            public void SetBounds(Rectangle bounds)
            {
                int width = Math.Max(bounds.Width, MinimumSize.Width);
                int height = Math.Max(bounds.Height, MinimumSize.Height);
                if (!MaximumSize.IsEmpty)
                {
                    width = Math.Min(width, MaximumSize.Width);
                    height = Math.Min(height, MaximumSize.Height);
                }
                normalBounds = new Rectangle(bounds.Location, new Size(width, height));
                if (!SecondScreen.IsEmpty)
                {
                    int monitorDpi = SecondScreen.Contains(bounds.Location) ? SecondScreenDpi : primaryDpi;
                    if (monitorDpi != Dpi)
                    {
                        double ratio = monitorDpi / (double)Dpi;
                        normalBounds = new Rectangle(
                            normalBounds.Location,
                            new Size(
                                (int)Math.Round(normalBounds.Width * ratio),
                                (int)Math.Round(normalBounds.Height * ratio)));
                        Dpi = monitorDpi;
                        DpiChanged?.Invoke();
                    }
                }
                Changed?.Invoke();
            }

            public void ApplyChrome(bool miniMode)
            {
                TopMost = miniMode;
            }
        }
    }
}
