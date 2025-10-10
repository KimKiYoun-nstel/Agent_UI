namespace Agent.UI.Wpf.Services
{
    public interface IFrameCodec
    {
        byte[] EncodeReq(Req req);
        bool TryDecode(ReadOnlySpan<byte> src, out Rsp? rsp, out Evt? evt);
    }
}
