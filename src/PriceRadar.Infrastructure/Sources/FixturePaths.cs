namespace PriceRadar.Infrastructure.Sources;

public static class FixturePaths
{
    private const string RelativeRoot = "tests/fixtures/sources";

    /// <summary>
    /// Uses <paramref name="configured"/> when set (e.g. <c>Sources:FixturesPath</c> in a container); otherwise walks
    /// up from the app's base directory to the repository's <c>tests/fixtures/sources</c> folder.
    /// </summary>
    public static string Resolve(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, RelativeRoot);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find '{RelativeRoot}' above '{AppContext.BaseDirectory}'. Set Sources:FixturesPath.");
    }
}
