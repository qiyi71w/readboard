using System;

namespace readboard
{
    internal interface IReadBoardTransport : IDisposable
    {
        event EventHandler<string> MessageReceived;
        // Unexpected EOF or I/O failure; raised once after becoming disconnected, outside transport locks.
        // Explicit Stop/Dispose does not raise this event.
        event EventHandler Disconnected;

        bool IsConnected { get; }

        void Start();
        void Stop();
        void Send(string line);
        void SendError(string message);
    }
}
