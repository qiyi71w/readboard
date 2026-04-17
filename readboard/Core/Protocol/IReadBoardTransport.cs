using System;

namespace readboard
{
    internal interface IReadBoardTransport : IDisposable
    {
        event EventHandler<string> MessageReceived;

        bool IsConnected { get; }

        void Start();
        void Stop();
        void Send(string line);
        void SendError(string message);
    }
}
