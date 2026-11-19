using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Services;

/// <summary>
/// Printable "My plan": title, night summary and a table in observing order. Black on white
/// so that it reads well on paper (and under red light in the field).
/// </summary>
public static class PlanPrintDocument
{
    private static readonly double[] ColumnWidths = [28, 74, 46, 150, 92, 40, 56, 44, 180];

    private static string[] Headers =>
    [
        Strings.PrintColumnNumber, Strings.ColumnCulmination, Strings.ColumnId, Strings.ColumnName,
        Strings.ColumnConstellation, Strings.ColumnMagnitude, Strings.ColumnMaximumAltitude, Strings.ColumnMoon,
        Strings.ColumnNotes
    ];

    public static FlowDocument Create(IReadOnlyList<PlanItemViewModel> items, string summary)
    {
        ArgumentNullException.ThrowIfNull(items);

        var culture = CultureInfo.CurrentCulture;
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            PagePadding = new Thickness(48),
            ColumnWidth = double.PositiveInfinity,
            Background = Brushes.White,
            Foreground = Brushes.Black
        };

        document.Blocks.Add(new Paragraph(new Run(Strings.PlanPrintTitle))
        {
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        document.Blocks.Add(new Paragraph(new Run(summary))
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 1) };
        foreach (var width in ColumnWidths)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(width) });
        }

        var group = new TableRowGroup();
        group.Rows.Add(CreateRow(Headers, isHeader: true));

        var order = 1;
        foreach (var item in items)
        {
            var row = item.Row;
            group.Rows.Add(CreateRow(
            [
                order++.ToString(culture),
                item.CulminationLocal?.ToString("HH:mm", culture) ?? "—",
                row.Id,
                row.CommonName,
                row.Constellation,
                row.ApparentMagnitude.ToString("0.0", culture),
                string.Create(culture, $"{item.MaximumAltitudeDegrees:0}°"),
                row.MoonInterferes
                    ? string.Create(culture, $"{row.MoonSeparationDegrees:0}° !")
                    : string.Create(culture, $"{row.MoonSeparationDegrees:0}°"),
                item.Note
            ], isHeader: false));
        }

        table.RowGroups.Add(group);
        document.Blocks.Add(table);

        if (items.Any(item => item.Row.MoonInterferes))
        {
            document.Blocks.Add(new Paragraph(new Run(Strings.PlanPrintMoonFootnote))
            {
                FontSize = 9,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 8, 0, 0)
            });
        }

        return document;
    }

    private static TableRow CreateRow(IEnumerable<string> values, bool isHeader)
    {
        var row = new TableRow();
        foreach (var value in values)
        {
            row.Cells.Add(new TableCell(new Paragraph(new Run(value)) { Margin = new Thickness(0) })
            {
                Padding = new Thickness(4, 3, 4, 3),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1),
                FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                Background = isHeader ? new SolidColorBrush(Color.FromRgb(235, 238, 243)) : Brushes.White
            });
        }

        return row;
    }
}
