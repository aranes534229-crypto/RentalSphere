using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.Maintenance.DTOs;
using RentalSphere.Modules.Maintenance.Models;
using RentalSphere.Modules.Maintenance.Repositories;

namespace RentalSphere.Modules.Maintenance.Services;

public interface IMaintenanceService
{
    Task<List<MaintenanceListItemDto>> ListAsync(string? statusFilter = null);
    Task<MaintenanceDetailsDto> GetDetailsAsync(int id);
    Task<int> OpenAsync(MaintenanceOpenDto dto, string actorUserId);
    Task CloseAsync(MaintenanceCloseDto dto, string actorUserId);
}

public class MaintenanceService : IMaintenanceService
{
    private readonly IMaintenanceRepository _repo;
    private readonly IEquipmentItemService _itemService;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;

    public MaintenanceService(
        IMaintenanceRepository repo,
        IEquipmentItemService itemService,
        ICurrentUser current,
        IAuditLogger audit)
    {
        _repo = repo;
        _itemService = itemService;
        _current = current;
        _audit = audit;
    }

    public async Task<List<MaintenanceListItemDto>> ListAsync(string? statusFilter = null)
    {
        // Customers never reach this service — controller blocks.
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view maintenance records.");

        var rows = await _repo.ListAsync(statusFilter);
        return rows.Select(MapList).ToList();
    }

    public async Task<MaintenanceDetailsDto> GetDetailsAsync(int id)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view maintenance records.");

        var m = await _repo.GetAsync(id)
            ?? throw new NotFoundException($"Maintenance record {id} not found.");
        return MapDetails(m);
    }

    public async Task<int> OpenAsync(MaintenanceOpenDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can open maintenance records.");

        if (string.IsNullOrWhiteSpace(dto.Reason))
            throw new InvalidOperationException("Reason is required.");

        if (await _repo.HasOpenForItemAsync(dto.EquipmentItemID))
            throw new InvalidOperationException(
                "This unit already has an open maintenance record. Close it first.");

        // Flip the unit out of the Available pool.
        await _itemService.ChangeStatusAsync(
            dto.EquipmentItemID, nameof(AvailabilityStatus.InMaintenance), actorUserId);

        var record = new MaintenanceRecord
        {
            EquipmentItemID = dto.EquipmentItemID,
            StartedAt = DateTime.UtcNow,
            ExpectedEnd = dto.ExpectedEnd,
            Reason = dto.Reason.Trim(),
            Notes = dto.Notes,
            Cost = dto.Cost,
            Status = MaintenanceStatus.InProgress,
            OpenedByUserId = _current.UserId,
        };
        await _repo.AddAsync(record);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Maintenance.Open", "MaintenanceRecord", record.MaintenanceRecordID.ToString(),
            newValues: new
            {
                record.EquipmentItemID,
                record.Reason,
                record.Cost,
                record.StartedAt,
            });

        return record.MaintenanceRecordID;
    }

    public async Task CloseAsync(MaintenanceCloseDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can close maintenance records.");

        var m = await _repo.GetAsync(dto.MaintenanceRecordID)
            ?? throw new NotFoundException($"Maintenance record {dto.MaintenanceRecordID} not found.");

        if (m.Status != MaintenanceStatus.InProgress)
            throw new InvalidOperationException(
                $"Only In Progress records can be closed (current status: {m.Status}).");

        await _itemService.ChangeStatusAsync(
            m.EquipmentItemID, nameof(AvailabilityStatus.Available), actorUserId);

        var oldValues = new
        {
            Status = m.Status.ToString(),
            m.Notes,
        };
        m.Status = MaintenanceStatus.Completed;
        m.EndedAt = DateTime.UtcNow;
        m.ClosedByUserId = _current.UserId;
        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            m.Notes = string.IsNullOrEmpty(m.Notes)
                ? dto.Notes.Trim()
                : $"{m.Notes}\nClosed: {dto.Notes.Trim()}";
        }
        _repo.Update(m);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Maintenance.Close", "MaintenanceRecord", m.MaintenanceRecordID.ToString(),
            oldValues: oldValues,
            newValues: new
            {
                Status = m.Status.ToString(),
                m.EndedAt,
                m.Notes,
            });
    }

    // ---- helpers ----

    private static MaintenanceListItemDto MapList(MaintenanceRecord m) => new()
    {
        MaintenanceRecordID = m.MaintenanceRecordID,
        EquipmentItemID = m.EquipmentItemID,
        SerialNumber = m.EquipmentItem?.SerialNumber ?? string.Empty,
        EquipmentName = m.EquipmentItem?.Equipment?.Name ?? string.Empty,
        StartedAt = m.StartedAt,
        ExpectedEnd = m.ExpectedEnd,
        EndedAt = m.EndedAt,
        Reason = m.Reason,
        Cost = m.Cost,
        Status = m.Status.ToString(),
        OpenedByUserName = m.OpenedByUser is null ? null : $"{m.OpenedByUser.FirstName} {m.OpenedByUser.LastName}",
        ClosedByUserName = m.ClosedByUser is null ? null : $"{m.ClosedByUser.FirstName} {m.ClosedByUser.LastName}",
    };

    private static MaintenanceDetailsDto MapDetails(MaintenanceRecord m) => new()
    {
        MaintenanceRecordID = m.MaintenanceRecordID,
        EquipmentItemID = m.EquipmentItemID,
        SerialNumber = m.EquipmentItem?.SerialNumber ?? string.Empty,
        EquipmentName = m.EquipmentItem?.Equipment?.Name ?? string.Empty,
        StartedAt = m.StartedAt,
        ExpectedEnd = m.ExpectedEnd,
        EndedAt = m.EndedAt,
        Reason = m.Reason,
        Notes = m.Notes,
        Cost = m.Cost,
        Status = m.Status.ToString(),
        OpenedByUserName = m.OpenedByUser is null ? null : $"{m.OpenedByUser.FirstName} {m.OpenedByUser.LastName}",
        ClosedByUserName = m.ClosedByUser is null ? null : $"{m.ClosedByUser.FirstName} {m.ClosedByUser.LastName}",
    };
}
