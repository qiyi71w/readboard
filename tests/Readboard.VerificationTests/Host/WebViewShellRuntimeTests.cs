using System;
using System.Collections.Generic;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class WebViewShellRuntimeTests
    {
        [Fact]
        public void StatePublisher_CoalescesHandlerAndNestedPublicationRequests()
        {
            int publicationCount = 0;
            var publisher = new WebViewStatePublisher(() => publicationCount++);

            bool handled = publisher.Dispatch(delegate
            {
                publisher.Request();
                publisher.Request();
                return true;
            });

            Assert.True(handled);
            Assert.Equal(1, publicationCount);
        }

        [Fact]
        public void StatePublisher_PreservesHandlerPublicationWhenNoSnapshotIsRequested()
        {
            int publicationCount = 0;
            var publisher = new WebViewStatePublisher(() => publicationCount++);

            bool handled = publisher.Dispatch(delegate
            {
                publisher.Request();
                return false;
            });

            Assert.False(handled);
            Assert.Equal(1, publicationCount);
        }

        [Fact]
        public void StatePublisher_DiscardsSuppressedRequestsAndSkipsNoOpDispatch()
        {
            int publicationCount = 0;
            var publisher = new WebViewStatePublisher(() => publicationCount++);

            publisher.Suppress(delegate
            {
                publisher.Request();
                publisher.Request();
            });
            bool handled = publisher.Dispatch(() => false);

            Assert.False(handled);
            Assert.Equal(0, publicationCount);
        }

        [Theory]
        [InlineData("{\"type\":\"window.minimize\"}", "Minimize")]
        [InlineData("{\"type\":\"window.maximize\",\"payload\":{}}", "ToggleMaximize")]
        [InlineData("{\"type\":\"window.close\"}", "Close")]
        public void StrictWindowJson_IsConvertedToTypedIntent(
            string json,
            string expected)
        {
            Assert.True(MainForm.TryParseWebViewCommand(json, out ReadBoardUiCommand command));

            Assert.True(WebViewWindowCommandRuntime.TryCreateIntent(command, out WebViewWindowIntent actual));
            Assert.Equal(expected, actual.ToString());
        }

        [Fact]
        public void WindowRuntime_AppliesStateTransitionsAndCloseLifecycle()
        {
            var adapter = new RecordingWindowAdapter();
            var runtime = new WebViewWindowCommandRuntime(adapter);

            Assert.True(runtime.Apply(WebViewWindowIntent.Minimize));
            Assert.Equal(WebViewWindowState.Minimized, adapter.State);
            Assert.Equal(1, adapter.SetCount);

            Assert.False(runtime.Apply(WebViewWindowIntent.Minimize));
            Assert.Equal(1, adapter.SetCount);

            Assert.True(runtime.Apply(WebViewWindowIntent.ToggleMaximize));
            Assert.Equal(WebViewWindowState.Maximized, adapter.State);
            Assert.True(runtime.Apply(WebViewWindowIntent.ToggleMaximize));
            Assert.Equal(WebViewWindowState.Normal, adapter.State);
            Assert.Equal(3, adapter.SetCount);

            Assert.False(runtime.Apply(WebViewWindowIntent.Close));
            Assert.Equal(1, adapter.CloseCount);
        }

        private sealed class RecordingWindowAdapter : IWebViewWindowAdapter
        {
            public WebViewWindowState State { get; private set; }
            public int SetCount { get; private set; }
            public int CloseCount { get; private set; }

            public void SetState(WebViewWindowState state)
            {
                State = state;
                SetCount++;
            }

            public void Close()
            {
                CloseCount++;
            }
        }
    }
}
