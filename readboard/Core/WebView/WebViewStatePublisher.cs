using System;

namespace readboard
{
    internal sealed class WebViewStatePublisher
    {
        private readonly Action publish;
        private int suppressionDepth;
        private int batchDepth;
        private bool publicationPending;

        public WebViewStatePublisher(Action publish)
        {
            this.publish = publish ?? throw new ArgumentNullException(nameof(publish));
        }

        public void Request()
        {
            if (suppressionDepth > 0)
                return;
            if (batchDepth > 0)
            {
                publicationPending = true;
                return;
            }
            publish();
        }

        public void Suppress(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            suppressionDepth++;
            try
            {
                action();
            }
            finally
            {
                suppressionDepth--;
            }
        }

        public T Suppress<T>(Func<T> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            suppressionDepth++;
            try
            {
                return action();
            }
            finally
            {
                suppressionDepth--;
            }
        }

        public void Batch(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            batchDepth++;
            try
            {
                action();
            }
            finally
            {
                batchDepth--;
                if (batchDepth == 0 && publicationPending)
                {
                    publicationPending = false;
                    publish();
                }
            }
        }

        public bool Dispatch(Func<bool> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            bool handled = false;
            Batch(delegate
            {
                handled = handler();
                if (handled)
                    Request();
            });
            return handled;
        }
    }
}
