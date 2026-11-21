using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>
/// M6b accessibility pass: every focusable control of every window has a name for screen
/// readers, and the text colors of both themes keep a WCAG AA contrast (4.5:1).
/// </summary>
public sealed partial class AccessibilityTests
{
    // ─── Contrast (WCAG 2.1, 1.4.3) ─────────────────────────────────────

    /// <summary>Text brushes, each checked on every background where text is drawn.</summary>
    private static readonly string[] TextBrushes =
    [
        "NightTextBrush", "StrongTextBrush", "LabelTextBrush", "TabTextBrush", "NightMutedTextBrush", "HighlightTextBrush",
        "AccentBrush", "SuccessBrush", "SuccessSoftBrush", "DangerBrush", "DangerSoftBrush", "WarningBrush", "MoonBrush",
        "PlanetBrush", "DarknessBrush"
    ];

    private static readonly string[] Backgrounds =
    [
        "WindowBackgroundBrush", "DialogBackgroundBrush", "NightPanelBrush", "NightCardBrush", "SurfaceBrush", "ControlBrush",
        "ControlRaisedBrush", "BannerBrush"
    ];

    /// <summary>Hover and selection only ever carry the main text colors (rows, buttons, tabs).</summary>
    private static readonly string[] MainText = ["NightTextBrush", "StrongTextBrush", "HighlightTextBrush"];

    private static readonly string[] HighlightBackgrounds = ["ControlHoverBrush", "SelectionBrush"];

    /// <summary>Primary buttons draw HighlightTextBrush on the primary colors.</summary>
    private static readonly string[] PrimaryBackgrounds = ["PrimaryBrush", "PrimaryPressedBrush"];

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Night.xaml")]
    public void ThemeText_HasAaContrast(string theme)
    {
        var colors = ThemeColors(theme);
        var pairs = TextBrushes.SelectMany(_ => Backgrounds, (text, background) => (text, background))
            .Concat(MainText.SelectMany(_ => HighlightBackgrounds, (text, background) => (text, background)))
            .Concat(PrimaryBackgrounds.Select(background => (text: "HighlightTextBrush", background)));

        var failures = pairs
            .Select(p => (p.text, p.background, Ratio: ContrastRatio(colors[p.text], colors[p.background])))
            .Where(p => p.Ratio < 4.5)
            .Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.text} on {p.background}: {p.Ratio:0.00}"))
            .ToList();

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void NightTheme_RedefinesEveryColorOfTheDarkTheme_AndIsRed()
    {
        var dark = ThemeColors("Dark.xaml");
        var night = ThemeColors("Night.xaml");

        night.Keys.Should().BeEquivalentTo(dark.Keys);
        night.Values.Should().OnlyContain(c => c.R >= c.G && c.R >= c.B, "night vision has no green or blue light");
    }

    [Fact]
    public void Windows_UseThemeBrushesOnlyThroughDynamicResource()
    {
        var folder = Path.Combine(RepositoryRoot(), "src", "AstroPlanner.UI");
        var files = Directory.GetFiles(folder, "*.xaml").Append(Path.Combine(folder, "Themes", "Dark.xaml"));

        files.SelectMany(f => StaticBrush().Matches(File.ReadAllText(f)).Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .Should().BeEmpty("a StaticResource brush would not change with the night theme");
    }

    /// <summary>Relative luminance ratio (WCAG 2.1).</summary>
    public static double ContrastRatio(System.Windows.Media.Color a, System.Windows.Media.Color b)
    {
        var (light, dark) = (Luminance(a), Luminance(b)) is var (x, y) && x > y ? (x, y) : (y, x);
        return (light + 0.05) / (dark + 0.05);

        static double Luminance(System.Windows.Media.Color color) =>
            0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }

    [Fact]
    public void ContrastRatio_MatchesTheWcagExamples()
    {
        ContrastRatio(System.Windows.Media.Colors.Black, System.Windows.Media.Colors.White).Should().BeApproximately(21, 0.01);
        ContrastRatio(System.Windows.Media.Color.FromRgb(0x77, 0x77, 0x77), System.Windows.Media.Colors.White).Should().BeApproximately(4.48, 0.01);
    }

    private static Dictionary<string, System.Windows.Media.Color> ThemeColors(string file) =>
        BrushColor().Matches(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AstroPlanner.UI", "Themes", file)))
            .ToDictionary(m => m.Groups[1].Value,
                m => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(m.Groups[2].Value));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AstroPlanner.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("AstroPlanner.slnx not found above the test folder.");
    }

    [GeneratedRegex("""<SolidColorBrush x:Key="(\w+)" Color="(#[0-9A-Fa-f]{6})"/>""")]
    private static partial Regex BrushColor();

    [GeneratedRegex(@"\{StaticResource \w+Brush\}")]
    private static partial Regex StaticBrush();

    // ─── Names for screen readers ───────────────────────────────────────

    [Fact]
    public void EveryFocusableControl_OfEveryWindow_HasAName()
    {
        var unnamed = WpfThread.Invoke(() =>
        {
            var shell = TestShell.Create();
            var logbook = new LogbookViewModel(new FakeLogService(), new NullLogger());
            var draft = logbook.CreateDraft("M42", "Orion Nebula", new DateTime(2026, 1, 15, 22, 0, 0), "Home",
                new CaptureDetails { RigName = "Refractor" }, null, photographed: true);
            Window[] windows =
            [
                new MainWindow(new NullLogger()) { DataContext = shell },
                new SettingsWindow(shell.Settings),
                new LogEntryWindow(draft),
                new AboutWindow(),
                new MilkyWayWindow(shell.Tonight.CreateMilkyWayPlanner()!)
            ];

            var problems = new List<string>();
            foreach (var window in windows)
            {
                foreach (var control in LogicalDescendants(window).OfType<Control>().Where(NeedsName))
                {
                    if (!HasName(control))
                    {
                        problems.Add($"{window.GetType().Name}: {control.GetType().Name} {Describe(control)}");
                    }
                }

                window.Close();
            }

            shell.Dispose();
            return problems;
        });

        string.Join(Environment.NewLine, unnamed).Should().BeEmpty();
    }

    /// <summary>Controls the user reaches with Tab; containers and read-only text are not.</summary>
    private static bool NeedsName(Control control) =>
        control is ButtonBase or TextBox or ComboBox or DatePicker or Slider or ListBox or DataGrid or PasswordBox
        && control.Focusable && control.IsTabStop;

    private static bool HasName(Control control)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(control))
            || BindingOperations.IsDataBound(control, AutomationProperties.NameProperty)
            || AutomationProperties.GetLabeledBy(control) is not null)
        {
            return true;
        }

        // The content is the name of buttons and check boxes (a string, an x:Static text or a binding).
        return control is ContentControl content
               && (content.Content is string { Length: > 0 } or TextBlock or AccessText
                   || BindingOperations.IsDataBound(content, ContentControl.ContentProperty));
    }

    private static string Describe(Control control) =>
        control.Name is { Length: > 0 } name ? name
        : BindingOperations.GetBindingExpression(control, TextBox.TextProperty)?.ParentBinding.Path?.Path
          ?? BindingOperations.GetBindingExpression(control, Selector.SelectedItemProperty)?.ParentBinding.Path?.Path
          ?? BindingOperations.GetBindingExpression(control, ToggleButton.IsCheckedProperty)?.ParentBinding.Path?.Path
          ?? BindingOperations.GetBindingExpression(control, ItemsControl.ItemsSourceProperty)?.ParentBinding.Path?.Path
          ?? (control as ButtonBase)?.Content?.ToString()
          ?? "(no binding)";

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalDescendants(child))
            {
                yield return descendant;
            }
        }
    }
}

/// <summary>
/// One STA thread with a WPF <see cref="Application"/> and the app's theme, shared by the tests
/// that create windows (WPF objects belong to the thread that made them).
/// </summary>
internal static class WpfThread
{
    private static readonly Lazy<Dispatcher> Dispatcher = new(Start);

    public static T Invoke<T>(Func<T> action) => Dispatcher.Value.Invoke(action);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/AstroPlannerPro;component/Themes/Dark.xaml")
            });
            dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test thread"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
