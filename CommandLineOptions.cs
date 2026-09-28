using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace Tcping;

internal sealed class CommandLineOptions
{
    public bool Continuous { get; private set; }
    public int Count { get; private set; } = 4;
    public TimeSpan Interval { get; private set; } = TimeSpan.FromSeconds(1);
    public TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(2);
    public bool IncludeDate { get; private set; }
    public int ResolveEvery { get; private set; }
    public bool StopOnSuccess { get; private set; }
    public bool ShowVersion { get; private set; }
    public AddressFamily? AddressFamily { get; private set; }
    public bool ChangesOnly { get; private set; }
    public int MaxConsecutiveFailures { get; private set; }
    public IPAddress? SourceAddress { get; private set; }
    public int? PortOverride { get; private set; }
    public bool ShowJitter { get; private set; }
    public int JitterWindowSize { get; private set; } = 5;
    public bool IncludeFqdn { get; private set; }
    public string? TeeFile { get; private set; }
    public bool Append { get; private set; }
    public string? FileName { get; private set; }
    public bool ShowHelp { get; private set; }
    public string? Host { get; private set; }
    public int? PositionalPort { get; private set; }

    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static ParseResult Parse(string[] args)
    {
        var result = new CommandLineOptions();
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "/?": case "-?": case "--help": result.ShowHelp = true; break;
                case "-v": case "--version": result.ShowVersion = true; break;
                case "-t": result.Continuous = true; break;
                case "-d": result.IncludeDate = true; break;
                case "-s": result.StopOnSuccess = true; break;
                case "-c": result.ChangesOnly = true; break;
                case "-j": result.ShowJitter = true; break;
                case "--fqdn": result.IncludeFqdn = true; break;
                case "--append": result.Append = true; break;
                case "-4":
                    if (result.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        return ParseResult.Fail("Options -4 and -6 cannot be used together.");
                    result.AddressFamily = System.Net.Sockets.AddressFamily.InterNetwork;
                    break;
                case "-6":
                    if (result.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ParseResult.Fail("Options -4 and -6 cannot be used together.");
                    result.AddressFamily = System.Net.Sockets.AddressFamily.InterNetworkV6;
                    break;
                case "-n":
                    if (!TryNext(args, ref i, arg, out var countText, out var countError)) return ParseResult.Fail(countError!);
                    if (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
                        return ParseResult.Fail("Probe count must be a positive integer.");
                    result.Count = count;
                    break;
                case "-i":
                    if (!TryReadSeconds(args, ref i, arg, true, out var interval, out var intervalError)) return ParseResult.Fail(intervalError!);
                    result.Interval = interval;
                    break;
                case "-w":
                    if (!TryReadSeconds(args, ref i, arg, false, out var timeout, out var timeoutError)) return ParseResult.Fail(timeoutError!);
                    result.Timeout = timeout;
                    break;
                case "-r":
                    if (!TryNext(args, ref i, arg, out var resolveText, out var resolveError)) return ParseResult.Fail(resolveError!);
                    if (!int.TryParse(resolveText, NumberStyles.None, CultureInfo.InvariantCulture, out var resolveEvery) || resolveEvery <= 0)
                        return ParseResult.Fail("DNS refresh interval must be a positive integer.");
                    result.ResolveEvery = resolveEvery;
                    break;
                case "-g":
                    if (!TryNext(args, ref i, arg, out var failuresText, out var failuresError)) return ParseResult.Fail(failuresError!);
                    if (!int.TryParse(failuresText, NumberStyles.None, CultureInfo.InvariantCulture, out var maxFailures) || maxFailures <= 0)
                        return ParseResult.Fail("Consecutive failure limit must be a positive integer.");
                    result.MaxConsecutiveFailures = maxFailures;
                    break;
                case "-S":
                    if (!TryNext(args, ref i, arg, out var sourceText, out var sourceError)) return ParseResult.Fail(sourceError!);
                    if (!IPAddress.TryParse(sourceText, out var sourceAddress)) return ParseResult.Fail($"Invalid source IP address: {sourceText}");
                    result.SourceAddress = sourceAddress;
                    break;
                case "-p":
                    if (!TryNext(args, ref i, arg, out var portText, out var portError)) return ParseResult.Fail(portError!);
                    if (!TryParsePort(portText, out var port)) return ParseResult.Fail($"Invalid port: {portText}");
                    result.PortOverride = port;
                    break;
                case "-js":
                    if (!TryNext(args, ref i, arg, out var windowText, out var windowError)) return ParseResult.Fail(windowError!);
                    if (!int.TryParse(windowText, NumberStyles.None, CultureInfo.InvariantCulture, out var window) || window < 2)
                        return ParseResult.Fail("Jitter window size must be at least 2.");
                    result.JitterWindowSize = window;
                    result.ShowJitter = true;
                    break;
                case "--tee":
                    if (!TryNext(args, ref i, arg, out var teeFile, out var teeError)) return ParseResult.Fail(teeError!);
                    result.TeeFile = teeFile;
                    break;
                case "--file":
                    if (!TryNext(args, ref i, arg, out var fileName, out var fileError)) return ParseResult.Fail(fileError!);
                    result.FileName = fileName;
                    break;
                default:
                    if (arg.StartsWith('-')) return ParseResult.Fail($"Unknown option: {arg}");
                    positional.Add(arg);
                    break;
            }
        }

        if (result.ShowHelp || result.ShowVersion) return ParseResult.Ok(result);
        if (result.Append && result.TeeFile is null) return ParseResult.Fail("--append requires --tee.");
        if (result.SourceAddress is not null && result.AddressFamily is not null &&
            result.SourceAddress.AddressFamily != result.AddressFamily)
            return ParseResult.Fail("The source address family does not match the selected -4 or -6 option.");
        if (result.FileName is not null && positional.Count > 0) return ParseResult.Fail("A host cannot be specified together with --file.");
        if (result.FileName is null)
        {
            if (positional.Count is < 1 or > 2) return ParseResult.Fail("A host and optional port are required.");
            result.Host = positional[0];
            if (positional.Count == 2)
            {
                if (!TryParsePort(positional[1], out var positionalPort)) return ParseResult.Fail($"Invalid port: {positional[1]}");
                result.PositionalPort = positionalPort;
            }
        }
        return ParseResult.Ok(result);
    }

    internal static bool TryParsePort(string text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;

    private static bool TryReadSeconds(string[] args, ref int index, string option, bool allowZero,
        out TimeSpan value, out string? error)
    {
        value = default;
        if (!TryNext(args, ref index, option, out var text, out error)) return false;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            !double.IsFinite(seconds) || (allowZero ? seconds < 0 : seconds <= 0) || seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            error = $"Option {option} requires {(allowZero ? "a non-negative" : "a positive")} number of seconds.";
            return false;
        }
        value = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static bool TryNext(string[] args, ref int index, string option, out string value, out string? error)
    {
        if (++index >= args.Length)
        {
            value = string.Empty;
            error = $"Option {option} requires a value.";
            return false;
        }
        value = args[index];
        error = null;
        return true;
    }

    public static string HelpText => """
        tcping - TCP connection ping utility

        Usage:
          tcping [options] host [port]
          tcping [options] --file targets.txt

        Options:
          -t                Probe continuously until Ctrl+C.
          -n count          Number of probes (default: 4).
          -i seconds        Interval between probes (default: 1; decimals allowed).
          -w seconds        Connection timeout (default: 2; decimals allowed).
          -d                Prefix each result with the current date and time.
          -r count          Refresh DNS after this many probes.
          -s                Stop after the first successful connection.
          -4                Use IPv4 only.
          -6                Use IPv6 only.
          -c                Print only when the state changes.
          -g count          Stop after this many consecutive failures.
          -S address        Bind the socket to a source IP address.
          -p port           Set or override the destination port.
          -j                Display rolling response-time jitter.
          -js count         Set the jitter window size (default: 5, minimum: 2).
          --fqdn            Include a reverse-resolved hostname when available.
          --tee file        Write console output to a file (overwrite by default).
          --append          Append to the file specified by --tee.
          --file file       Read whitespace-separated host and port targets.
          -v, --version     Display the version and exit.
          /?, -?, --help    Display this help and exit.
        """;
}

internal sealed record ParseResult(bool Success, CommandLineOptions? Options, string? Error)
{
    public static ParseResult Ok(CommandLineOptions options) => new(true, options, null);
    public static ParseResult Fail(string error) => new(false, null, error);
}
