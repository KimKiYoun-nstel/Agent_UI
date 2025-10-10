using System;
using System.Buffers.Binary;

namespace Agent.UI.Wpf.Services
{
    /// <summary>RIPC 상수</summary>
    public static class Wire
    {
        public const uint  Magic      = 0x52495043;   // 'RIPC'
        public const ushort Ver       = 0x0001;
        public const ushort TReq      = 0x1000;
        public const ushort TRsp      = 0x1001;
        public const ushort TEvt      = 0x1002;
        public const int    HeaderSize= 4+2+2+4+4+8;  // 24 bytes
    }

    /// <summary>RIPC 헤더(네트워크 바이트오더)</summary>
    public readonly struct WireHeader
    {
        public readonly uint  Magic;
        public readonly ushort Ver;
        public readonly ushort Type;
        public readonly uint  CorrId;
        public readonly uint  Length;
        public readonly ulong TsNs;

        public WireHeader(uint magic, ushort ver, ushort type, uint corrId, uint length, ulong tsNs)
        { Magic=magic; Ver=ver; Type=type; CorrId=corrId; Length=length; TsNs=tsNs; }

        public static WireHeader CreateReq(uint corr, uint len, ulong ts)
            => new(Wire.Magic, Wire.Ver, Wire.TReq, corr, len, ts);
        public static WireHeader CreateRsp(uint corr, uint len, ulong ts)
            => new(Wire.Magic, Wire.Ver, Wire.TRsp, corr, len, ts);
        public static WireHeader CreateEvt(uint corr, uint len, ulong ts)
            => new(Wire.Magic, Wire.Ver, Wire.TEvt, corr, len, ts);

        public static void Write(Span<byte> dst, in WireHeader h)
        {
            if (dst.Length < Wire.HeaderSize) throw new ArgumentException("header too small");
            BinaryPrimitives.WriteUInt32BigEndian(dst[0..4],   h.Magic);
            BinaryPrimitives.WriteUInt16BigEndian(dst[4..6],   h.Ver);
            BinaryPrimitives.WriteUInt16BigEndian(dst[6..8],   h.Type);
            BinaryPrimitives.WriteUInt32BigEndian(dst[8..12],  h.CorrId);
            BinaryPrimitives.WriteUInt32BigEndian(dst[12..16], h.Length);
            BinaryPrimitives.WriteUInt64BigEndian(dst[16..24], h.TsNs);
        }

        public static WireHeader Read(ReadOnlySpan<byte> src)
        {
            if (src.Length < Wire.HeaderSize) throw new ArgumentException("header too small");
            var magic  = BinaryPrimitives.ReadUInt32BigEndian(src[0..4]);
            var ver    = BinaryPrimitives.ReadUInt16BigEndian(src[4..6]);
            var type   = BinaryPrimitives.ReadUInt16BigEndian(src[6..8]);
            var corrId = BinaryPrimitives.ReadUInt32BigEndian(src[8..12]);
            var len    = BinaryPrimitives.ReadUInt32BigEndian(src[12..16]);
            var ts     = BinaryPrimitives.ReadUInt64BigEndian(src[16..24]);
            return new WireHeader(magic, ver, type, corrId, len, ts);
        }
    }

    public static class MonoTime
    {
        /// <summary>UTC now → ns (진단용)</summary>
        public static ulong UtcNowNs()
        {
            var now = DateTimeOffset.UtcNow;
            return (ulong)now.ToUnixTimeMilliseconds() * 1_000_000UL;
        }
    }
}
