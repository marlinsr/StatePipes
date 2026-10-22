using System.Net;
using System.Net.Sockets;

namespace StatePipes.Test.OpcUaBridge
{
    /// <summary>
    /// Forwards TCP connections to a server until it is frozen. Frozen, it stops forwarding in both directions on
    /// every connection, old and new, without closing any: the peer goes silent, as one behind a dead network
    /// path does, and nothing on the client side sees a FIN or RST.
    /// </summary>
    internal sealed class FreezingTcpProxy : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly int _targetPort;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<TcpClient> _clients = [];
        private volatile bool _frozen;

        public FreezingTcpProxy(int targetPort)
        {
            _targetPort = targetPort;
            _listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int Connections { get; private set; }

        public void Freeze() => _frozen = true;

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    TcpClient server = new();
                    lock (_clients) _clients.AddRange([client, server]);
                    Connections++;
                    if (_frozen) continue; // accepted, never answered
                    await server.ConnectAsync(IPAddress.Loopback, _targetPort, _cts.Token);
                    _ = PumpAsync(client.GetStream(), server.GetStream());
                    _ = PumpAsync(server.GetStream(), client.GetStream());
                }
            }
            catch (Exception) { } // stopped
        }

        private async Task PumpAsync(NetworkStream from, NetworkStream to)
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (true)
                {
                    var read = await from.ReadAsync(buffer, _cts.Token);
                    if (read == 0) return;
                    if (_frozen) await Task.Delay(Timeout.Infinite, _cts.Token);
                    await to.WriteAsync(buffer.AsMemory(0, read), _cts.Token);
                }
            }
            catch (Exception) { } // stopped or closed
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            lock (_clients) foreach (var client in _clients) client.Dispose();
        }
    }
}
