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
            if (scale <= DisplayScaling.DefaultScale)
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

            if (frame.SyncMode == SyncMode.Background)
                return TryBuildBoundsFromScreen(frame, out bounds)
                    || TryBuildBoundsFromWindow(frame, out bounds);

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

            bounds = DisplayScaling.ProtocolBoundsFromWindow(windowBounds, sourceBounds, window.IsDpiAware, GetDpiScale(window));
            return bounds != null && !bounds.IsEmpty;
        }

        private static bool TryBuildBoundsFromScreen(BoardFrame frame, out PixelRect bounds)
        {
            bounds = null;
            PixelRect screenBounds = frame.Viewport.ScreenBounds;
            if (screenBounds == null || screenBounds.IsEmpty)
                return false;

            bounds = DisplayScaling.ProtocolBoundsFromScreen(screenBounds, GetDpiScale(frame.Window));
            return !bounds.IsEmpty;
        }

        private static double GetDpiScale(WindowDescriptor window)
        {
            return window == null ? DisplayScaling.DefaultScale : DisplayScaling.NormalizeScale(window.DpiScale);
        }
    }
}
