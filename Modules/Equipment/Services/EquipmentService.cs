using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Equipment.DTOs;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.ViewModels;

namespace RentalSphere.Modules.Equipment.Services;

public interface IEquipmentService
{
    Task<List<EquipmentListItemDto>> ListAsync(string? search = null, int? categoryId = null);
    Task<(List<EquipmentListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? search, int? categoryId, string? sort, int skip, int take);
    Task<EquipmentDetailsDto> GetDetailsAsync(int id, DateTime? rangeStart = null, DateTime? rangeEnd = null);
    Task<List<EquipmentListItemDto>> ListActiveAsync(string? search = null, int? categoryId = null);
    Task<int> CreateAsync(EquipmentCreateDto dto, string actorUserId);
    Task UpdateAsync(EquipmentUpdateDto dto, string actorUserId);
    Task DeleteAsync(int id, string actorUserId);
    Task<List<EquipmentLookupDto>> GetLookupAsync();
}

public record EquipmentLookupDto(int EquipmentID, string Name, decimal DailyRate);
public record EquipmentItemLookupDto(int ItemID, int EquipmentID, string EquipmentName, string SerialNumber, string Status);

public class EquipmentService : IEquipmentService
{
    private readonly IEquipmentRepository _repo;
    private readonly ICategoryRepository _categories;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;
    private readonly RentalSphere.Data.ApplicationDbContext _db;

    public EquipmentService(
        IEquipmentRepository repo,
        ICategoryRepository categories,
        ICurrentUser current,
        IAuditLogger audit,
        RentalSphere.Data.ApplicationDbContext db)
    {
        _repo = repo;
        _categories = categories;
        _current = current;
        _audit = audit;
        _db = db;
    }

    public async Task<List<EquipmentListItemDto>> ListAsync(string? search = null, int? categoryId = null)
    {
        var items = await _repo.ListAsync(search, categoryId);
        return items.Select(MapList).ToList();
    }

    public async Task<(List<EquipmentListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? search, int? categoryId, string? sort, int skip, int take)
    {
        var all = await _repo.ListAsync(search, categoryId);
        var total = all.Count;

        // Apply sort (simple in-memory: page sizes are small enough).
        var sorted = sort?.ToLowerInvariant() switch
        {
            "name" or "name_desc" => sort == "name_desc"
                ? all.OrderByDescending(e => e.Name).ToList()
                : all.OrderBy(e => e.Name).ToList(),
            "rate" => all.OrderBy(e => e.DailyRate).ThenBy(e => e.Name).ToList(),
            "rate_desc" => all.OrderByDescending(e => e.DailyRate).ThenBy(e => e.Name).ToList(),
            "stock" => all.OrderBy(e => e.StockQuantity).ToList(),
            "stock_desc" => all.OrderByDescending(e => e.StockQuantity).ToList(),
            "added" => all.OrderBy(e => e.DateAdded).ToList(),
            "added_desc" => all.OrderByDescending(e => e.DateAdded).ToList(),
            _ => all.OrderBy(e => e.Name).ToList(),
        };

        var page = sorted.Skip(skip).Take(take).Select(MapList).ToList();
        return (page, total);
    }

    public async Task<List<EquipmentListItemDto>> ListActiveAsync(string? search = null, int? categoryId = null)
    {
        // Customers only see Available equipment.
        var all = await ListAsync(search, categoryId);
        return all.Where(e => e.Status == "Available").ToList();
    }

    public async Task<EquipmentDetailsDto> GetDetailsAsync(int id, DateTime? rangeStart = null, DateTime? rangeEnd = null)
    {
        var e = await _repo.GetWithItemsAsync(id)
            ?? throw new NotFoundException($"Equipment {id} not found.");

        var totalStock = e.StockQuantity > 0 ? e.StockQuantity : e.Items.Count;
        var bookedCount = 0;

        // ponytail: TotalStock pulls from the catalog StockQuantity (same number shown
        // on the page) so the denominator matches the "Stock quantity" line above. The
        // per-unit EquipmentItems count is a separate value; we use it only as a
        // fallback when StockQuantity is unset.
        var s = (rangeStart ?? DateTime.UtcNow.Date);
        var end = (rangeEnd ?? s.AddDays(1)).Date;

        if (totalStock > 0)
        {
            bookedCount = await _db.ReservationItems
                .Where(ri => ri.EquipmentID == id
                    && (ri.Reservation.Status == RentalSphere.Modules.ReservationManagement.Models.ReservationStatus.Pending
                        || ri.Reservation.Status == RentalSphere.Modules.ReservationManagement.Models.ReservationStatus.Confirmed
                        || ri.Reservation.Status == RentalSphere.Modules.ReservationManagement.Models.ReservationStatus.CheckedOut)
                    && ri.Reservation.RentalStartDate < end
                    && ri.Reservation.RentalEndDate > s)
                .SumAsync(ri => (int?)ri.Quantity) ?? 0;
        }

        var available = Math.Max(0, totalStock - bookedCount);

        return new EquipmentDetailsDto
        {
            EquipmentID = e.EquipmentID,
            Name = e.Name,
            CategoryID = e.CategoryID,
            CategoryName = e.Category.Name,
            Description = e.Description,
            DailyRate = e.DailyRate,
            StockQuantity = e.StockQuantity,
            Status = e.Status.ToString(),
            DateAdded = e.DateAdded,
            ImageURL = e.ImageURL,
            SerialPrefix = e.SerialPrefix,
            TotalStock = totalStock,
            BookedCount = bookedCount,
            AvailableCount = available,
            RangeStart = s,
            RangeEnd = end,
        };
    }

    public async Task<int> CreateAsync(EquipmentCreateDto dto, string actorUserId)
    {
        if (await _categories.GetAsync(dto.CategoryID) is null)
            throw new NotFoundException($"Category {dto.CategoryID} not found.");

        var e = new EquipmentCatalog
        {
            Name = dto.Name.Trim(),
            CategoryID = dto.CategoryID,
            Description = dto.Description,
            DailyRate = dto.DailyRate,
            StockQuantity = dto.StockQuantity,
            Status = ParseStatus(dto.Status),
            DateAdded = DateTime.UtcNow,
            ImageURL = dto.ImageURL,
            SerialPrefix = string.IsNullOrWhiteSpace(dto.SerialPrefix) ? null : dto.SerialPrefix.Trim().ToUpperInvariant(),
        };
        await _repo.AddAsync(e);
        await _repo.SaveChangesAsync();

        // ponytail: auto-generate EquipmentItem rows so the catalog count
        // matches the per-unit count from the start. If the Admin later wants
        // to retire individual units, they do that through Equipment Items.
        if (dto.StockQuantity > 0)
        {
            await BackfillItemsAsync(e.EquipmentID, dto.StockQuantity, actorUserId);
        }

        await _audit.LogAsync(
            actorUserId, "Equipment.Create", "Equipment", e.EquipmentID.ToString(),
            newValues: new { e.Name, e.CategoryID, e.DailyRate, e.StockQuantity, e.SerialPrefix, Status = e.Status.ToString() });

        return e.EquipmentID;
    }

    public async Task UpdateAsync(EquipmentUpdateDto dto, string actorUserId)
    {
        var e = await _repo.GetAsync(dto.EquipmentID)
            ?? throw new NotFoundException($"Equipment {dto.EquipmentID} not found.");

        var oldValues = new
        {
            e.Name, e.CategoryID, e.DailyRate, e.StockQuantity,
            Status = e.Status.ToString(),
        };

        // ponytail: if the new StockQuantity would put the catalog below the
        // current EquipmentItem count, refuse the save. The Admin must retire
        // or delete items individually instead of shrinking stock in bulk.
        if (dto.StockQuantity < e.StockQuantity)
        {
            var currentTracked = await _db.EquipmentItems
                .CountAsync(i => i.EquipmentID == dto.EquipmentID);
            if (dto.StockQuantity < currentTracked)
            {
                throw new InvalidOperationException(
                    $"Cannot reduce stock below the current item count ({currentTracked}). " +
                    "Retire or delete individual units from the Equipment Items module instead.");
            }
        }

        e.Name = dto.Name.Trim();
        e.CategoryID = dto.CategoryID;
        e.Description = dto.Description;
        e.DailyRate = dto.DailyRate;
        e.StockQuantity = dto.StockQuantity;
        e.Status = ParseStatus(dto.Status);
        e.ImageURL = dto.ImageURL;
        e.SerialPrefix = string.IsNullOrWhiteSpace(dto.SerialPrefix) ? null : dto.SerialPrefix.Trim().ToUpperInvariant();

        _repo.Update(e);
        await _repo.SaveChangesAsync();

        // If the new StockQuantity is higher than the current EquipmentItem count,
        // generate the difference so the catalog and inventory stay in lockstep.
        var trackedAfter = await _db.EquipmentItems
            .CountAsync(i => i.EquipmentID == dto.EquipmentID);
        if (dto.StockQuantity > trackedAfter)
        {
            var toAdd = dto.StockQuantity - trackedAfter;
            await BackfillItemsAsync(dto.EquipmentID, toAdd, actorUserId);
        }

        await _audit.LogAsync(
            actorUserId, "Equipment.Update", "Equipment", e.EquipmentID.ToString(),
            oldValues: oldValues,
            newValues: new
            {
                e.Name, e.CategoryID, e.DailyRate, e.StockQuantity,
                Status = e.Status.ToString(),
            });
    }

    private async Task BackfillItemsAsync(int equipmentId, int count, string actorUserId)
    {
        if (count <= 0) return;

        var eq = await _repo.GetAsync(equipmentId)
            ?? throw new NotFoundException($"Equipment {equipmentId} not found.");

        var prefix = !string.IsNullOrWhiteSpace(eq.SerialPrefix) ? eq.SerialPrefix : "EQ-";

        // Pull all existing serials for this equipment so the generator can
        // pick the smallest missing counter (gap-filling).
        var existingSerials = await _db.EquipmentItems
            .Where(i => i.EquipmentID == equipmentId)
            .Select(i => i.SerialNumber)
            .ToListAsync();

        var next = SerialNumberGenerator.NextCounter(existingSerials, prefix);
        var now = DateTime.UtcNow;
        for (var i = 0; i < count; i++)
        {
            var serial = SerialNumberGenerator.Build(prefix, next + i);
            _db.EquipmentItems.Add(new EquipmentItem
            {
                EquipmentID = equipmentId,
                SerialNumber = serial,
                AvailabilityStatus = AvailabilityStatus.Available,
                LastStatusChange = now,
            });
        }
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "EquipmentItem.Backfill", "Equipment", equipmentId.ToString(),
            newValues: new { Added = count, Reason = "StockQuantity change" });
    }

    public async Task DeleteAsync(int id, string actorUserId)
    {
        // Service-layer guard: only Admin can delete equipment.
        if (!_current.IsAdmin)
            throw new ForbiddenException("Only Admin can delete equipment.");

        var e = await _repo.GetWithItemsAsync(id)
            ?? throw new NotFoundException($"Equipment {id} not found.");

        // ponytail: refuse to delete if any unit is currently in a customer-facing
        // state. Force staff to return / cancel those rentals first, otherwise
        // we'd leave rental records pointing at a deleted catalog row.
        var inFlight = e.Items
            .Where(i => i.AvailabilityStatus == AvailabilityStatus.Reserved
                     || i.AvailabilityStatus == AvailabilityStatus.CheckedOut)
            .ToList();
        if (inFlight.Any())
        {
            var serials = string.Join(", ", inFlight.Select(i => i.SerialNumber));
            throw new InvalidOperationException(
                $"Cannot delete this equipment because units are currently reserved or checked out by a customer ({serials}).");
        }

        // Soft-cascade: any active items get marked Retired first.
        foreach (var item in e.Items)
        {
            if (item.AvailabilityStatus != AvailabilityStatus.Retired)
            {
                item.AvailabilityStatus = AvailabilityStatus.Retired;
                item.LastStatusChange = DateTime.UtcNow;
            }
        }

        var snapshot = new { e.Name, e.CategoryID, e.StockQuantity };
        await _repo.RemoveAsync(e);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Equipment.Delete", "Equipment", id.ToString(),
            oldValues: snapshot);
    }

    public async Task<List<EquipmentLookupDto>> GetLookupAsync()
    {
        var items = await _repo.ListAsync();
        return items
            .Select(i => new EquipmentLookupDto(i.EquipmentID, i.Name, i.DailyRate))
            .ToList();
    }

    // ---- helpers ----
    private static EquipmentListItemDto MapList(EquipmentCatalog e) => new()
    {
        EquipmentID = e.EquipmentID,
        Name = e.Name,
        CategoryID = e.CategoryID,
        CategoryName = e.Category?.Name ?? string.Empty,
        DailyRate = e.DailyRate,
        StockQuantity = e.StockQuantity,
        Status = e.Status.ToString(),
        DateAdded = e.DateAdded,
        ImageURL = e.ImageURL,
        SerialPrefix = e.SerialPrefix,
    };

    private static EquipmentStatus ParseStatus(string s) => s switch
    {
        "Available" => EquipmentStatus.Available,
        "Unavailable" => EquipmentStatus.Unavailable,
        "Retired" => EquipmentStatus.Retired,
        _ => EquipmentStatus.Available,
    };
}
