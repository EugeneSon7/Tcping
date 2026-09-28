using System.Net;
using System.Globalization;

namespace Tcping;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var parseResult = CommandLineOptions.Parse(args);
        if (!parseResult.Success)
        {
            Console.Error.WriteLine(parseResult.Error);
            Console.Error.WriteLine("Use --help for usage information.");
            return 2;
        }

        var options = parseResult.Options!;
        if (options.ShowHelp) { Console.WriteLine(CommandLineOptions.HelpText); return 0; }
        if (options.ShowVersion) { Console.WriteLine($"tcping {CommandLineOptions.Version}"); return 0; }

        IReadOnlyList<TcpTarget> targets;
        try
        {
            targets = options.FileName is not null
                ? FileTargetReader.Read(options.FileName, options.PortOverride)
                : [new TcpTarget(options.Host!, options.PortOverride ?? options.PositionalPort ?? 80)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }

        using var output = new OutputWriter(options.TeeFile, options.Append);
        using var shutdown = new CancellationTokenSource();
        var controlCReceived = false;
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            if (controlCReceived) return;
            controlCReceived = true;
            output.WriteLine("Control-C");
            shutdown.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            var allTargetsSucceeded = true;
            foreach (var target in targets)
            {
                if (shutdown.IsCancellationRequested) break;
                var succeeded = await RunTargetAsync(target, options, output, shutdown.Token);
                allTargetsSucceeded &= succeeded;
                if (options.StopOnSuccess && succeeded) break;
            }
            return allTargetsSucceeded ? 0 : 1;
        }
        finally { Console.CancelKeyPress -= cancelHandler; }
    }

    private static async Task<bool> RunTargetAsync(TcpTarget target, CommandLineOptions options,
        OutputWriter output, CancellationToken cancellationToken)
    {
        var probe = new TcpProbe(options.AddressFamily, options.SourceAddress, options.Timeout);
        var statistics = new TcpingStatistics();
        var jitter = new JitterCalculator(options.JitterWindowSize);
        IReadOnlyList<IPAddress>? addresses = null;
        string? reverseName = null;
        bool? previousOpenState = null;
        var consecutiveFailures = 0;
        var probeNumber = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested && (options.Continuous || probeNumber < options.Count))
            {
                probeNumber++;
                if (addresses is null ||
                    (options.ResolveEvery > 0 && probeNumber > 1 && (probeNumber - 1) % options.ResolveEvery == 0))
                {
                    try
                    {
                        addresses = await probe.ResolveAsync(target.Host, cancellationToken);
                        reverseName = options.IncludeFqdn
                            ? await TryGetHostNameAsync(addresses[0], cancellationToken)
                            : null;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                    catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
                    {
                        addresses = null;
                        reverseName = null;
                        var status = IPAddress.TryParse(target.Host, out _)
                            ? ProbeStatus.InvalidAddress
                            : ProbeStatus.DnsResolutionFailed;
                        var failed = TcpProbeResult.Failed(status, ex.Message);
                        RecordAndWrite(failed, target, null, reverseName, options, statistics, jitter,
                            output, ref previousOpenState, ref consecutiveFailures);
                        if (ShouldStop(options, failed, consecutiveFailures)) break;
                        if (!await DelayBeforeNextProbeAsync(options, probeNumber, cancellationToken)) break;
                        continue;
                    }
                }

                TcpProbeResult result;
                try { result = await probe.ConnectAsync(addresses, target.Port, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }

                RecordAndWrite(result, target, result.Address, reverseName, options, statistics, jitter,
                    output, ref previousOpenState, ref consecutiveFailures);
                if (ShouldStop(options, result, consecutiveFailures)) break;
                if (!await DelayBeforeNextProbeAsync(options, probeNumber, cancellationToken)) break;
            }
        }
        finally
        {
            output.WriteLine();
            statistics.WriteTo(output, statistics.LastAddress?.ToString() ?? target.Host, target.Port);
        }

        return statistics.Successful > 0;
    }

    private static void RecordAndWrite(TcpProbeResult result, TcpTarget target, IPAddress? address,
        string? reverseName, CommandLineOptions options, TcpingStatistics statistics,
        JitterCalculator jitter, OutputWriter output, ref bool? previousOpenState,
        ref int consecutiveFailures)
    {
        statistics.Add(result);
        consecutiveFailures = result.Success ? 0 : consecutiveFailures + 1;
        double? jitterValue = result.Success && result.Elapsed is { } elapsed
            ? jitter.Add(elapsed.TotalMilliseconds) : null;

        var stateChanged = previousOpenState is null || previousOpenState != result.Success;
        previousOpenState = result.Success;
        if (options.ChangesOnly && !stateChanged) return;

        var endpointAddress = address;
        if (endpointAddress is null) IPAddress.TryParse(target.Host, out endpointAddress);
        var endpoint = endpointAddress?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{endpointAddress}]"
            : address?.ToString() ?? target.Host;
        if (options.IncludeFqdn && address is not null && !string.IsNullOrWhiteSpace(reverseName) &&
            !string.Equals(reverseName, endpoint, StringComparison.OrdinalIgnoreCase))
            endpoint = $"{reverseName} [{address}]";

        var prefix = options.IncludeDate ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " : string.Empty;
        var status = result.Status switch
        {
            ProbeStatus.Open => "Port is open",
            ProbeStatus.ConnectionRefused => "Connection refused",
            ProbeStatus.Timeout => "Connection timed out",
            ProbeStatus.DnsResolutionFailed => "DNS resolution failed",
            ProbeStatus.NetworkUnreachable => "Network unreachable",
            ProbeStatus.HostUnreachable => "Host unreachable",
            ProbeStatus.InvalidAddress => "Invalid address",
            _ => "Connection failed"
        };
        var timing = result.Elapsed is { } time ? $" - time={time.TotalMilliseconds:F3}ms" : string.Empty;
        var jitterText = options.ShowJitter && jitterValue is { } value ? $" - jitter={value:F3}ms" : string.Empty;
        output.WriteLine($"{prefix}Probing {endpoint}:{target.Port}/tcp - {status}{timing}{jitterText}");
    }

    private static bool ShouldStop(CommandLineOptions options, TcpProbeResult result, int failures) =>
        (options.StopOnSuccess && result.Success) ||
        (options.MaxConsecutiveFailures > 0 && failures >= options.MaxConsecutiveFailures);

    private static async Task<bool> DelayBeforeNextProbeAsync(CommandLineOptions options, int completed,
        CancellationToken cancellationToken)
    {
        if ((!options.Continuous && completed >= options.Count) || options.Interval <= TimeSpan.Zero) return true;
        try { await Task.Delay(options.Interval, cancellationToken); return true; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
    }

    private static async Task<string?> TryGetHostNameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try { return (await Dns.GetHostEntryAsync(address).WaitAsync(cancellationToken)).HostName; }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException) { return null; }
    }
}
