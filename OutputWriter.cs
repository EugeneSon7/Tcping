using System.Text;

namespace Tcping;

internal sealed class OutputWriter : IDisposable
{
    private readonly TextWriter? _file;
    private readonly object _sync = new();

    public OutputWriter(string? fileName, bool append)
    {
        if (fileName is not null)
            _file = new StreamWriter(fileName, append, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void WriteLine(string? value = null)
    {
        lock (_sync)
        {
            Console.WriteLine(value);
            _file?.WriteLine(value);
        }
    }

    public void Dispose() => _file?.Dispose();
}
