using System.Globalization;

namespace PriceRadar.Seeder;

/// <summary>Tiny parser for <c>command --key value --flag</c> command lines.</summary>
internal sealed class CliArgs
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public string? Command { get; private init; }

    public static CliArgs Parse(string[] args)
    {
        var result = new CliArgs { Command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : null };
        for (var i = result.Command is null ? 0 : 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{args[i]}'.");
            }

            var key = args[i][2..];
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            result._options[key] = hasValue ? args[++i] : null;
        }

        return result;
    }

    public bool Flag(string key) => _options.ContainsKey(key);

    public string? String(string key) => _options.GetValueOrDefault(key);

    public int Int(string key, int fallback) =>
        _options.TryGetValue(key, out var v) && v is not null
            ? int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0
                ? n
                : throw new ArgumentException($"--{key} must be a positive integer, got '{v}'.")
            : fallback;

    public DateOnly? Date(string key) =>
        _options.TryGetValue(key, out var v) && v is not null
            ? DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : throw new ArgumentException($"--{key} must be yyyy-MM-dd, got '{v}'.")
            : null;
}
