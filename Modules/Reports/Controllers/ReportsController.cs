using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RentalSphere.Common.Constants;
using RentalSphere.Modules.Reports.DTOs;
using RentalSphere.Modules.Reports.Services;

namespace RentalSphere.Modules.Reports.Controllers;

/// <summary>
/// Read-only Reports dashboard. Admin + Staff only — Customer role is forbidden
/// (CLAUDE.md: Customers have no access to Reports).
/// </summary>
[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
public class ReportsController : Controller
{
    private readonly IReportsService _service;

    public ReportsController(IReportsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DateTime? start, DateTime? end, string? granularity)
    {
        var s = (start ?? DateTime.UtcNow.Date.AddDays(-30)).Date;
        var e = (end ?? DateTime.UtcNow.Date).Date;
        if (e <= s) e = s.AddDays(1);
        granularity = string.IsNullOrWhiteSpace(granularity) ? "Daily" : granularity;

        var bundle = await _service.GetBundleAsync(s, e, granularity);

        ViewBag.Start = s;
        ViewBag.End = e;
        ViewBag.Granularity = granularity;
        ViewBag.ActiveTab = HttpContext.Request.Query["tab"].ToString();
        if (string.IsNullOrEmpty(ViewBag.ActiveTab)) ViewBag.ActiveTab = "revenue";

        return View(bundle);
    }

    [HttpGet]
    public async Task<IActionResult> ExportPdf(DateTime? start, DateTime? end, string? granularity)
    {
        var s = (start ?? DateTime.UtcNow.Date.AddDays(-30)).Date;
        var e = (end ?? DateTime.UtcNow.Date).Date;
        if (e <= s) e = s.AddDays(1);
        granularity = string.IsNullOrWhiteSpace(granularity) ? "Daily" : granularity;

        var bundle = await _service.GetBundleAsync(s, e, granularity);

        var pdfBytes = BuildReportPdf(bundle);
        var fileName = $"RentalSphere-Report-{s:yyyy-MM-dd}-to-{e:yyyy-MM-dd}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    private static byte[] BuildReportPdf(ReportsBundleDto bundle)
    {
        var currencySymbol = "₱"; // ₱

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Header().Text("RentalSphere - Reports")
                    .SemiBold().FontSize(16).FontColor(Colors.Blue.Medium);

                page.Content().Column(column =>
                {
                    // --- Date range + granularity ---
                    column.Item().Text($"Period: {bundle.Start:yyyy-MM-dd} to {bundle.End:yyyy-MM-dd}  |  Granularity: {bundle.Granularity}")
                        .FontSize(10).FontColor(Colors.Grey.Medium);

                    // --- 6 metrics table ---
                    column.Item().PaddingVertical(5).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(MetricsHeaderStyle).Text("Metric").SemiBold().FontSize(9);
                            header.Cell().Element(MetricsHeaderStyle).Text("Value").SemiBold().FontSize(9).AlignRight();
                        });

                        table.Cell().Element(MetricsCellStyle).Text("Revenue Realized");
                        table.Cell().Element(MetricsCellStyle).Text($"{currencySymbol}{bundle.TotalRevenue:N2}").AlignRight();

                        table.Cell().Element(MetricsCellStyle).Text("Maintenance Expenses");
                        table.Cell().Element(MetricsCellStyle).Text($"{currencySymbol}{bundle.TotalMaintenanceExpenses:N2}").AlignRight();

                        table.Cell().Element(MetricsCellStyle).Text("Net Revenue");
                        table.Cell().Element(MetricsCellStyle).Text($"{currencySymbol}{bundle.NetRevenue:N2}").AlignRight();

                        table.Cell().Element(MetricsCellStyle).Text("Outstanding");
                        table.Cell().Element(MetricsCellStyle).Text($"{currencySymbol}{bundle.TotalOutstanding:N2}").AlignRight();

                        table.Cell().Element(MetricsCellStyle).Text("Late Returns");
                        table.Cell().Element(MetricsCellStyle).Text($"{bundle.LateReturnCount} rentals").AlignRight();

                        table.Cell().Element(MetricsCellStyle).Text("Damage Amount");
                        table.Cell().Element(MetricsCellStyle).Text($"{currencySymbol}{bundle.TotalDamageAmount:N2}").AlignRight();
                    });

                    // --- Revenue by period table ---
                    column.Item().PaddingTop(10).Text("Revenue by Period").SemiBold().FontSize(12);

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3); // Period
                            columns.RelativeColumn();  // Invoices
                            columns.RelativeColumn();  // Rentals
                            columns.RelativeColumn();  // Revenue
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(PeriodHeaderStyle).Text("Period").SemiBold().FontSize(8);
                            header.Cell().Element(PeriodHeaderStyle).Text("Invoices").SemiBold().FontSize(8).AlignRight();
                            header.Cell().Element(PeriodHeaderStyle).Text("Rentals").SemiBold().FontSize(8).AlignRight();
                            header.Cell().Element(PeriodHeaderStyle).Text("Revenue").SemiBold().FontSize(8).AlignRight();
                        });

                        foreach (var r in bundle.Revenue)
                        {
                            table.Cell().Element(PeriodCellStyle).Text(r.Period).FontSize(8);
                            table.Cell().Element(PeriodCellStyle).Text(r.InvoiceCount.ToString()).AlignRight().FontSize(8);
                            table.Cell().Element(PeriodCellStyle).Text(r.RentalTransactionCount.ToString()).AlignRight().FontSize(8);
                            table.Cell().Element(PeriodCellStyle).Text($"{currencySymbol}{r.Revenue:N2}").AlignRight().FontSize(8);
                        }

                        // total row
                        table.Cell().Element(PeriodCellStyle).Text("Total").SemiBold().FontSize(8);
                        table.Cell().Element(PeriodCellStyle).Text(bundle.Revenue.Sum(x => x.InvoiceCount).ToString()).AlignRight().SemiBold().FontSize(8);
                        table.Cell().Element(PeriodCellStyle).Text(bundle.Revenue.Sum(x => x.RentalTransactionCount).ToString()).AlignRight().SemiBold().FontSize(8);
                        table.Cell().Element(PeriodCellStyle).Text($"{currencySymbol}{bundle.TotalRevenue:N2}").AlignRight().SemiBold().FontSize(8);
                    });
                });

                page.Footer().AlignCenter().DefaultTextStyle(style => style
                    .FontSize(8)
                    .FontColor(Colors.Grey.Medium))
                .Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static IContainer MetricsHeaderStyle(IContainer container) =>
        container.PaddingVertical(3).PaddingHorizontal(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);

    private static IContainer MetricsCellStyle(IContainer container) =>
        container.PaddingVertical(3).PaddingHorizontal(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);

    private static IContainer PeriodHeaderStyle(IContainer container) =>
        container.PaddingVertical(3).PaddingHorizontal(5);

    private static IContainer PeriodCellStyle(IContainer container) =>
        container.PaddingVertical(3).PaddingHorizontal(5);
}
