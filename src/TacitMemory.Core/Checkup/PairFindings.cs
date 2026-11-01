using TacitMemory.Core.Decisions;

namespace TacitMemory.Core.Checkup;

/// <summary>One unordered pair two items were found in: the first item and the second as they were first found, and the tier.</summary>
internal sealed record PairFinding<T>(T First, T Second, Tier Tier);

/// <summary>
/// The pairs a category lists. A pair is unordered: it is listed once, in the order it was first found, at the tier of
/// its surest answer. Items are told apart by <paramref name="keyOf"/>.
/// </summary>
internal sealed class PairFindings<T>(Func<T, string> keyOf)
{
    private readonly List<PairFinding<T>> _pairs = [];
    private readonly Dictionary<(string, string), int> _at = [];

    public IReadOnlyList<PairFinding<T>> Pairs => _pairs;

    public void Add(T first, T second, Tier tier)
    {
        var (a, b) = (keyOf(first), keyOf(second));
        var key = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
        if (!_at.TryGetValue(key, out var at))
        {
            _at[key] = _pairs.Count;
            _pairs.Add(new PairFinding<T>(first, second, tier));
        }
        else if (tier == Tier.Certain && _pairs[at].Tier != Tier.Certain)
        {
            _pairs[at] = _pairs[at] with { Tier = Tier.Certain };
        }
    }
}
