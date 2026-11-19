using System.Reflection;
using AstroPlanner.Core.Services;
using AstroPlanner.Data.Catalog;
using AstroPlanner.Online.Weather;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>
/// Dependency rules of the solution (spec §5.2), checked on the compiled assemblies:
/// Core depends on the BCL only; Data and Online depend on Core only; only the UI (the
/// composition root) may reference everything; the DataBuilder tool may use Core and Data, and
/// nothing may depend on it. Only Data (and the tool) talk to SQLite.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Core = typeof(AstronomyService).Assembly;
    private static readonly Assembly Data = typeof(SqliteCatalogRepository).Assembly;
    private static readonly Assembly Online = typeof(OpenMeteoForecastService).Assembly;
    private static readonly Assembly Ui = typeof(ShellViewModel).Assembly;
    private static readonly Assembly DataBuilder = Assembly.Load("AstroPlanner.DataBuilder");

    /// <summary>BCL assemblies that would mean IO-heavy, network or UI code inside the domain.</summary>
    private static readonly string[] ForbiddenInCore =
    [
        "System.Net.Http", "System.Net.Primitives", "System.Net.Sockets", "System.Data", "Microsoft.Data.Sqlite",
        "PresentationCore", "PresentationFramework", "WindowsBase", "System.Xaml", "System.Windows.Forms"
    ];

    private static List<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

    private static List<string> ProjectReferences(Assembly assembly) =>
        References(assembly).Where(name => name.StartsWith("AstroPlanner", StringComparison.Ordinal)).ToList();

    private static bool IsBcl(string name) =>
        name is "netstandard" or "mscorlib"
        || name.StartsWith("System.", StringComparison.Ordinal) || name == "System"
        || name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);

    [Fact]
    public void Core_DependsOnTheBaseClassLibraryOnly()
    {
        var references = References(Core);

        references.Should().OnlyContain(name => IsBcl(name), "Core is the pure domain: no packages, no other projects");
        references.Should().NotContain(ForbiddenInCore, "Core must not do network, database or UI work");
    }

    [Theory]
    [InlineData("Data")]
    [InlineData("Online")]
    public void DataAndOnline_DependOnCoreOnly(string layer)
    {
        var assembly = layer == "Data" ? Data : Online;

        ProjectReferences(assembly).Should().BeEquivalentTo([Core.GetName().Name!]);
    }

    [Fact]
    public void OnlyTheUiReferencesTheUi()
    {
        var uiName = Ui.GetName().Name!;

        foreach (var assembly in new[] { Core, Data, Online, DataBuilder })
        {
            References(assembly).Should().NotContain(uiName);
        }
    }

    [Fact]
    public void DataBuilder_UsesCoreAndDataOnly_AndNothingUsesIt()
    {
        ProjectReferences(DataBuilder).Should().Contain(Core.GetName().Name!)
            .And.BeSubsetOf([Core.GetName().Name!, Data.GetName().Name!]);

        foreach (var assembly in new[] { Core, Data, Online, Ui })
        {
            References(assembly).Should().NotContain(DataBuilder.GetName().Name!);
        }
    }

    [Fact]
    public void OnlyDataTalksToSqlite()
    {
        References(Data).Should().Contain("Microsoft.Data.Sqlite");

        foreach (var assembly in new[] { Core, Online, Ui })
        {
            References(assembly).Should().NotContain("Microsoft.Data.Sqlite");
        }
    }

    [Fact]
    public void Core_HasNoWpfOrHttpTypesInItsPublicSurface()
    {
        var offending = Core.GetExportedTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(member => member switch
            {
                MethodInfo method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType),
                PropertyInfo property => [property.PropertyType],
                FieldInfo field => [field.FieldType],
                _ => []
            })
            .Where(type => type.Namespace is { } ns
                           && (ns.StartsWith("System.Net", StringComparison.Ordinal)
                               || ns.StartsWith("System.Windows", StringComparison.Ordinal)
                               || ns.StartsWith("System.IO", StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .Distinct()
            .ToList();

        offending.Should().BeEmpty();
    }
}
