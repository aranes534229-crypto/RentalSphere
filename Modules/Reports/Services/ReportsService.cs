using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Modules.Reports.DTOs;

namespace RentalSphere.Modules.Reports.Services;

public interface IReportsService
{
    Task<ReportsBundleDto> GetBundleAsync(DateTime start, DateTime end, string granularity);
}

public class ReportsService : IReportsService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _current;

    public ReportsService(ApplicationDbContext db, ICurrentUser current)
    {
        _db = db;
        _current = current;
    }

    public async Task<ReportsBundleDto> GetBundleAsync(DateTime start, DateTime end, string granularity)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view reports.");

        var s = start.Date;
        var e = end.Date;
        if (e <= s) throw new InvalidOperationException("End date must be after start date.");
        if ((e - s).TotalDays > 365)
            throw new InvalidOperationException("Report window cannot exceed 365 days.");

        if (granularity is not ("Daily" or "Weekly" or "Monthly"))
            granularity = "Daily";

        var bundle = new ReportsBundleDto
        {
            Start = s,
            End = e,
            Granularity = granularity,
        };

        // DbContext is not thread-safe; run sequentially.
        bundle.Revenue = await RevenueByPeriodAsync(s, e, granularity);
        bundle.Utilization = await EquipmentUtilizationAsync(s, e);
        bundle.OutstandingBalances = await OutstandingBalancesAsync();
        bundle.LateReturns = await LateReturnsAsync(s, e);
        bundle.DamageTrends = await DamageTrendsAsync(s, e, granularity);

        bundle.TotalRevenue = bundle.Revenue.Sum(r => r.Revenue);
        bundle.TotalOutstanding = bundle.OutstandingBalances.Sum(o => o.Outstanding);
        bundle.LateReturnCount = bundle.LateReturns.Count;
        bundle.TotalDamageAmount = bundle.DamageTrends.Sum(d => d.DamageAmount);

        return bundle;
    }

    // ---- 1. Revenue by period ----
    private async Task<List<RevenuePeriodRowDto>> RevenueByPeriodAsync(
        DateTime start, DateTime end, string granularity)
    {
        // Aggregate paid invoice amounts (TotalAmount - WaivedAmount) by InvoiceDate bucket.
        // Pull a flat projection first; bucket in memory (window is bounded).
        var inv = await _db.Invoices
            .Where(i => i.InvoiceDate >= start && i.InvoiceDate < end.AddDays(1)
                     && i.Status != Billing.Models.InvoiceStatus.Voided)
            .Select(i => new
            {
                i.InvoiceID,
                i.InvoiceDate,
                i.TotalAmount,
                i.AmountPaid,
                i.Status,
                i.RentalTransactionID,
            })
            .ToListAsync();

        return inv
            .GroupBy(i => BucketKey(i.InvoiceDate, granularity))
            .Select(g => new RevenuePeriodRowDto
            {
                Period = g.Key.Label,
                PeriodStart = g.Key.Start,
                Revenue = g.Sum(x => x.AmountPaid),                       // realized cash
                InvoiceCount = g.Count(),
                RentalTransactionCount = g.Select(x => x.RentalTransactionID).Distinct().Count(),
            })
            .OrderBy(r => r.PeriodStart)
            .ToList();
    }

    // ---- 2. Equipment utilization ----
    private async Task<List<UtilizationRowDto>> EquipmentUtilizationAsync(
        DateTime start, DateTime end)
    {
        var dayCount = (int)(end - start).TotalDays;
        if (dayCount <= 0) return new();

        var equipment = await _db.Equipment
            .Include(e => e.Category)
            .Where(e => e.Status != Equipment.Models.EquipmentStatus.Retired)
            .OrderBy(e => e.Name)
            .ToListAsync();

        var equipmentIds = equipment.Select(e => e.EquipmentID).ToList();

        var totals = await _db.EquipmentItems
            .Where(i => equipmentIds.Contains(i.EquipmentID))
            .GroupBy(i => i.EquipmentID)
            .Select(g => new { EquipmentID = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EquipmentID, x => x.Count);

        // Reserved unit-days from confirmed/pending reservations intersecting the window.
        var reservations = await _db.ReservationItems
            .Where(ri => equipmentIds.Contains(ri.EquipmentID)
                      && (ri.Reservation.Status == ReservationManagement.Models.ReservationStatus.Pending
                       || ri.Reservation.Status == ReservationManagement.Models.ReservationStatus.Confirmed
                       || ri.Reservation.Status == ReservationManagement.Models.ReservationStatus.CheckedOut)
                      && ri.Reservation.RentalStartDate < end.AddDays(1)
                      && ri.Reservation.RentalEndDate > start)
            .Select(ri => new
            {
                ri.EquipmentID,
                ri.Quantity,
                ri.Reservation.RentalStartDate,
                ri.Reservation.RentalEndDate,
            })
            .ToListAsync();

        // Active rentals also consume capacity.
        var activeRentals = await _db.RentalTransactionItems
            .Where(rti => equipmentIds.Contains(rti.EquipmentID)
                       && rti.RentalTransaction.Status == RentalTransaction.Models.RentalTransactionStatus.Active)
            .Select(rti => new
            {
                rti.EquipmentID,
                rti.Quantity,
                rti.RentalTransaction.CheckoutDate,
                rti.RentalTransaction.ExpectedReturnDate,
            })
            .ToListAsync();

        var rows = new List<UtilizationRowDto>();
        foreach (var eq in equipment)
        {
            var totalUnits = totals.GetValueOrDefault(eq.EquipmentID, 0);
            var availableDays = totalUnits * dayCount;

            var reserved = 0;
            foreach (var r in reservations.Where(r => r.EquipmentID == eq.EquipmentID))
            {
                var s0 = r.RentalStartDate.Date < start ? start : r.RentalStartDate.Date;
                var e0 = r.RentalEndDate.Date > end ? end : r.RentalEndDate.Date;
                var days = Math.Max(0, (int)(e0 - s0).TotalDays);
                reserved += days * r.Quantity;
            }

            foreach (var r in activeRentals.Where(r => r.EquipmentID == eq.EquipmentID))
            {
                var s0 = r.CheckoutDate.Date < start ? start : r.CheckoutDate.Date;
                var e0 = r.ExpectedReturnDate.Date > end ? end : r.ExpectedReturnDate.Date;
                var days = Math.Max(0, (int)(e0 - s0).TotalDays);
                reserved += days * r.Quantity;
            }

            var pct = availableDays <= 0 ? 0 : Math.Round((decimal)reserved / availableDays * 100m, 1);
            if (pct > 100m) pct = 100m;

            rows.Add(new UtilizationRowDto
            {
                EquipmentID = eq.EquipmentID,
                EquipmentName = eq.Name,
                CategoryName = eq.Category?.Name ?? string.Empty,
                TotalUnits = totalUnits,
                ReservedDays = reserved,
                AvailableDays = availableDays,
                UtilizationPercent = pct,
            });
        }

        return rows.OrderByDescending(r => r.UtilizationPercent).ToList();
    }

    // ---- 3. Outstanding balances ----
    private async Task<List<OutstandingBalanceRowDto>> OutstandingBalancesAsync()
    {
        var today = DateTime.UtcNow.Date;

        var rows = await _db.Invoices
            .Include(i => i.Customer)
            .Where(i => i.Status == Billing.Models.InvoiceStatus.Unpaid
                     || i.Status == Billing.Models.InvoiceStatus.PartiallyPaid)
            .OrderBy(i => i.DueDate)
            .Select(i => new
            {
                i.InvoiceID,
                i.InvoiceNumber,
                i.CustomerID,
                CustomerFirst = i.Customer!.FirstName,
                CustomerLast = i.Customer!.LastName,
                i.TotalAmount,
                i.AmountPaid,
                i.DueDate,
                i.Status,
            })
            .ToListAsync();

        return rows.Select(r => new OutstandingBalanceRowDto
        {
            InvoiceID = r.InvoiceID,
            InvoiceNumber = r.InvoiceNumber,
            CustomerID = r.CustomerID,
            CustomerName = $"{r.CustomerFirst} {r.CustomerLast}".Trim(),
            TotalAmount = r.TotalAmount,
            AmountPaid = r.AmountPaid,
            Outstanding = r.TotalAmount - r.AmountPaid,
            DueDate = r.DueDate ?? today,
            DaysOverdue = (r.DueDate.HasValue && today > r.DueDate.Value)
                ? (int)(today - r.DueDate.Value).TotalDays
                : 0,
            Status = r.Status.ToString(),
        }).ToList();
    }

    // ---- 4. Late returns ----
    private async Task<List<LateReturnRowDto>> LateReturnsAsync(DateTime start, DateTime end)
    {
        // Late = Returned past ExpectedReturnDate, where the transaction completed in-window.
        // Also include Active rentals where ExpectedReturnDate has passed (open late).
        var completedLate = await _db.RentalTransactions
            .Include(t => t.Customer)
            .Include(t => t.Items)
                .ThenInclude(i => i.Equipment)
            .Where(t => t.ReturnDate != null
                     && t.ReturnDate!.Value.Date > t.ExpectedReturnDate.Date
                     && t.ReturnDate!.Value.Date >= start
                     && t.ReturnDate!.Value.Date < end.AddDays(1))
            .Select(t => new
            {
                t.RentalTransactionID,
                CustomerFirst = t.Customer!.FirstName,
                CustomerLast = t.Customer!.LastName,
                t.ExpectedReturnDate,
                t.ReturnDate,
                EquipmentList = t.Items.Select(i => $"{i.Equipment!.Name} ×{i.Quantity}").ToList(),
            })
            .ToListAsync();

        var rows = completedLate.Select(t => new LateReturnRowDto
        {
            RentalTransactionID = t.RentalTransactionID,
            CustomerName = $"{t.CustomerFirst} {t.CustomerLast}".Trim(),
            EquipmentSummary = string.Join(", ", t.EquipmentList),
            ExpectedReturnDate = t.ExpectedReturnDate,
            ActualReturnDate = t.ReturnDate,
            DaysLate = Math.Max(1, (int)Math.Ceiling((t.ReturnDate!.Value.Date - t.ExpectedReturnDate.Date).TotalDays)),
            LateFee = 0m, // set below via second pass if needed
        }).ToList();

        // Augment LateFee from invoice lines where available.
        var ids = rows.Select(r => r.RentalTransactionID).ToList();
        if (ids.Count > 0)
        {
            var invLines = await _db.InvoiceLines
                .Where(l => l.LineType == Billing.Models.InvoiceLineType.LateFee
                         && ids.Contains(l.Invoice!.RentalTransactionID))
                .Select(l => new { l.Invoice!.RentalTransactionID, l.LineTotal })
                .ToListAsync();
            var feeMap = invLines.ToDictionary(x => x.RentalTransactionID, x => x.LineTotal);
            foreach (var r in rows)
            {
                r.LateFee = feeMap.TryGetValue(r.RentalTransactionID, out var fee) ? fee : 0m;
            }
        }

        return rows.OrderByDescending(r => r.DaysLate).ToList();
    }

    // ---- 4b. Damage trends ----
    private async Task<List<DamageTrendRowDto>> DamageTrendsAsync(
        DateTime start, DateTime end, string granularity)
    {
        var rows = await _db.DamagePenalties
            .Where(d => d.ReportedAt >= start && d.ReportedAt < end.AddDays(1))
            .Select(d => new { d.ReportedAt, d.Amount, d.WaivedAmount, d.RentalTransactionID })
            .ToListAsync();

        return rows
            .GroupBy(d => BucketKey(d.ReportedAt, granularity))
            .Select(g => new DamageTrendRowDto
            {
                Period = g.Key.Label,
                PeriodStart = g.Key.Start,
                DamageAmount = g.Sum(x => x.Amount - x.WaivedAmount),
                PenaltyCount = g.Count(),
                AffectedTransactions = g.Select(x => x.RentalTransactionID).Distinct().Count(),
            })
            .OrderBy(r => r.PeriodStart)
            .ToList();
    }

    // ---- helpers ----
    private static (string Label, DateTime Start) BucketKey(DateTime when, string granularity)
    {
        var d = when.Date;
        return granularity switch
        {
            "Weekly" => ($"{d.Year}-W{ISOWeek(d):D2}", ISOWeekStart(d)),
            "Monthly" => ($"{d.Year}-{d.Month:D2}", new DateTime(d.Year, d.Month, 1)),
            _ => (d.ToString("yyyy-MM-dd"), d),
        };
    }

    private static int ISOWeek(DateTime d)
    {
        var day = (int)System.Globalization.CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(d);
        if (day == 0) day = 7;
        return System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
            d, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
    }

    private static DateTime ISOWeekStart(DateTime d)
    {
        int diff = (7 + (int)d.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return d.AddDays(-diff).Date;
    }
}
