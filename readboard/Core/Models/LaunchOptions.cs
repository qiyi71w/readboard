using System;

namespace readboard
{
    internal sealed class LaunchOptions
    {
        public string AiTime { get; set; }
        public string Playouts { get; set; }
        public string FirstPolicy { get; set; }
        public TransportKind TransportKind { get; set; }
        public string Language { get; set; }
        public int TcpPort { get; set; }

        public static bool TryParse(string[] args, out LaunchOptions options)
        {
            options = null;
            if (args == null || args.Length < 7 || !string.Equals(args[0], "yzy", StringComparison.Ordinal))
                return false;

            int tcpPort;
            int.TryParse(args[6], out tcpPort);
            options = new LaunchOptions
            {
                AiTime = args[1],
                Playouts = args[2],
                FirstPolicy = args[3],
                TransportKind = args[4] == "1" ? TransportKind.Tcp : TransportKind.Pipe,
                Language = string.IsNullOrWhiteSpace(args[5]) ? "cn" : args[5],
                TcpPort = tcpPort
            };
            return true;
        }
    }
}
