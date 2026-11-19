using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.Tests;

/// <summary>File logging, in a temporary folder (app.db persistence is tested in AstroPlanner.Data.Tests).</summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));

    public PersistenceTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathFor(string fileName) => Path.Combine(_directory, fileName);

    [Fact]
    public void FileLogger_WritesErrorWithExceptionDetails()
    {
        var logger = new FileAppLogger(PathFor("logs"));

        logger.LogError("Test failure", new InvalidOperationException("internal detail"));

        var text = File.ReadAllText(logger.CurrentFilePath);
        text.Should().Contain("[ERROR] Test failure")
            .And.Contain("InvalidOperationException")
            .And.Contain("internal detail");
    }
}
