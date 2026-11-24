using System.IO;
using System.Text.RegularExpressions;
using AstroPlanner.Application.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>What used to live in the main window's code-behind and is now in the view models.</summary>
public sealed partial class MainWindowLogicTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Mexico City (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlanExports_AreNamedAfterTheObservingDate()
    {
        var shell = TestShell.Create(time: new ManualTimeProvider(WinterNightUtc));

        shell.Plan.SuggestedFileName("pdf").Should().Be("AstroPlannerPro-plan-2026-01-15.pdf");
        shell.Plan.SuggestedFileName("ics").Should().Be("AstroPlannerPro-plan-2026-01-15.ics");

        shell.ObservationDate = new DateTime(2026, 3, 2);

        shell.Plan.SuggestedFileName("csv").Should().Be("AstroPlannerPro-plan-2026-03-02.csv");
    }

    [Fact]
    public void LogbookExport_IsNamedAfterToday() =>
        LogbookViewModel.SuggestedExportFileName(new DateTime(2026, 11, 21, 23, 5, 0)).Should().Be("AstroPlannerPro-logbook-2026-11-21.csv");

    [Fact]
    public void ToggleNightTheme_SwitchesTheThemeBothWays()
    {
        var settings = new FakeSettingsService(new ObserverSettings());
        var shell = TestShell.Create(settings: settings);

        shell.ToggleNightThemeCommand.Execute(null);

        shell.IsNightTheme.Should().BeTrue();
        settings.Load().Theme.Should().Be(AppTheme.Night);

        shell.ToggleNightThemeCommand.Execute(null);

        shell.IsNightTheme.Should().BeFalse();
        settings.Load().Theme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public void KeyBindings_RunCommandsOfTheShell()
    {
        var xaml = File.ReadAllText(Path.Combine(AccessibilityTests.RepositoryRoot(), "src", "AstroPlanner.UI", "MainWindow.xaml"));
        var commands = KeyBindingCommand().Matches(xaml).Select(m => m.Groups[1].Value).ToList();

        commands.Should().Contain(["SetNowCommand", "ToggleNightThemeCommand"]);
        commands.Should().OnlyContain(name => typeof(ShellViewModel).GetProperty(name) != null);
    }

    [GeneratedRegex("""<KeyBinding [^>]*Command="\{Binding (\w+)\}""")]
    private static partial Regex KeyBindingCommand();
}
