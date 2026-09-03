using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.EquipmentAvailability.DTOs;
using RentalSphere.Modules.ReservationManagement.Models;

namespace RentalSphere.Modules.EquipmentAvailability.Services;

public interface IAvailabilityService
{
    Task<AvailabilityCalendarDto> GetCalendarAsync(DateTime start, DateTime end, int? categoryId = null);
}

/// <summary>
/// Pure read. Computes per-equipment per-day available count by subtracting items that
/// are InMaintenance today and Confirmed-reservation overlap across the date range.
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

        // Snapshot totals: total per-unit count per equipment.
        var totals = await _db.EquipmentItems
            .Where(i => equipmentIds.Contains(i.EquipmentID))
            .GroupBy(i => i.EquipmentID)
            .Select(g => new { EquipmentID = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EquipmentID, x => x.Count);

        // Pre-pull reservations that overlap the requested window for these equipment rows.
        var overlapping = await _db.ReservationItems
            .Where(ri => equipmentIds.Contains(ri.EquipmentID)
                      && (ri.Reservation.Status == ReservationStatus.Pending
                          || ri.Reservation.Status == ReservationStatus.Confirmed
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

        // Maintenance pulls: items in InMaintenance across the window. A unit pulled for
        // maintenance on day 5 reduces availability from day 5 onwards until closed.
        var maintenance = await _db.EquipmentItems
            .Where(i => equipmentIds.Contains(i.EquipmentID) && i.AvailabilityStatus == AvailabilityStatus.InMaintenance)
            .Select(i => new { i.EquipmentID, i.ItemID })
            .ToListAsync();

        var rows = new List<AvailabilityRowDto>();
        foreach (var eq in equipment)
        {
            var cells = new List<AvailabilityCellDto>(dates.Count);
            var baseline = totals.GetValueOrDefault(eq.EquipmentID, 0);
            var inMaintenance = maintenance.Count(m => m.EquipmentID == eq.EquipmentID);

            foreach (var d in dates)
            {
                var dEnd = d.AddDays(1);
                var locked = overlapping
                    .Where(o => o.EquipmentID == eq.EquipmentID
                             && o.RentalStartDate < dEnd
                             && o.RentalEndDate > d)
                    .Sum(o => o.Quantity);

                var available = Math.Max(0, baseline - inMaintenance - locked);
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
