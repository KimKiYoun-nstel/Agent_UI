using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Agent.UI.Wpf.Services
{
    /// <summary>
    /// UdpClient 래퍼
    /// </summary>
    public sealed class UdpTransport : ITransport
    {
        public static Action<string>? LogAction;

        private static void Log(string msg)
        {
            if (LogAction != null) try { LogAction(msg); } catch { }
        }
        public event Action<ReadOnlyMemory<byte>>? DatagramReceived;

        private UdpClient? _udp;
        private IPEndPoint? _remote;
        private CancellationTokenSource? _rxCts;

        // (선택) 로컬 포트 고정 옵션
        public int? LocalPort { get; set; } = null;

        public Task StartAsync(string address, int port, CancellationToken ct = default)
        {
            // 1) IPv4 주소 선택
            var addrs = System.Net.Dns.GetHostAddresses(address);
            var ipv4  = addrs.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (ipv4 == default) throw new InvalidOperationException("IPv4 address required");
            _remote = new IPEndPoint(ipv4, port);

            // 2) IPv4 소켓 생성 + 옵션
            _udp = new UdpClient(System.Net.Sockets.AddressFamily.InterNetwork);
            _udp.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReuseAddress, true);

            // 3) 로컬 바인드(임의 또는 지정)
            var local = new IPEndPoint(System.Net.IPAddress.Any, LocalPort ?? 0);
            try
            {
                _udp.Client.Bind(local);
                Services.Logger.Info($"UdpTransport: bound to local {local}");
            }
            catch (Exception ex)
            {
                Services.Logger.Info($"UdpTransport: local bind failed local={local} ex={ex}");
                throw;
            }

            // 4) 목적지 고정(Connect after bind to avoid Windows WSAEINVAL)
            try
            {
                _udp.Connect(_remote);
            }
            catch (Exception ex)
            {
                Services.Logger.Info($"UdpTransport: connect failed remote={_remote} ex={ex}");
                throw;
            }

            // 5) 수신 루프 시작
            _rxCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = ReceiveLoop(_rxCts.Token);
            Services.Logger.Info($"UdpTransport started: remote={address}:{port} localPort={(LocalPort.HasValue?LocalPort.Value:0)}");
            return Task.CompletedTask;
        }

        public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
        {
            if (_udp == null) throw new InvalidOperationException("Transport not started");
            // Log only summary at TRACE level; do not emit raw/base64 payload
            Services.Logger.Trace($"UdpTransport SEND len={payload.Length}");
            await _udp.SendAsync(payload.ToArray(), payload.Length);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            try { _rxCts?.Cancel(); } catch { /* ignore */ }
            _udp?.Dispose();
            _udp = null; _remote = null;
            _rxCts?.Dispose(); _rxCts = null;
            Services.Logger.Info("UdpTransport stopped");
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync() => await StopAsync();

        private async Task ReceiveLoop(CancellationToken ct)
        {
            if (_udp == null) return;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var res = await _udp.ReceiveAsync(ct);
                    // Only log summary at TRACE level; actual payload not emitted to avoid clutter
                    Services.Logger.Trace($"UdpTransport RECV len={res.Buffer?.Length ?? 0}");
                    DatagramReceived?.Invoke(res.Buffer);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log($"UdpTransport receive error: {ex.Message}"); }
            }
        }
    }
}
