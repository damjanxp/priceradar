using System.Reflection;

namespace PriceRadar.Infrastructure.Sql;

/// <summary>Loads the versioned .sql files embedded in this assembly (stored procedures, seed scripts).</summary>
public static class SqlResource
{
    private static readonly Assembly Assembly = typeof(SqlResource).Assembly;

    /// <param name="relativePath">Path below the Sql folder, e.g. <c>Seed/dw.DimDate.sql</c>.</param>
    public static string Load(string relativePath)
    {
        var resourceName = $"{Assembly.GetName().Name}.Sql.{relativePath.Replace('/', '.').Replace('\\', '.')}";
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
