namespace Tcping;

internal sealed record TcpTarget(string Host, int Port);

internal static class FileTargetReader
{
    public static IReadOnlyList<TcpTarget> Read(string path, int? portOverride)
    {
        var targets = new List<TcpTarget>();
        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 2) throw new FormatException($"Invalid target at {path}:{lineNumber}.");

            int port;
            if (portOverride is not null) port = portOverride.Value;
            else if (parts.Length == 1) port = 80;
            else if (!CommandLineOptions.TryParsePort(parts[1], out port))
                throw new FormatException($"Invalid port at {path}:{lineNumber}: {parts[1]}");
            targets.Add(new TcpTarget(parts[0], port));
        }

        if (targets.Count == 0) throw new FormatException($"No targets were found in {path}.");
        return targets;
    }
}
