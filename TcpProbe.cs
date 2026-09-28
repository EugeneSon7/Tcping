using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Tcping;

internal sealed class TcpProbe(AddressFamily? addressFamily, IPAddress? sourceAddress, TimeSpan timeout)
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var family = addressFamily ?? sourceAddress?.AddressFamily;
        var filtered = family is null ? addresses : addresses.Where(address => address.AddressFamily == family).ToArray();
        if (filtered.Length == 0) throw new SocketException((int)SocketError.HostNotFound);
        return filtered;
    }

    public async Task<TcpProbeResult> ConnectAsync(IReadOnlyList<IPAddress> addresses, int port,
        CancellationToken cancellationToken)
    {
        TcpProbeResult? lastFailure = null;
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                if (sourceAddress is not null)
                {
                    if (sourceAddress.AddressFamily != address.AddressFamily) continue;
                    socket.Bind(new IPEndPoint(sourceAddress, 0));
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, port), timeoutCts.Token);
                    stopwatch.Stop();
                    return new TcpProbeResult(ProbeStatus.Open, address, stopwatch.Elapsed, null);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    stopwatch.Stop();
                    lastFailure = TcpProbeResult.Failed(ProbeStatus.Timeout, "Connection timed out.", address);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (SocketException ex)
            {
                lastFailure = TcpProbeResult.Failed(MapSocketError(ex.SocketErrorCode), ex.Message, address);
            }
            catch (ArgumentException ex)
            {
                lastFailure = TcpProbeResult.Failed(ProbeStatus.InvalidAddress, ex.Message, address);
            }
        }

        return lastFailure ?? TcpProbeResult.Failed(ProbeStatus.InvalidAddress,
            "No compatible destination address was found.");
    }

    private static ProbeStatus MapSocketError(SocketError error) => error switch
    {
        SocketError.ConnectionRefused => ProbeStatus.ConnectionRefused,
        SocketError.TimedOut => ProbeStatus.Timeout,
        SocketError.NetworkUnreachable or SocketError.NetworkDown => ProbeStatus.NetworkUnreachable,
        SocketError.HostUnreachable or SocketError.HostDown => ProbeStatus.HostUnreachable,
        SocketError.AddressFamilyNotSupported or SocketError.AddressNotAvailable or SocketError.InvalidArgument => ProbeStatus.InvalidAddress,
        _ => ProbeStatus.ConnectionFailed
    };
}
