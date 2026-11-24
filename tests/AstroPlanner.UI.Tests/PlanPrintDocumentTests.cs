using System.Globalization;
using System.Windows.Documents;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;
using AstroPlanner.UI.Services;
using AstroPlanner.Application.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>My plan prints as a WPF FlowDocument.</summary>
public sealed class PlanPrintDocumentTests
{
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private readonly FakePlanService _planService = new();

    private async Task<ShellViewModel> CreateLoadedMainAsync()
    {
        var viewModel = TestShell.Create(plan: _planService, settings: new FakeSettingsService(new ObserverSettings { MinimumAltitude = 20 }),
            time: new ManualTimeProvider(WinterNightUtc));
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task PrintDocument_HasHeaderRowPlusOneRowPerItem()
    {
        _planService.Entries = [new("M42", "note"), new("M31")];
        var viewModel = await CreateLoadedMainAsync();
        var items = viewModel.Plan.Items.ToList();
        var summary = viewModel.Plan.SummaryText;

        // FlowDocument needs an STA thread, like the UI one.
        var rowCount = RunOnStaThread(() =>
        {
            var document = PlanPrintDocument.Create(items, summary);
            return document.Blocks.OfType<Table>().Single().RowGroups[0].Rows.Count;
        });

        rowCount.Should().Be(3);
        summary.Should().Contain("Night of 2026-01-15").And.Contain("Moon");
    }

    private static T RunOnStaThread<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error is null ? result : throw new InvalidOperationException("Failed on the STA thread.", error);
    }
}
