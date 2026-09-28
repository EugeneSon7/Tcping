using System.Net;

namespace Tcping;

internal sealed class TcpingStatistics
{
    private double _totalMilliseconds;
    public int Sent { get; private set; }
    public int Successful { get; private set; }
    public int Failed => Sent - Successful;
    public double? MinimumMilliseconds { get; private set; }
    public double? MaximumMilliseconds { get; private set; }
    public IPAddress? LastAddress { get; private set; }

    public void Add(TcpProbeResult result)
    {
        Sent++;
        if (result.Address is not null) LastAddress = result.Address;
        if (!result.Success || result.Elapsed is not { } elapsed) return;
        var milliseconds = elapsed.TotalMilliseconds;
        Successful++;
        _totalMilliseconds += milliseconds;
        MinimumMilliseconds = MinimumMilliseconds is null ? milliseconds : Math.Min(MinimumMilliseconds.Value, milliseconds);
        MaximumMilliseconds = MaximumMilliseconds is null ? milliseconds : Math.Max(MaximumMilliseconds.Value, milliseconds);
    }

    public void WriteTo(OutputWriter output, string host, int port)
    {
        if (IPAddress.TryParse(host, out var address) &&
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            host = $"[{host}]";
        var failurePercent = Sent == 0 ? 0 : Failed * 100d / Sent;
        output.WriteLine($"Ping statistics for {host}:{port}");
        output.WriteLine($"     {Sent} probes sent.");
        output.WriteLine($"     {Successful} successful, {Failed} failed.  ({failurePercent:F2}% fail)");
        if (Successful == 0) return;
        output.WriteLine("Approximate trip times in milli-seconds:");
        output.WriteLine($"     Minimum = {MinimumMilliseconds:F3}ms, Maximum = {MaximumMilliseconds:F3}ms, Average = {_totalMilliseconds / Successful:F3}ms");
    }
}
