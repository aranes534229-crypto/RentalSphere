using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.EquipmentAvailability.DTOs;
using RentalSphere.Modules.Maintenance.Models;
using RentalSphere.Modules.ReservationManagement.Models;

namespace RentalSphere.Modules.EquipmentAvailability.Services;

public interface IAvailabilityService
{
    Task<AvailabilityCalendarDto> GetCalendarAsync(DateTime start, DateTime end, int? categoryId = null);
}

/// <summary>
/// Pure read. Computes per-equipment per-day available count from the catalog
/// StockQuantity, subtracting items InMaintenance and overlapping
/// Confirmed/CheckedOut reservations. Pending requests do NOT hold stock.
/// </summary>
public class AvailabilityService : IAvailabilityService
{
    private readonly ApplicationDbContext _db;

    public AvailabilityService(ApplicationDbContext db) { _db = db; }

    public async Task<AvailabilityCalendarDto> GetCalendarAsync(DateTime start, DateTime end, int? categoryId = null)
    {
        if (end <= start)
            throw new InvalidOperationException("End date must be after start date.");
        if ((end - start).TotalDays > 60)
            throw new InvalidOperationException("Date range cannot exceed 60 days.");

        var dates = EnumerateDates(start, end).ToList();

        var equipment = await _db.Equipment
            .Include(e => e.Category)
            .Where(e => e.Status != EquipmentStatus.Retired)
            .Where(e => categoryId == null || e.CategoryID == categoryId.Value)
            .OrderBy(e => e.Name)
            .ToListAsync();

        var equipmentIds = equipment.Select(e => e.EquipmentID).ToList();

        // Baseline = the catalog's StockQuantity. This is the same number the
        // equipment list page shows and what the customer expects to see on a
        // day with no holds. We then subtract per-day holds:
        //   - open MaintenanceRecords, but only on days inside their
        //     [StartedAt, ExpectedEnd] window (see below) — not a blanket count
        //     of InMaintenance items, so dates past ExpectedEnd free up the unit
        //   - confirmed / checked-out reservations that overlap each column
        //     (pending requests do NOT hold stock — staff must approve first)
        // Using StockQuantity (not a count of Available items) keeps the calendar
        // header in lockstep with the equipment list even when per-item status
        // has drifted away from StockQuantity.
        var totals = equipment.ToDictionary(e => e.EquipmentID, e => e.StockQuantity);

        // Per-day maintenance pull. An item in maintenance reduces availability
        // only for days inside the maintenance window [StartedAt, ExpectedEnd].
        // For dates after ExpectedEnd, the unit is treated as available unless the
        // record is still open past its expected date (no ExpectedEnd yet, or
        // Status is still InProgress past ExpectedEnd) — then it still holds
        // stock. We pre-load the open records that touch the requested window,
        // then check each day individually. Window is bounded (≤ 60 days) so the
        // in-memory check is cheap. Closed records (Status = Completed) are
        // excluded — once closed, the unit is back in the Available pool.
        var maintRecords = await _db.MaintenanceRecords
            .Where(m => m.Status == MaintenanceStatus.InProgress
                     && equipmentIds.Contains(m.EquipmentItem!.EquipmentID)
                     && m.StartedAt < end
                     && (m.ExpectedEnd == null || m.ExpectedEnd > start))
            .Select(m => new
            {
                m.EquipmentItemID,
                EquipmentID = m.EquipmentItem!.EquipmentID,
                m.StartedAt,
                m.ExpectedEnd,
            })
            .ToListAsync();

        // For each open record, compute the day-range it covers within [start, end).
        // A record holds stock for every day d where
        //   d < end  AND  d >= StartedAt.Date  AND  (ExpectedEnd == null OR d < ExpectedEnd.Date)
        // The third clause is what requirement (2) is about: dates after ExpectedEnd
        // are released — unless ExpectedEnd is null (record still open with no
        // expected end set), in which case the record still holds stock.
        // We expand to a per-equipment per-day set of held unit-IDs.
        var heldByDate = new Dictionary<(int EquipmentID, DateTime Date), int>();
        foreach (var m in maintRecords)
        {
            var from = m.StartedAt.Date < start ? start : m.StartedAt.Date;
            // Effective end: if ExpectedEnd is null OR the record is still open
            // past ExpectedEnd, the hold extends to end of the window. Once the
            // window ends, the calendar can't say either way — but the unit is
            // only counted InMaintenance on the per-item status until staff
            // explicitly closes the record.
            var toExclusive = m.ExpectedEnd.HasValue
                ? (m.ExpectedEnd.Value.Date > end ? end : m.ExpectedEnd.Value.Date)
                : end;
            if (toExclusive <= from) continue;
            for (var d = from; d < toExclusive; d = d.AddDays(1))
            {
                var key = (m.EquipmentID, d);
                heldByDate[key] = heldByDate.GetValueOrDefault(key) + 1;
            }
        }

        // Pre-pull reservations that overlap the requested window for these equipment rows.
        // Only confirmed / checked-out holds deduct from the calendar. Pending is a customer
        // intent, not a hold — staff must approve before it consumes stock.
        var overlapping = await _db.ReservationItems
            .Where(ri => equipmentIds.Contains(ri.EquipmentID)
                      && (ri.Reservation.Status == ReservationStatus.Confirmed
                          || ri.Reservation.Status == ReservationStatus.CheckedOut)
                      && ri.Reservation.RentalStartDate < end
                      && ri.Reservation.RentalEndDate > start)
            .Select(ri => new
            {
                ri.EquipmentID,
                ri.Quantity,
                ri.Reservation.RentalStartDate,
                ri.Reservation.RentalEndDate,
            })
            .ToListAsync();

        // Maintenance pulls are excluded from `totals` (baseline comes from
        // StockQuantity), so we need to subtract them per day.

        var rows = new List<AvailabilityRowDto>();
        foreach (var eq in equipment)
        {
            var cells = new List<AvailabilityCellDto>(dates.Count);
            var baseline = totals.GetValueOrDefault(eq.EquipmentID, 0);

            foreach (var d in dates)
            {
                // Half-open overlap test: a reservation [start, end) holds the unit
                // for the start day, every full day in between, and the end day.
                // (start < date+1) ⇔ (start <= date) and (end > date). We sum only
                // the holds that intersect this specific column, so a reservation
                // that ends before `d` or starts after `d+1` is correctly ignored
                // for that cell. The aggregate is per-cell, not per-row.
                var dEnd = d.AddDays(1);
                var locked = overlapping
                    .Where(o => o.EquipmentID == eq.EquipmentID
                             && o.RentalStartDate < dEnd
                             && o.RentalEndDate > d)
                    .Sum(o => o.Quantity);

                // Maintenance hold is per-day, derived from the open records'
                // [StartedAt, ExpectedEnd) window — not a blanket count of
                // InMaintenance items. See heldByDate above.
                var maintHeld = heldByDate.GetValueOrDefault((eq.EquipmentID, d), 0);

                var available = Math.Max(0, baseline - maintHeld - locked);
                cells.Add(new AvailabilityCellDto { Date = d, Available = available });
            }

            rows.Add(new AvailabilityRowDto
            {
                EquipmentID = eq.EquipmentID,
                EquipmentName = eq.Name,
                CategoryName = eq.Category?.Name ?? string.Empty,
                TotalUnits = baseline,
                Cells = cells,
            });
        }

        return new AvailabilityCalendarDto
        {
            Start = start,
            End = end,
            Dates = dates,
            Rows = rows,
        };
    }

    private static IEnumerable<DateTime> EnumerateDates(DateTime start, DateTime end)
    {
        for (var d = start.Date; d < end.Date; d = d.AddDays(1))
            yield return d;
    }
}
