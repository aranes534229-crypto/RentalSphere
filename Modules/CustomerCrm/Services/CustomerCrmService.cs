using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.CustomerCrm.DTOs;
using RentalSphere.Modules.CustomerCrm.Models;
using RentalSphere.Modules.CustomerCrm.Repositories;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.CustomerManagement.Repositories;

namespace RentalSphere.Modules.CustomerCrm.Services;

public interface ICustomerCrmService
{
    Task<CustomerCrmTabDto> GetTabAsync(int customerId);
    Task<int> AddNoteAsync(CustomerNoteCreateDto dto, string actorUserId);
    Task CompleteFollowUpAsync(CustomerFollowUpCompleteDto dto, string actorUserId);
    Task<int> AddFollowUpAsync(CustomerFollowUpCreateDto dto, string actorUserId);
    Task CancelFollowUpAsync(int followUpId, string actorUserId);
    Task<List<CustomerFollowUpDto>> ListMyFollowUpsAsync(string actorUserId);
    Task<int> CountPendingForUserAsync(string userId);
    Task<int> CountUnreadNotesForUserAsync(string userId);
    Task<int> CountUnreadCrmItemsForUserAsync(string userId);
    Task<int> MarkNotesReadForUserAsync(int customerId, string userId);
    Task MarkNoteReadAsync(int noteId, string userId);
    Task MarkNoteUnreadAsync(int noteId, string userId);
}

/// <summary>
/// Combined CRM payload — notes + follow-ups for a single customer. Service applies
/// Customer scoping: a Customer role only ever sees their own CRM tab.
/// </summary>
public class CustomerCrmTabDto
{
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public List<CustomerNoteDto> Notes { get; set; } = new();
    public List<CustomerFollowUpDto> FollowUps { get; set; } = new();
}

public class CustomerCrmService : ICustomerCrmService
{
    private readonly ICustomerCrmRepository _repo;
    private readonly ICustomerRepository _customerRepo;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;

    public CustomerCrmService(
        ICustomerCrmRepository repo,
        ICustomerRepository customerRepo,
        ApplicationDbContext db,
        ICurrentUser current,
        IAuditLogger audit)
    {
        _repo = repo;
        _customerRepo = customerRepo;
        _db = db;
        _current = current;
        _audit = audit;
    }

    public async Task<CustomerCrmTabDto> GetTabAsync(int customerId)
    {
        await EnsureCanViewAsync(customerId);

        var customer = await _customerRepo.GetAsync(customerId)
            ?? throw new NotFoundException($"Customer {customerId} not found.");

        var viewerId = _current.UserId;
        var isStaffOrAdmin = _current.IsAdmin || _current.IsStaff;

        // Read/ack state is now MANUAL. Opening the tab does not change the
        // per-row state — the user clicks the toggle on each row, or the
        // "Mark all read" / "Ack all my follow-ups" bulk buttons. This was
        // the right call: auto-mark on view kept overriding the user's
        // explicit "Mark unread" click, so the count was unreliable.

        var notes = await _repo.ListNotesAsync(customerId);
        var followUps = await _repo.ListFollowUpsAsync(customerId);

        // Per-row unread flags.
        HashSet<int> readNoteIds = isStaffOrAdmin && !string.IsNullOrEmpty(viewerId)
            ? (await _db.NoteReads
                .Where(r => r.UserId == viewerId
                         && notes.Select(n => n.CustomerNoteID).Contains(r.CustomerNoteID))
                .Select(r => r.CustomerNoteID)
                .ToListAsync()).ToHashSet()
            : new HashSet<int>();

        var noteDtos = notes.Select(n =>
        {
            var dto = CustomerCrmMappings.Map(n);
            dto.IsUnreadForCurrentUser = isStaffOrAdmin && !readNoteIds.Contains(n.CustomerNoteID);
            return dto;
        }).ToList();

        var followUpDtos = followUps.Select(f =>
        {
            var dto = CustomerCrmMappings.Map(f);
            return dto;
        }).ToList();

        return new CustomerCrmTabDto
        {
            CustomerID = customer.CustomerID,
            CustomerName = $"{customer.FirstName} {customer.LastName}".Trim(),
            CustomerEmail = customer.Email,
            Notes = noteDtos,
            FollowUps = followUpDtos,
        };
    }

    public async Task<int> AddNoteAsync(CustomerNoteCreateDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can add customer notes.");

        var customer = await _customerRepo.GetAsync(dto.CustomerID)
            ?? throw new NotFoundException($"Customer {dto.CustomerID} not found.");

        var entity = new CustomerNote
        {
            CustomerID = dto.CustomerID,
            Body = dto.Body.Trim(),
            IsFlagged = dto.IsFlagged,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _current.UserId,
        };

        await _repo.AddNoteAsync(entity);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "CustomerNote.Create", "CustomerNote", entity.CustomerNoteID.ToString(),
            newValues: new
            {
                entity.CustomerID,
                entity.IsFlagged,
                Length = entity.Body.Length,
            });

        return entity.CustomerNoteID;
    }

    public async Task<int> AddFollowUpAsync(CustomerFollowUpCreateDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can schedule follow-ups.");

        var customer = await _customerRepo.GetAsync(dto.CustomerID)
            ?? throw new NotFoundException($"Customer {dto.CustomerID} not found.");

        if (dto.FollowUpDate.Date < DateTime.UtcNow.Date)
            throw new InvalidOperationException("Follow-up date cannot be in the past.");

        var entity = new CustomerFollowUp
        {
            CustomerID = dto.CustomerID,
            Reason = dto.Reason.Trim(),
            FollowUpDate = dto.FollowUpDate.Date,
            Status = FollowUpStatus.Pending,
            AssignedToUserId = string.IsNullOrWhiteSpace(dto.AssignedToUserId) ? null : dto.AssignedToUserId,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _current.UserId,
        };

        await _repo.AddFollowUpAsync(entity);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "CustomerFollowUp.Create", "CustomerFollowUp", entity.CustomerFollowUpID.ToString(),
            newValues: new
            {
                entity.CustomerID,
                entity.FollowUpDate,
                entity.AssignedToUserId,
            });

        return entity.CustomerFollowUpID;
    }

    public async Task CompleteFollowUpAsync(CustomerFollowUpCompleteDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can complete follow-ups.");

        var fu = await _repo.GetFollowUpAsync(dto.CustomerFollowUpID)
            ?? throw new NotFoundException($"Follow-up {dto.CustomerFollowUpID} not found.");

        if (fu.Status != FollowUpStatus.Pending)
            throw new InvalidOperationException(
                $"Follow-up is already {fu.Status}.");

        fu.Status = FollowUpStatus.Done;
        fu.CompletedAt = DateTime.UtcNow;
        fu.ResolutionNotes = dto.ResolutionNotes?.Trim();
        _repo.UpdateFollowUp(fu);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "CustomerFollowUp.Complete", "CustomerFollowUp", fu.CustomerFollowUpID.ToString(),
            oldValues: new { Status = FollowUpStatus.Pending.ToString() },
            newValues: new { Status = fu.Status.ToString(), fu.CompletedAt });
    }

    public async Task CancelFollowUpAsync(int followUpId, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can cancel follow-ups.");

        var fu = await _repo.GetFollowUpAsync(followUpId)
            ?? throw new NotFoundException($"Follow-up {followUpId} not found.");

        if (fu.Status != FollowUpStatus.Pending)
            throw new InvalidOperationException(
                $"Follow-up is already {fu.Status}.");

        fu.Status = FollowUpStatus.Cancelled;
        fu.CompletedAt = DateTime.UtcNow;
        _repo.UpdateFollowUp(fu);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "CustomerFollowUp.Cancel", "CustomerFollowUp", fu.CustomerFollowUpID.ToString(),
            oldValues: new { Status = FollowUpStatus.Pending.ToString() },
            newValues: new { Status = fu.Status.ToString() });
    }

    public async Task<List<CustomerFollowUpDto>> ListMyFollowUpsAsync(string actorUserId)
    {
        // Customer-scoped: pull the customer's CustomerID, then their follow-ups.
        var customer = await _customerRepo.GetByUserIdAsync(actorUserId)
            ?? throw new NotFoundException("No customer profile linked to this user.");

        var followUps = await _repo.ListFollowUpsAsync(customer.CustomerID);
        return followUps.Select(CustomerCrmMappings.Map).ToList();
    }

    public async Task<int> CountPendingForUserAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return 0;
        return await _db.CustomerFollowUps
            .CountAsync(f =>
                f.Status == FollowUpStatus.Pending
                && f.AssignedToUserId == userId);
    }

    public async Task<int> MarkNotesReadForUserAsync(int customerId, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return 0;

        // Find notes on this customer that the user has not yet read.
        var unreadNoteIds = await _db.CustomerNotes
            .Where(n => n.CustomerID == customerId
                     && !_db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                              && r.UserId == userId))
            .Select(n => n.CustomerNoteID)
            .ToListAsync();

        if (unreadNoteIds.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var reads = unreadNoteIds.Select(id => new NoteRead
        {
            CustomerNoteID = id,
            UserId = userId,
            ReadAt = now,
        });
        _db.NoteReads.AddRange(reads);
        await _db.SaveChangesAsync();
        return unreadNoteIds.Count;
    }

    public async Task<int> CountUnreadNotesForUserAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return 0;
        return await _db.CustomerNotes
            .CountAsync(n => !_db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                                  && r.UserId == userId));
    }

    public async Task<int> CountUnreadCrmItemsForUserAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return 0;
        var unreadNotes = await _db.CustomerNotes
            .CountAsync(n => !_db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                                  && r.UserId == userId));
        var pendingFus = await CountPendingForUserAsync(userId);
        return unreadNotes + pendingFus;
    }

    public async Task MarkNoteReadAsync(int noteId, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;

        // Only staff/admin can mark. Customer role has no read state on notes.
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can change note read state.");

        // Idempotent: if a NoteRead row already exists, do nothing.
        var exists = await _db.NoteReads.AnyAsync(r => r.CustomerNoteID == noteId && r.UserId == userId);
        if (exists) return;

        _db.NoteReads.Add(new NoteRead
        {
            CustomerNoteID = noteId,
            UserId = userId,
            ReadAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    public async Task MarkNoteUnreadAsync(int noteId, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;

        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can change note read state.");

        var row = await _db.NoteReads
            .FirstOrDefaultAsync(r => r.CustomerNoteID == noteId && r.UserId == userId);
        if (row is null) return;

        _db.NoteReads.Remove(row);
        await _db.SaveChangesAsync();
    }

    private async Task EnsureCanViewAsync(int customerId)
    {
        if (_current.IsAdmin || _current.IsStaff) return;

        if (_current.IsCustomer)
        {
            var own = await _customerRepo.GetByUserIdAsync(_current.UserId!);
            if (own is null || own.CustomerID != customerId)
                throw new ForbiddenException("Customers can only access their own CRM record.");
            return;
        }

        throw new ForbiddenException("Sign in to access CRM.");
    }
}
