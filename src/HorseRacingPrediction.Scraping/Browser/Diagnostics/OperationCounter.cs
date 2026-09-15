using System.Collections.Concurrent;

namespace HorseRacingPrediction.Scraping.Browser.Diagnostics;

/// <summary>Explicit counter seam for repository, browser, or transport calls in diagnostics.</summary>
public sealed class OperationCounter
{
    private readonly ConcurrentDictionary<string, long> _counts = new(StringComparer.Ordinal);

    public long Increment(string operation, long value = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
        return _counts.AddOrUpdate(operation, value, (_, current) => checked(current + value));
    }

    public long Get(string operation) => _counts.GetValueOrDefault(operation);

    public IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(_counts);
}
