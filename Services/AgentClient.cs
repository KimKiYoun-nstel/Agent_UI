using System;
using System.Threading;
using System.Threading.Tasks;

namespace Agent.UI.Wpf.Services
{
    /// <summary>요청/응답 + 이벤트(헤더+CBOR Map)</summary>
    public sealed class AgentClient : IAsyncDisposable
    {
        public static Action<string>? LogAction;
        private static void Log(string msg) { if (LogAction != null) try { LogAction(msg); } catch { } }

        private readonly ITransport  _tx;
        private readonly IFrameCodec _fx;

        // corr_id -> pending response TaskCompletionSource
        private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, TaskCompletionSource<Rsp>> _pendingMap
            = new();
        private uint _nextCorr = 1;

        public event Action<Evt>? EventReceived;

        public AgentClient(ITransport tx, IFrameCodec fx)
        {
            _tx = tx; _fx = fx;
            _tx.DatagramReceived += OnDatagram;
            Log("AgentClient created");
        }

        public Task ConnectAsync(string addr, int port, CancellationToken ct = default)
            => _tx.StartAsync(addr, port, ct);

        public Task DisconnectAsync(CancellationToken ct = default)
            => _tx.StopAsync(ct);

        public async Task<Rsp> RequestAsync(Req req, CancellationToken ct = default)
        {
            // 1) CBOR Map payload
            var payload = _fx.EncodeReq(req);

            // 2) 헤더(REQ)
            var corr = _nextCorr++;
            var header = WireHeader.CreateReq(corr, (uint)payload.Length, MonoTime.UtcNowNs());

            // 3) header + payload 합치기
            var buf = new byte[Wire.HeaderSize + payload.Length];
            WireHeader.Write(buf.AsSpan(0, Wire.HeaderSize), in header);
            Buffer.BlockCopy(payload, 0, buf, Wire.HeaderSize, payload.Length);

            // 4) create pending and send
            var tcs = new TaskCompletionSource<Rsp>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingMap.TryAdd(corr, tcs))
            {
                throw new InvalidOperationException("corr id collision");
            }

            try
            {
                Log($"AgentClient: send REQ corr={corr} len={payload.Length}");
                try
                {
                    await _tx.SendAsync(buf, ct);
                }
                catch (Exception ex)
                {
                    Log($"AgentClient: SendAsync failed: {ex}");
                    throw;
                }

                using var reg = ct.Register(() =>
                {
                    if (_pendingMap.TryRemove(corr, out var p)) p.TrySetCanceled();
                });

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Log($"AgentClient: RequestAsync exception for corr={corr}: {ex}");
                throw;
            }
            finally
            {
                // On normal completion tcs removed in OnDatagram; ensure cleanup on exception
                _pendingMap.TryRemove(corr, out _);
            }
        }

        private void OnDatagram(ReadOnlyMemory<byte> bytes)
        {
            var span = bytes.Span;
            if (span.Length < Wire.HeaderSize) return;

            // 1) 헤더 파싱/검증
            WireHeader h;
            try { h = WireHeader.Read(span[..Wire.HeaderSize]); }
            catch { Log("AgentClient: invalid header length"); return; }
            if (h.Magic != Wire.Magic || h.Ver != Wire.Ver) { Log("AgentClient: header magic/ver mismatch"); return; }
            if (h.Type != Wire.TRsp && h.Type != Wire.TEvt) { Log($"AgentClient: unsupported type={h.Type}"); return; }

            var len = (int)h.Length;
            if (Wire.HeaderSize + len > span.Length) { Log("AgentClient: truncated frame"); return; }

            var payload = span.Slice(Wire.HeaderSize, len);

            if (h.Type == Wire.TRsp)
            {
                if (_fx.TryDecode(payload, out var rsp, out _))
                {
                    Log($"AgentClient: got RSP corr={h.CorrId} ok={rsp?.Ok}");
                    if (_pendingMap.TryRemove(h.CorrId, out var tcs))
                    {
                        tcs.TrySetResult(rsp!);
                    }
                    else
                    {
                        Log($"AgentClient: unmatched RSP corr={h.CorrId} (dropped)");
                    }
                }
                else
                {
                    Log("AgentClient: decode failed for RSP");
                }
            }
            else // EVT
            {
                if (_fx.TryDecode(payload, out _, out var evt) && evt != null)
                {
                    Log($"AgentClient: got EVT kind={evt.Kind}");
                    EventReceived?.Invoke(evt);
                }
                else
                {
                    Log("AgentClient: decode failed for EVT");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _tx.DatagramReceived -= OnDatagram;
            }
            catch { }

            // Fail/Cancel any outstanding pending requests
            foreach (var kv in _pendingMap)
            {
                if (_pendingMap.TryRemove(kv.Key, out var tcs))
                {
                    try { tcs.TrySetCanceled(); } catch { }
                }
            }

            await _tx.DisposeAsync();
        }
    }
}
