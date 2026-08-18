using System;

namespace readboard
{
    internal enum WebViewWindowIntent
    {
        Minimize = 0,
        ToggleMaximize = 1,
        Close = 2
    }

    internal enum WebViewWindowState
    {
        Normal = 0,
        Minimized = 1,
        Maximized = 2
    }

    internal interface IWebViewWindowAdapter
    {
        WebViewWindowState State { get; }
        void SetState(WebViewWindowState state);
        void Close();
    }

    internal sealed class WebViewWindowCommandRuntime
    {
        private readonly IWebViewWindowAdapter adapter;

        public WebViewWindowCommandRuntime(IWebViewWindowAdapter adapter)
        {
            this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public bool Apply(WebViewWindowIntent intent)
        {
            switch (intent)
            {
                case WebViewWindowIntent.Minimize:
                    return SetState(WebViewWindowState.Minimized);
                case WebViewWindowIntent.ToggleMaximize:
                    return SetState(
                        adapter.State == WebViewWindowState.Maximized
                            ? WebViewWindowState.Normal
                            : WebViewWindowState.Maximized);
                case WebViewWindowIntent.Close:
                    adapter.Close();
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent));
            }
        }

        public static bool TryCreateIntent(
            ReadBoardUiCommand command,
            out WebViewWindowIntent intent)
        {
            intent = default;
            if (command == null)
                return false;

            switch (command.Type)
            {
                case "window.minimize":
                    intent = WebViewWindowIntent.Minimize;
                    return true;
                case "window.maximize":
                    intent = WebViewWindowIntent.ToggleMaximize;
                    return true;
                case "window.close":
                    intent = WebViewWindowIntent.Close;
                    return true;
                default:
                    return false;
            }
        }

        private bool SetState(WebViewWindowState state)
        {
            if (adapter.State == state)
                return false;

            adapter.SetState(state);
            return true;
        }
    }
}
