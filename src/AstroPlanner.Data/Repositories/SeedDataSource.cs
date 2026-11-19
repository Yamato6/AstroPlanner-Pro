namespace AstroPlanner.Data.Repositories;

/// <summary>
/// Source of the JSON catalogs. By default they are read from the resources embedded in the
/// assembly, so that the published .exe works without the SeedData folder; an explicit path
/// (tests, custom catalogs) takes precedence.
/// </summary>
internal static class SeedDataSource
{
    /// <summary>Opens <paramref name="filePath"/> or, if it is null, the embedded resource <paramref name="fileName"/>.</summary>
    public static Stream Open(string fileName, string? filePath)
    {
        if (filePath is not null)
        {
            return File.OpenRead(filePath);
        }

        var resourceName = $"SeedData/{fileName}";
        return typeof(SeedDataSource).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded catalog '{resourceName}' was not found.", resourceName);
    }

    /// <summary>Readable description of the source, for error messages.</summary>
    public static string Describe(string fileName, string? filePath) =>
        filePath ?? $"SeedData/{fileName} (embedded)";
}
