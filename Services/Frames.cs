namespace Agent.UI.Wpf.Services
{
    /// <summary>요청 프레임</summary>
    public sealed class Req
    {
        public required string Op { get; init; }
        /// <summary>Target can be a string or structured object (e.g. { kind = "qos" })</summary>
        public required object Target { get; init; }
        public System.Collections.Generic.Dictionary<string, object?>? TargetExtra { get; init; }
        public object? Args { get; init; }
        public object? Data { get; init; }
        public int Proto { get; init; } = 1;
    }

    /// <summary>응답 프레임</summary>
    public sealed class Rsp
    {
        public bool Ok { get; init; }
        public string? Action { get; init; }
        public object? Data { get; init; }
        public string? Err { get; init; }
    }

    /// <summary>이벤트 프레임</summary>
    public sealed class Evt
    {
        public required string Kind { get; init; }
        public object? Data { get; init; }
    }
}
