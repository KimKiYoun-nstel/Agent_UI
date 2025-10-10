using System;
using System.IO;
using System.Formats.Cbor;
using System.Text.Json;

namespace Agent.UI.Wpf.Services
{
    /// <summary>
    /// CBOR 안에 JSON 바이트 포장
    /// </summary>
    public sealed class CborFrameCodec : IFrameCodec
    {
        public static Action<string>? LogAction;
        private static void Log(string msg) { if (LogAction != null) try { LogAction(msg); } catch { } }
        public byte[] EncodeReq(Req req)
        {
            using var js = new MemoryStream();
            JsonSerializer.Serialize(js, new { op = req.Op, target = req.Target, args = req.Args, data = req.Data, proto = req.Proto });
            js.Position = 0;

            var writer = new CborWriter();
            writer.WriteByteString(js.ToArray());
            return writer.Encode();
        }

        public bool TryDecode(ReadOnlySpan<byte> src, out Rsp? rsp, out Evt? evt)
        {
            rsp = null; evt = null;
            try
            {
                // CborReader expects ReadOnlyMemory/byte[] overload; convert incoming span to array
                var buf = src.ToArray();
                var reader = new CborReader(buf, CborConformanceMode.Strict);
                var payload = reader.ReadByteString();
                // payload is a byte[] (or ReadOnlyMemory<byte> depending on runtime) - ensure byte[] for JsonDocument
                var payloadBytes = payload is byte[] b ? b : payload.ToArray();
                using var doc = JsonDocument.Parse(payloadBytes);
                var root = doc.RootElement;

                if (root.TryGetProperty("ok", out var ok))
                {
                    rsp = new Rsp
                    {
                        Ok = ok.GetBoolean(),
                        Action = root.TryGetProperty("action", out var a) ? a.GetString() : null,
                        Data = root.TryGetProperty("data", out var d) ? JsonSerializer.Deserialize<object>(d.GetRawText()) : null,
                        Err = root.TryGetProperty("err", out var e) ? e.GetString() : null
                    };
                    return true;
                }
                if (root.TryGetProperty("kind", out var kind))
                {
                    evt = new Evt
                    {
                        Kind = kind.GetString() ?? "unknown",
                        Data = root.TryGetProperty("data", out var d) ? JsonSerializer.Deserialize<object>(d.GetRawText()) : null
                    };
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Log($"CborFrameCodec decode failed: {ex.Message}");
                return false;
            }
        }
    }
}
