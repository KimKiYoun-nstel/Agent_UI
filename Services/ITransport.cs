using System;
using System.Threading;
using System.Threading.Tasks;

namespace Agent.UI.Wpf.Services
{
    /// <summary>
    /// UDP 기반 전송 인터페이스
    /// </summary>
    public interface ITransport : IAsyncDisposable
    {
        /// <summary>수신 바이트 이벤트</summary>
        event Action<ReadOnlyMemory<byte>>? DatagramReceived;

        /// <summary>전송 시작 (원격 address:port 지정)</summary>
        Task StartAsync(string address, int port, CancellationToken ct = default);

        /// <summary>전송</summary>
        Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default);

        /// <summary>정지/종료</summary>
        Task StopAsync(CancellationToken ct = default);
    }
}
