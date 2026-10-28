using System.Runtime.CompilerServices;
using System.Text.Json;
using PriceRadar.Core.Abstractions;

namespace PriceRadar.Infrastructure.Sources;

/// <summary>
/// Replays a recorded run from <c>{root}/{code}/{fixtureSet}.json</c> (spec §4.5). One instance per source code:
/// <c>fixture</c> (presence scenario) and <c>kp</c> (KupujemProdajem-style invented ads, D-39).
/// </summary>
public sealed class FixtureListingSource(string code, string fixturesRoot) : IListingSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Code { get; } = code;

    public async IAsyncEnumerable<RawListing> FetchAsync(ScrapeRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var file = await LoadAsync(request, ct);

        request.Progress.CapturedAtUtc = file.CapturedAtUtc;
        foreach (var l in file.Listings)
        {
            ct.ThrowIfCancellationRequested();
            yield return new RawListing(l.ExternalId, l.Title, l.Author, l.Price, l.Condition, l.Url, l.PostedAtUtc);
        }

        // A replayed file always covers its whole scope.
        request.Progress.PagesFetched = file.Pages;
        request.Progress.IsComplete = true;
    }

    public string PathFor(string fixtureSet) => Path.Combine(fixturesRoot, Code, $"{fixtureSet}.json");

    private async Task<FixtureFile> LoadAsync(ScrapeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FixtureSet))
        {
            throw new ArgumentException($"Source '{Code}' needs a fixture set, e.g. \"run-001\".", nameof(request));
        }

        if (request.FixtureSet.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || request.FixtureSet.Contains(".."))
        {
            throw new ArgumentException($"Invalid fixture set name '{request.FixtureSet}'.", nameof(request));
        }

        var path = PathFor(request.FixtureSet);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture set '{request.FixtureSet}' not found for source '{Code}'.", path);
        }

        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<FixtureFile>(stream, JsonOptions, ct)
            ?? throw new InvalidDataException($"Fixture file '{path}' is empty.");

        if (!string.Equals(file.ScopeKey, request.ScopeKey, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Fixture '{path}' has scope '{file.ScopeKey}' but the run requested '{request.ScopeKey}'.");
        }

        return file;
    }

    private sealed record FixtureFile(string Source, string ScopeKey, DateTime CapturedAtUtc, short Pages, List<FixtureListing> Listings);

    private sealed record FixtureListing(
        string ExternalId, string Title, string? Author, string? Price, string? Condition, string Url, DateTime? PostedAtUtc);
}
