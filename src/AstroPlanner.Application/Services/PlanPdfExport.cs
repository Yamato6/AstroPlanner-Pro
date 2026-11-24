using System.Globalization;
using System.IO;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Application.Resources;
using AstroPlanner.Application.ViewModels;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace AstroPlanner.Application.Services;

/// <summary>What the PDF of the session plan shows.</summary>
public sealed record PlanPdfContent(
    string Title,
    string Summary,
    string ScheduleSummary,
    PlanTimeline? Timeline,
    IReadOnlyList<PlanItemViewModel> Items,
    IReadOnlyList<(string Text, bool Done)> Checklist);

/// <summary>
/// Printable PDF of the session plan (spec §4.6) with PDFsharp 6.2.4 (MIT): title and night
/// summary, the timeline, the table in observing order and the checklist. US Letter landscape,
/// black on white with light fills, so that it reads well printed and under red light.
/// </summary>
public static class PlanPdfExport
{
    private const string FontFamily = "Arial";
    private const double Margin = 36;
    private const double RowHeight = 15;
    private const double LabelWidth = 90;

    private static readonly XColor Ink = XColor.FromArgb(20, 20, 20);
    private static readonly XColor Muted = XColor.FromArgb(100, 100, 100);
    private static readonly XColor Rule = XColor.FromArgb(200, 200, 200);
    private static readonly XColor HeaderFill = XColor.FromArgb(235, 238, 243);
    private static readonly XColor CrossingInk = XColor.FromArgb(200, 40, 40);

    /// <summary>Column titles and widths (points) of the table.</summary>
    private static (string Title, double Width)[] Columns =>
    [
        (Strings.PrintColumnNumber, 24), (Strings.ColumnStart, 42), (Strings.ColumnEnd, 42), (Strings.ColumnName, 170),
        (Strings.ColumnSubs, 130), (Strings.ColumnIntegration, 62), (Strings.ColumnMeridianFlip, 44), (Strings.ColumnVerdict, 66),
        (Strings.ColumnNotes, 140)
    ];

    static PlanPdfExport()
    {
        // The Core build of PDFsharp needs a font source; on Windows it can read the installed fonts.
        // The setting is global and can be set only once.
        try
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
        catch (InvalidOperationException)
        {
            // Already set (by another caller in this process).
        }
    }

    public static void Write(Stream stream, PlanPdfContent content)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(content);

        using var document = new PdfDocument();
        document.Info.Title = content.Title;
        document.Info.Creator = "AstroPlanner Pro";
        var writer = new PageWriter(document);

        writer.Text(content.Title, writer.TitleFont, Ink);
        writer.Text(content.Summary, writer.Font, Muted);
        if (content.ScheduleSummary.Length > 0)
        {
            writer.Text(content.ScheduleSummary, writer.Font, Muted);
        }

        writer.Y += 8;
        if (content.Timeline is { Rows.Count: > 0 } timeline)
        {
            DrawTimeline(writer, timeline);
        }

        DrawTable(writer, content.Items);
        DrawCrossings(writer, content.Items);
        DrawChecklist(writer, content.Checklist);
        writer.Finish();
        document.Save(stream, closeStream: false);
    }

    private static void DrawTimeline(PageWriter writer, PlanTimeline timeline)
    {
        var height = 18 + timeline.Rows.Count * RowHeight + 6;
        writer.EnsureSpace(height + 12);
        var g = writer.Graphics;
        var left = Margin + LabelWidth;
        var width = writer.ContentWidth - LabelWidth;
        var top = writer.Y;
        var bottom = top + height - 18;
        var start = timeline.WindowStartLocal;
        var span = (timeline.WindowEndLocal - start).TotalMinutes;
        double X(DateTime t) => left + Math.Clamp((t - start).TotalMinutes / span, 0, 1) * width;

        foreach (var band in timeline.TwilightBands)
        {
            g.DrawRectangle(new XSolidBrush(XColor.FromArgb(225, 225, 225)), X(band.Start), top, X(band.End) - X(band.Start), bottom - top);
        }

        foreach (var (cloud, percent) in timeline.Clouds.Where(c => c.CloudCoverPercent >= 5))
        {
            g.DrawRectangle(new XSolidBrush(XColor.FromArgb((int)(percent * 0.6), 120, 150, 190)),
                X(cloud.Start), top, X(cloud.End) - X(cloud.Start), bottom - top);
        }

        // Hour ticks.
        var pen = new XPen(Rule, 0.5);
        for (var hour = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0).AddHours(1); hour < timeline.WindowEndLocal; hour = hour.AddHours(1))
        {
            g.DrawLine(pen, X(hour), top, X(hour), bottom);
            g.DrawString(hour.ToString("HH", CultureInfo.CurrentCulture), writer.SmallFont, new XSolidBrush(Muted),
                new XRect(X(hour) - 10, bottom + 2, 20, 10), XStringFormats.TopCenter);
        }

        g.DrawRectangle(new XPen(Rule, 0.8), left, top, width, bottom - top);
        for (var index = 0; index < timeline.Rows.Count; index++)
        {
            var row = timeline.Rows[index];
            var y = top + 3 + index * RowHeight;
            g.DrawString(Fit(g, row.Label, writer.SmallFont, LabelWidth - 6), writer.SmallFont, new XSolidBrush(Ink),
                new XRect(Margin, y, LabelWidth - 6, RowHeight - 4), XStringFormats.CenterLeft);
            foreach (var usable in row.Usable)
            {
                g.DrawRectangle(new XSolidBrush(XColor.FromArgb(200, 200, 200)), X(usable.Start), y + 5, X(usable.End) - X(usable.Start), 3);
            }

            if (row.Block is { } block)
            {
                var rect = new XRect(X(block.Start), y + 1, Math.Max(1, X(block.End) - X(block.Start)), RowHeight - 6);
                g.DrawRectangle(new XPen(Ink, row.Locked ? 1.4 : 0.6), new XSolidBrush(VerdictColor(row.Verdict)), rect);
            }

            if (row.MeridianFlipLocal is { } flip)
            {
                g.DrawLine(new XPen(Ink, 1.2) { DashStyle = XDashStyle.Dash }, X(flip), y, X(flip), y + RowHeight - 4);
            }

            foreach (var crossing in row.CrossingsLocal ?? [])
            {
                g.DrawLine(new XPen(CrossingInk, 1.2), X(crossing), y - 1, X(crossing), y + 4);
            }
        }

        writer.Y = bottom + 14;
        g.DrawString(Strings.PdfTimelineLegend, writer.SmallFont, new XSolidBrush(Muted), new XPoint(Margin, writer.Y + 6));
        writer.Y += 16;
    }

    private static void DrawTable(PageWriter writer, IReadOnlyList<PlanItemViewModel> items)
    {
        var culture = CultureInfo.CurrentCulture;
        DrawRow(writer, [.. Columns.Select(c => c.Title)], header: true);
        var order = 1;
        foreach (var item in items)
        {
            if (writer.EnsureSpace(RowHeight + 2))
            {
                DrawRow(writer, [.. Columns.Select(c => c.Title)], header: true);
            }

            DrawRow(writer,
            [
                order++.ToString(culture),
                item.StartLocal?.ToString("HH:mm", culture) ?? "—",
                item.EndLocal?.ToString("HH:mm", culture) ?? "—",
                item.Row.Title,
                item.SubsText.Length > 0 ? item.SubsText : item.StatusText,
                item.IntegrationText,
                item.MeridianFlipLocal?.ToString("HH:mm", culture) ?? "",
                item.VerdictText,
                item.Note
            ], header: false);
        }

        writer.Y += 10;
    }

    private static void DrawRow(PageWriter writer, string[] values, bool header)
    {
        var g = writer.Graphics;
        var font = header ? writer.BoldFont : writer.Font;
        var x = Margin;
        if (header)
        {
            g.DrawRectangle(new XSolidBrush(HeaderFill), Margin, writer.Y, writer.ContentWidth, RowHeight);
        }

        var columns = Columns;
        for (var index = 0; index < columns.Length; index++)
        {
            var width = index == columns.Length - 1 ? writer.ContentWidth - (x - Margin) : columns[index].Width;
            g.DrawString(Fit(g, values[index], font, width - 6), font, new XSolidBrush(Ink),
                new XRect(x + 3, writer.Y, width - 6, RowHeight), XStringFormats.CenterLeft);
            x += width;
        }

        writer.Y += RowHeight;
        g.DrawLine(new XPen(Rule, 0.5), Margin, writer.Y, Margin + writer.ContentWidth, writer.Y);
    }

    /// <summary>The satellites crossing the field of each block, one line each (nothing when there are none).</summary>
    private static void DrawCrossings(PageWriter writer, IReadOnlyList<PlanItemViewModel> items)
    {
        var lines = items.SelectMany(item => item.Crossings.Select(c => $"{item.Row.Title}: {c.Text}")).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        writer.EnsureSpace(RowHeight * 3);
        writer.Text(Strings.PdfCrossingsTitle, writer.BoldFont, Ink);
        foreach (var line in lines)
        {
            writer.Text(line, writer.Font, Ink);
        }

        writer.Y += 8;
    }

    private static void DrawChecklist(PageWriter writer, IReadOnlyList<(string Text, bool Done)> checklist)
    {
        if (checklist.Count == 0)
        {
            return;
        }

        writer.EnsureSpace(RowHeight * 4);
        writer.Text(Strings.ChecklistTitle, writer.BoldFont, Ink);
        var g = writer.Graphics;
        const double columnWidth = 180;
        var perRow = Math.Max(1, (int)(writer.ContentWidth / columnWidth));
        for (var index = 0; index < checklist.Count; index++)
        {
            if (index % perRow == 0 && index > 0)
            {
                writer.Y += RowHeight;
                writer.EnsureSpace(RowHeight);
            }

            var x = Margin + index % perRow * columnWidth;
            var box = new XRect(x, writer.Y + 3, 9, 9);
            g.DrawRectangle(new XPen(Ink, 0.8), box);
            if (checklist[index].Done)
            {
                g.DrawLine(new XPen(Ink, 1), box.Left + 1.5, box.Top + 4.5, box.Left + 4, box.Bottom - 1.5);
                g.DrawLine(new XPen(Ink, 1), box.Left + 4, box.Bottom - 1.5, box.Right - 1, box.Top + 1);
            }

            g.DrawString(Fit(g, checklist[index].Text, writer.Font, columnWidth - 20), writer.Font, new XSolidBrush(Ink),
                new XRect(x + 14, writer.Y, columnWidth - 20, RowHeight), XStringFormats.CenterLeft);
        }

        writer.Y += RowHeight;
    }

    /// <summary>Fill of a block by verdict: darker = better (still readable in grey).</summary>
    private static XColor VerdictColor(FeasibilityVerdict? verdict) => verdict switch
    {
        FeasibilityVerdict.Excellent => XColor.FromArgb(46, 125, 80),
        FeasibilityVerdict.Good => XColor.FromArgb(96, 165, 120),
        FeasibilityVerdict.Marginal => XColor.FromArgb(222, 184, 92),
        FeasibilityVerdict.Unlikely => XColor.FromArgb(214, 140, 120),
        _ => XColor.FromArgb(190, 190, 190)
    };

    /// <summary>The text, shortened with "…" to fit <paramref name="width"/>.</summary>
    private static string Fit(XGraphics g, string text, XFont font, double width)
    {
        if (string.IsNullOrEmpty(text) || g.MeasureString(text, font).Width <= width)
        {
            return text ?? string.Empty;
        }

        var length = text.Length;
        while (length > 1 && g.MeasureString(text[..length] + "…", font).Width > width)
        {
            length--;
        }

        return text[..length] + "…";
    }

    /// <summary>Pages, fonts and the vertical cursor; adds a page (with footer) when one fills up.</summary>
    private sealed class PageWriter
    {
        private readonly PdfDocument _document;
        private int _pages;

        public PageWriter(PdfDocument document)
        {
            _document = document;
            Font = new XFont(FontFamily, 8.5);
            BoldFont = new XFont(FontFamily, 8.5, XFontStyleEx.Bold);
            SmallFont = new XFont(FontFamily, 7);
            TitleFont = new XFont(FontFamily, 16, XFontStyleEx.Bold);
            NewPage();
        }

        public XFont Font { get; }
        public XFont BoldFont { get; }
        public XFont SmallFont { get; }
        public XFont TitleFont { get; }
        public XGraphics Graphics { get; private set; } = null!;
        public double Y { get; set; }
        public double ContentWidth { get; private set; }
        private double Bottom { get; set; }

        public void Text(string text, XFont font, XColor color)
        {
            var height = font.GetHeight() + 3;
            EnsureSpace(height);
            Graphics.DrawString(Fit(Graphics, text, font, ContentWidth), font, new XSolidBrush(color),
                new XRect(Margin, Y, ContentWidth, height), XStringFormats.TopLeft);
            Y += height;
        }

        /// <returns>True when a new page was started.</returns>
        public bool EnsureSpace(double height)
        {
            if (Y + height <= Bottom)
            {
                return false;
            }

            NewPage();
            return true;
        }

        public void Finish() => Footer();

        private void NewPage()
        {
            if (_pages > 0)
            {
                Footer();
                Graphics.Dispose();
            }

            var page = _document.AddPage();
            page.Size = PageSize.Letter;
            page.Orientation = PageOrientation.Landscape;
            _pages++;
            Graphics = XGraphics.FromPdfPage(page);
            ContentWidth = page.Width.Point - 2 * Margin;
            Bottom = page.Height.Point - Margin - 14;
            Y = Margin;
        }

        private void Footer()
        {
            var page = _document.Pages[_pages - 1];
            Graphics.DrawString(DisplayNames.Format(Strings.PdfFooterFormat, _pages), SmallFont, new XSolidBrush(Muted),
                new XRect(Margin, page.Height.Point - Margin, ContentWidth, 10), XStringFormats.TopRight);
        }
    }
}
