using PriceRadar.Core.Abstractions;

namespace PriceRadar.Infrastructure.Sources;

/// <summary>Finds the <see cref="IListingSource"/> registered for a source code.</summary>
public sealed class ListingSourceRegistry(IEnumerable<IListingSource> sources)
{
    private readonly Dictionary<string, IListingSource> _byCode =
        sources.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);

    public IListingSource Get(string code) =>
        _byCode.TryGetValue(code, out var source)
            ? source
            : throw new KeyNotFoundException($"No listing source is registered for '{code}'.");

    public bool Contains(string code) => _byCode.ContainsKey(code);
}
