namespace Tcping;

internal sealed class JitterCalculator(int windowSize)
{
    private readonly Queue<double> _samples = new();

    public double Add(double milliseconds)
    {
        _samples.Enqueue(milliseconds);
        while (_samples.Count > windowSize) _samples.Dequeue();
        if (_samples.Count < 2) return 0;
        var samples = _samples.ToArray();
        var totalDifference = 0d;
        for (var i = 1; i < samples.Length; i++) totalDifference += Math.Abs(samples[i] - samples[i - 1]);
        return totalDifference / (samples.Length - 1);
    }
}
