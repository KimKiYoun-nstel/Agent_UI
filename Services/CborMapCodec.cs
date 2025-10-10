using System;
using System.Formats.Cbor;
using System.Linq;
using System.Text.Json;

namespace Agent.UI.Wpf.Services
{
    /// <summary>CBOR Map 코덱 (REQ 인코딩, RSP/EVT 디코딩)</summary>
    public sealed class CborMapCodec : IFrameCodec
    {
        public static Action<string>? LogAction;
        private static void Log(string msg) { if (LogAction != null) try { LogAction(msg); } catch { } }

        public byte[] EncodeReq(Req req)
        {
            try
            {
                var w = new CborWriter(CborConformanceMode.Canonical);

                // 고정 키 5개(op/target/args/data/proto)
                w.WriteStartMap(5);

                w.WriteTextString("op");     w.WriteTextString(req.Op ?? "");
                // Encode target as a map: { "kind": req.Target, ...TargetExtra }
                w.WriteTextString("target");
                if (req.TargetExtra is { } extra && extra.Count > 0)
                {
                    w.WriteStartMap(1 + extra.Count);
                    w.WriteTextString("kind"); w.WriteTextString(req.Target ?? "");
                    foreach (var kv in extra)
                    {
                        w.WriteTextString(kv.Key);
                        WriteJsonAsCbor(w, kv.Value);
                    }
                    w.WriteEndMap();
                }
                else
                {
                    w.WriteStartMap(1);
                    w.WriteTextString("kind"); w.WriteTextString(req.Target ?? "");
                    w.WriteEndMap();
                }

                w.WriteTextString("args");   WriteJsonAsCbor(w, req.Args);
                w.WriteTextString("data");   WriteJsonAsCbor(w, req.Data);

                w.WriteTextString("proto");  w.WriteInt32(req.Proto);

                w.WriteEndMap();
                var enc = w.Encode();
                Log($"CborMapCodec: EncodeReq op={req.Op} proto={req.Proto} -> {enc.Length} bytes");
                return enc;
            }
            catch (Exception ex)
            {
                try
                {
                    var preview = req?.Op + ":" + (req?.Target ?? "") + " args=" + (req?.Args == null ? "null" : string.Join(',', req.Args));
                    Log($"CborMapCodec: EncodeReq exception preview={preview} ex={ex}");
                }
                catch { Log($"CborMapCodec: EncodeReq exception ex={ex}"); }
                throw;
            }
        }

        public bool TryDecode(ReadOnlySpan<byte> src, out Rsp? rsp, out Evt? evt)
        {
            rsp=null; evt=null;
            try
            {
                var buf = src.ToArray();
                // Use Lax mode for decoding to tolerate non-canonical key ordering from peers
                var r = new CborReader(new ReadOnlyMemory<byte>(buf), CborConformanceMode.Lax);
                var mapLen = r.ReadStartMap();

                bool hasOk=false, hasEvt=false;
                bool ok=false; string? action=null; object? result=null; string? errStr=null;
                string? evtKind=null, topic=null, type=null; object? display=null;

                for (int i=0; i<(mapLen ?? int.MaxValue); i++)
                {
                    if (r.PeekState() == CborReaderState.EndMap) break;
                    var key = r.ReadTextString();
                    switch (key)
                    {
                        case "ok":     ok = r.ReadBoolean(); hasOk = true; break;
                        case "action": action = r.ReadTextString(); break;
                        case "result": result = ReadNodeAsObject(r); break;
                        case "err":    errStr = NodeToString(r); break;

                        case "evt":    evtKind = r.ReadTextString(); hasEvt = true; break;
                        case "topic":  topic = r.ReadTextString(); break;
                        case "type":   type = r.ReadTextString(); break;
                        case "display":display = ReadNodeAsObject(r); break;

                        default: SkipAny(r); break;
                    }
                }
                r.ReadEndMap();

                if (hasOk)
                {
                    rsp = new Rsp { Ok = ok, Action = action, Data = result, Err = errStr };
                    return true;
                }
                if (hasEvt)
                {
                    evt = new Evt { Kind = evtKind ?? "unknown", Data = new { topic, type, display } };
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                try
                {
                    var len = Math.Min(128, src.Length);
                    var preview = BitConverter.ToString(src.Slice(0, len).ToArray()).Replace('-', ' ');
                    Log($"CborMapCodec: TryDecode failed len={src.Length} preview={preview} ex={ex}");
                }
                catch { Log($"CborMapCodec: TryDecode failed ex={ex}"); }
                return false;
            }
        }

        // ---------- JSON(object) → CBOR ----------
        private static void WriteJsonAsCbor(CborWriter w, object? value)
        {
            if (value is null) { w.WriteNull(); return; }
            var raw = JsonSerializer.SerializeToUtf8Bytes(value);
            using var doc = JsonDocument.Parse(raw);
            WriteElem(w, doc.RootElement);
        }

        private static void WriteElem(CborWriter w, JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    var props = e.EnumerateObject().ToArray();
                    w.WriteStartMap(props.Length);
                    foreach (var p in props)
                    {
                        w.WriteTextString(p.Name);
                        WriteElem(w, p.Value);
                    }
                    w.WriteEndMap();
                    break;

                case JsonValueKind.Array:
                    var arr = e.EnumerateArray().ToArray();
                    w.WriteStartArray(arr.Length);
                    foreach (var it in arr) WriteElem(w, it);
                    w.WriteEndArray();
                    break;

                case JsonValueKind.String: w.WriteTextString(e.GetString() ?? ""); break;
                case JsonValueKind.Number:
                    if (e.TryGetInt64(out var i64)) w.WriteInt64(i64);
                    else if (e.TryGetDouble(out var d)) w.WriteDouble(d);
                    else w.WriteTextString(e.GetRawText());
                    break;
                case JsonValueKind.True:  w.WriteBoolean(true); break;
                case JsonValueKind.False: w.WriteBoolean(false); break;
                case JsonValueKind.Null:  w.WriteNull(); break;
                default: w.WriteNull(); break;
            }
        }

        // ---------- CBOR → System.Object ----------
        private static object? ReadNodeAsObject(CborReader r)
        {
            var node = ReadElem(r);
            var json = JsonSerializer.Serialize(node);
            return JsonSerializer.Deserialize<object>(json);
        }

        private static string? NodeToString(CborReader r)
        {
            var node = ReadElem(r);
            return node?.ToString();
        }

        private static object? ReadElem(CborReader r)
        {
            switch (r.PeekState())
            {
                case CborReaderState.Null: r.ReadNull(); return null;
                case CborReaderState.Boolean: return r.ReadBoolean();
                case CborReaderState.TextString: return r.ReadTextString();
                case CborReaderState.UnsignedInteger: return (long)r.ReadUInt64();
                case CborReaderState.NegativeInteger: return r.ReadInt64();
                case CborReaderState.SinglePrecisionFloat: return r.ReadSingle();
                case CborReaderState.DoublePrecisionFloat: return r.ReadDouble();

                case CborReaderState.StartArray:
                {
                    var len = r.ReadStartArray();
                    var list = new System.Collections.Generic.List<object?>();
                    for (int i=0; i<(len ?? int.MaxValue); i++)
                    {
                        if (r.PeekState()==CborReaderState.EndArray) break;
                        list.Add(ReadElem(r));
                    }
                    r.ReadEndArray();
                    return list;
                }
                case CborReaderState.StartMap:
                {
                    var len = r.ReadStartMap();
                    var dict = new System.Collections.Generic.Dictionary<string, object?>();
                    for (int i=0; i<(len ?? int.MaxValue); i++)
                    {
                        if (r.PeekState()==CborReaderState.EndMap) break;
                        var k = r.ReadTextString();
                        dict[k] = ReadElem(r);
                    }
                    r.ReadEndMap();
                    return dict;
                }
                default:
                    // 알 수 없는 타입은 스킵
                    SkipAny(r); return null;
            }
        }

        private static void SkipAny(CborReader r)
        {
            switch (r.PeekState())
            {
                case CborReaderState.StartArray:
                    r.ReadStartArray(); while (r.PeekState()!=CborReaderState.EndArray) SkipAny(r); r.ReadEndArray(); break;
                case CborReaderState.StartMap:
                    r.ReadStartMap(); while (r.PeekState()!=CborReaderState.EndMap) { r.ReadTextString(); SkipAny(r); } r.ReadEndMap(); break;
                default: ReadElem(r); break;
            }
        }
    }
}
