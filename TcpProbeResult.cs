using System.Net;

namespace Tcping;

internal enum ProbeStatus
{
    Open,
    ConnectionRefused,
    Timeout,
    DnsResolutionFailed,
    NetworkUnreachable,
    HostUnreachable,
    InvalidAddress,
    ConnectionFailed
}

internal sealed record TcpProbeResult(ProbeStatus Status, IPAddress? Address, TimeSpan? Elapsed, string? Error)
{
    public bool Success => Status == ProbeStatus.Open;
    public static TcpProbeResult Failed(ProbeStatus status, string? error, IPAddress? address = null) =>
        new(status, address, null, error);
}
