using System;
using System.Globalization;

namespace readboard
{
    internal interface IOverlayService
    {
        OverlayUpdateResult BuildUpdate(OverlayUpdateRequest request);
        void Reset();
    }

    internal sealed class LegacyOverlayService : IOverlayService
    {
        private const string InBoardCommand = "inboard";
        private const string DpiTokenPrefix = "99_";
        private const string DpiTokenSeparator = "_";
        private const double DefaultDpiScale = 1d;

        private string lastProtocolLine;

        public OverlayUpdateResult BuildUpdate(OverlayUpdateRequest request)
        {
            OverlayVisibility visibility = request == null ? OverlayVisibility.Hidden : request.Visibility;
            string protocolLine = BuildProtocolLine(request);
            bool shouldSend = ShouldSend(protocolLine);
            if (shouldSend)
                lastProtocolLine = protocolLine;

            return new OverlayUpdateResult
            {
                ShouldSend = shouldSend,
                Visibility = visibility,
                ProtocolLine = protocolLine
            };
        }

        public void Reset()
        {
            lastProtocolLine = null;
        }

        private bool ShouldSend(string protocolLine)
        {
            if (string.IsNullOrWhiteSpace(protocolLine))
                return false;

            return !string.Equals(lastProtocolLine, protocolLine, StringComparison.Ordinal);
        }

        private static string BuildProtocolLine(OverlayUpdateRequest request)
        {
            if (request == null)
                return null;

            if (request.Visibility == OverlayVisibility.Hidden)
                return GetHiddenCommand(request.HiddenCommandText);

            return BuildVisibleCommand(request);
        }

        private static string GetHiddenCommand(string hiddenCommandText)
        {
            if (string.IsNullOrWhiteSpace(hiddenCommandText))
                return null;

            return hiddenCommandText;
        }

        private static string BuildVisibleCommand(OverlayUpdateRequest request)
        {
            PixelRect bounds;
            if (!TryBuildBounds(request.Frame, out bounds))
                return null;

            string typeToken = BuildTypeToken(request);
            if (string.IsNullOrWhiteSpace(typeToken))
                return null;

            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} {1} {2} {3} {4} {5}",
                InBoardCommand,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                typeToken);
        }

        private static string BuildTypeToken(OverlayUpdateRequest request)
        {
            string baseToken = ResolveBaseToken(request);
            if (string.IsNullOrWhiteSpace(baseToken))
                return null;

            double scale = GetDpiScale(request == null ? null : request.Frame == null ? null : request.Frame.Window);
            if (scale <= DefaultDpiScale)
                return baseToken;

            return string.Concat(
                DpiTokenPrefix,
                scale.ToString(CultureInfo.CurrentCulture),
                DpiTokenSeparator,
                baseToken);
        }

        private static string ResolveBaseToken(OverlayUpdateRequest request)
        {
            if (request == null)
                return null;
            if (!string.IsNullOrWhiteSpace(request.LegacyTypeToken))
                return request.LegacyTypeToken;
            if (request.Frame == null)
                return null;

            return ((int)request.Frame.SyncMode).ToString(CultureInfo.CurrentCulture);
        }

        private static bool TryBuildBounds(BoardFrame frame, out PixelRect bounds)
        {
            bounds = null;
            if (frame == null || frame.Viewport == null)
                return false;

            return TryBuildBoundsFromWindow(frame, out bounds)
                || TryBuildBoundsFromScreen(frame, out bounds);
        }

        private static bool TryBuildBoundsFromWindow(BoardFrame frame, out PixelRect bounds)
        {
            bounds = null;
            WindowDescriptor window = frame.Window;
            PixelRect windowBounds = window == null ? null : window.Bounds;
            PixelRect sourceBounds = frame.Viewport.SourceBounds;
            if (windowBounds == null || windowBounds.IsEmpty || sourceBounds == null || sourceBounds.IsEmpty)
                return false;

            double scale = GetDpiScale(window);
            bounds = window.IsDpiAware
                ? CreateAwareBounds(windowBounds, sourceBounds, scale)
                : CreateUnawareBounds(windowBounds, sourceBounds, scale);
            return bounds != null && !bounds.IsEmpty;
        }

        private static bool TryBuildBoundsFromScreen(BoardFrame frame, out PixelRect bounds)
        {
            bounds = null;
            PixelRect screenBounds = frame.Viewport.ScreenBounds;
            if (screenBounds == null || screenBounds.IsEmpty)
                return false;

            double scale = GetDpiScale(frame.Window);
            bounds = new PixelRect(
                ScaleCoordinate(screenBounds.X, scale),
                ScaleCoordinate(screenBounds.Y, scale),
                ScaleSize(screenBounds.Width, scale),
                ScaleSize(screenBounds.Height, scale));
            return !bounds.IsEmpty;
        }

        private static PixelRect CreateAwareBounds(PixelRect windowBounds, PixelRect sourceBounds, double scale)
        {
            return new PixelRect(
                ScaleCoordinate(windowBounds.X + sourceBounds.X, scale),
                ScaleCoordinate(windowBounds.Y + sourceBounds.Y, scale),
                ScaleSize(sourceBounds.Width, scale),
                ScaleSize(sourceBounds.Height, scale));
        }

        private static PixelRect CreateUnawareBounds(PixelRect windowBounds, PixelRect sourceBounds, double scale)
        {
            return new PixelRect(
                sourceBounds.X + ScaleCoordinate(windowBounds.X, scale),
                sourceBounds.Y + ScaleCoordinate(windowBounds.Y, scale),
                sourceBounds.Width,
                sourceBounds.Height);
        }

        private static int ScaleCoordinate(int value, double scale)
        {
            if (scale <= DefaultDpiScale)
                return value;

            return (int)(value / scale);
        }

        private static int ScaleSize(int value, double scale)
        {
            if (scale <= DefaultDpiScale)
                return value;

            return (int)Math.Round(value / scale);
        }

        private static double GetDpiScale(WindowDescriptor window)
        {
            if (window == null || window.DpiScale <= 0d)
                return DefaultDpiScale;

            return window.DpiScale;
        }
    }
}
