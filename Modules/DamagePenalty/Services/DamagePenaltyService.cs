using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Billing.Services;
using RentalSphere.Modules.DamagePenalty.DTOs;
using RentalSphere.Modules.DamagePenalty.Models;
using RentalSphere.Modules.DamagePenalty.Repositories;
using DPEntity = RentalSphere.Modules.DamagePenalty.Models.DamagePenalty;

namespace RentalSphere.Modules.DamagePenalty.Services;

public interface IDamagePenaltyService
{
    Task<List<DamagePenaltyListItemDto>> ListAsync(string? statusFilter = null, int? rentalTransactionId = null);
    Task<(List<DamagePenaltyListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? statusFilter, int? rentalTransactionId, string? sort, int skip, int take);
    Task<DamagePenaltyDetailsDto> GetDetailsAsync(int id);
    Task<int> CreateAsync(DamagePenaltyCreateDto dto, string actorUserId);
    Task WaiveAsync(DamagePenaltyWaiveDto dto, string actorUserId);
    Task UpdateAmountAsync(int id, decimal amount, string actorUserId);
}

public class DamagePenaltyService : IDamagePenaltyService
{
    private readonly IDamagePenaltyRepository _repo;
    private readonly IBillingService _billing;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;

    public DamagePenaltyService(
        IDamagePenaltyRepository repo,
        IBillingService billing,
        ICurrentUser current,
        IAuditLogger audit)
    {
        _repo = repo;
        _billing = billing;
        _current = current;
        _audit = audit;
    }

    public async Task<List<DamagePenaltyListItemDto>> ListAsync(string? statusFilter = null, int? rentalTransactionId = null)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view damage records.");

        var rows = await _repo.ListAsync(statusFilter, rentalTransactionId);
        return rows.Select(MapList).ToList();
    }

    public async Task<(List<DamagePenaltyListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? statusFilter, int? rentalTransactionId, string? sort, int skip, int take)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view damage records.");

        var all = await _repo.ListAsync(statusFilter, rentalTransactionId);
        var total = all.Count;

        var sorted = sort?.ToLowerInvariant() switch
        {
            "id" or "id_desc" => sort == "id_desc"
                ? all.OrderByDescending(p => p.DamagePenaltyID).ToList()
                : all.OrderBy(p => p.DamagePenaltyID).ToList(),
            "tx" => all.OrderBy(p => p.RentalTransactionID).ToList(),
            "tx_desc" => all.OrderByDescending(p => p.RentalTransactionID).ToList(),
            "amount" => all.OrderBy(p => p.Amount).ToList(),
            "amount_desc" => all.OrderByDescending(p => p.Amount).ToList(),
            "date" => all.OrderBy(p => p.ReportedAt).ToList(),
            "date_desc" => all.OrderByDescending(p => p.ReportedAt).ToList(),
            _ => all.OrderByDescending(p => p.ReportedAt).ToList(),
        };

        var page = sorted.Skip(skip).Take(take).Select(MapList).ToList();
        return (page, total);
    }

    public async Task<DamagePenaltyDetailsDto> GetDetailsAsync(int id)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view damage records.");

        var p = await _repo.GetAsync(id)
            ?? throw new NotFoundException($"Damage record {id} not found.");
        return MapDetails(p);
    }

    public async Task<int> CreateAsync(DamagePenaltyCreateDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can record damage.");
        if (dto.Amount < 0)
            throw new InvalidOperationException("Amount cannot be negative.");
        if (string.IsNullOrWhiteSpace(dto.Description))
            throw new InvalidOperationException("Description is required.");

        var entity = new DPEntity
        {
            RentalTransactionID = dto.RentalTransactionID,
            PenaltyType = dto.PenaltyType,
            EquipmentID = dto.EquipmentID,
            EquipmentItemID = dto.EquipmentItemID,
            Description = dto.Description.Trim(),
            Amount = dto.Amount,
            Status = DamagePenaltyStatus.Pending,
            ReportedByUserId = _current.UserId,
            Notes = dto.Notes,
        };
        await _repo.AddAsync(entity);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "DamagePenalty.Create", "DamagePenalty", entity.DamagePenaltyID.ToString(),
            newValues: new
            {
                entity.RentalTransactionID,
                entity.PenaltyType,
                entity.Amount,
                entity.Description,
            });

        return entity.DamagePenaltyID;
    }

    public async Task WaiveAsync(DamagePenaltyWaiveDto dto, string actorUserId)
    {
        if (!_current.IsAdmin)
            throw new ForbiddenException("Only Admin can waive damage records.");

        var p = await _repo.GetAsync(dto.DamagePenaltyID)
            ?? throw new NotFoundException($"Damage record {dto.DamagePenaltyID} not found.");

        if (dto.Amount < 0)
            throw new InvalidOperationException("Waive amount cannot be negative.");

        var newWaived = p.WaivedAmount + dto.Amount;
        if (newWaived > p.Amount)
            throw new InvalidOperationException(
                $"Cannot waive more than the record amount (₱{p.Amount:N2}, already waived ₱{p.WaivedAmount:N2}).");

        var oldWaived = p.WaivedAmount;
        p.WaivedAmount = newWaived;
        if (p.WaivedAmount >= p.Amount)
        {
            p.Status = DamagePenaltyStatus.Waived;
            p.ResolvedAt = DateTime.UtcNow;
            p.ResolvedByUserId = _current.UserId;
        }
        _repo.Update(p);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "DamagePenalty.Waive", "DamagePenalty", p.DamagePenaltyID.ToString(),
            oldValues: new { WaivedAmount = oldWaived, p.Status },
            newValues: new { WaivedAmount = p.WaivedAmount, Status = p.Status.ToString() });
    }

    public async Task UpdateAmountAsync(int id, decimal amount, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can update damage records.");

        var p = await _repo.GetAsync(id)
            ?? throw new NotFoundException($"Damage record {id} not found.");
        if (amount < 0)
            throw new InvalidOperationException("Amount cannot be negative.");

        var oldAmount = p.Amount;
        p.Amount = amount;
        _repo.Update(p);
        await _repo.SaveChangesAsync();

        // Push the new amount into any linked invoice line and recompute totals.
        // Ignore not-found: penalty might not have a linked invoice yet (admin edits first).
        try
        {
            await _billing.SyncDamageLineAsync(id, actorUserId);
        }
        catch (NotFoundException) { }

        await _audit.LogAsync(
            actorUserId, "DamagePenalty.UpdateAmount", "DamagePenalty", id.ToString(),
            oldValues: new { Amount = oldAmount },
            newValues: new { Amount = amount });
    }

    private static DamagePenaltyListItemDto MapList(DPEntity p) => new()
    {
        DamagePenaltyID = p.DamagePenaltyID,
        RentalTransactionID = p.RentalTransactionID,
        EquipmentName = p.Equipment?.Name,
        SerialNumber = p.EquipmentItem?.SerialNumber,
        PenaltyType = p.PenaltyType,
        Description = p.Description,
        Amount = p.Amount,
        WaivedAmount = p.WaivedAmount,
        Status = p.Status,
        ReportedAt = p.ReportedAt,
        ReportedByUserName = p.ReportedByUser is null ? null : $"{p.ReportedByUser.FirstName} {p.ReportedByUser.LastName}",
    };

    private static DamagePenaltyDetailsDto MapDetails(DPEntity p) => new()
    {
        DamagePenaltyID = p.DamagePenaltyID,
        RentalTransactionID = p.RentalTransactionID,
        EquipmentName = p.Equipment?.Name,
        SerialNumber = p.EquipmentItem?.SerialNumber,
        PenaltyType = p.PenaltyType,
        Description = p.Description,
        Amount = p.Amount,
        WaivedAmount = p.WaivedAmount,
        Status = p.Status,
        ReportedAt = p.ReportedAt,
        ResolvedAt = p.ResolvedAt,
        ReportedByUserName = p.ReportedByUser is null ? null : $"{p.ReportedByUser.FirstName} {p.ReportedByUser.LastName}",
        ResolvedByUserName = p.ResolvedByUser is null ? null : $"{p.ResolvedByUser.FirstName} {p.ResolvedByUser.LastName}",
        Notes = p.Notes,
    };
}
