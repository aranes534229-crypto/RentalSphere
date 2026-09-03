using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Identity;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.CustomerManagement.DTOs;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.CustomerManagement.Repositories;

namespace RentalSphere.Modules.CustomerManagement.Services;

public interface ICustomerService
{
    Task<List<CustomerListItemDto>> ListAsync(string? search = null, string? loyaltyTier = null);
    Task<(List<CustomerListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? search, string? loyaltyTier, string? sort, int skip, int take);
    Task<CustomerDetailsDto> GetDetailsAsync(int id);
    Task<int> CreateAsync(CustomerCreateDto dto, string actorUserId);
    Task UpdateAsync(CustomerUpdateDto dto, string actorUserId);
    Task OverrideTierAsync(int id, LoyaltyTier tier, string reason, string actorUserId);
    Task ClearTierOverrideAsync(int id, string actorUserId);
    Task DeleteAsync(int id, string actorUserId);
    Task<List<CustomerLookupDto>> GetLookupAsync();
    Task<int?> GetMyCustomerIdAsync();
    Task<List<RentalSphere.Modules.ReservationManagement.DTOs.ReservationListItemDto>>
        GetRecentReservationsAsync(int customerId, int take = 5);
    Task EnsureForUserAsync(ApplicationUser user);
}

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _repo;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;

    public CustomerService(
        ICustomerRepository repo,
        ApplicationDbContext db,
        ICurrentUser current,
        IAuditLogger audit)
    {
        _repo = repo;
        _db = db;
        _current = current;
        _audit = audit;
    }

    public async Task<List<CustomerListItemDto>> ListAsync(string? search = null, string? loyaltyTier = null)
    {
        var all = await _repo.ListAsync(search, loyaltyTier);

        // Customer role sees only their own record (defense-in-depth: even if the URL leaks).
        if (_current.IsCustomerScoped)
        {
            all = all.Where(c => c.UserId == _current.UserId).ToList();
        }

        return all.Select(MapList).ToList();
    }

    public async Task<(List<CustomerListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? search, string? loyaltyTier, string? sort, int skip, int take)
    {
        // Customer role is scoped to a single row; paging is a no-op there.
        if (_current.IsCustomerScoped)
        {
            var own = await _repo.ListAsync(search, loyaltyTier);
            var ownFiltered = own.Where(c => c.UserId == _current.UserId).ToList();
            return (ownFiltered.Select(MapList).ToList(), ownFiltered.Count);
        }

        var total = await _repo.CountAsync(search, loyaltyTier);
        var page = await _repo.ListPagedAsync(search, loyaltyTier, sort, skip, take);
        return (page.Select(MapList).ToList(), total);
    }

    public async Task<CustomerDetailsDto> GetDetailsAsync(int id)
    {
        var c = await _repo.GetAsync(id)
            ?? throw new NotFoundException($"Customer {id} not found.");

        EnsureCustomerMayRead(c);

        var reservationCount = await _db.Reservations
            .CountAsync(r => r.CustomerID == c.CustomerID);

        return MapDetails(c, reservationCount);
    }

    public async Task<int> CreateAsync(CustomerCreateDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can create customers.");

        // LoyaltyTier is derived from TotalSpent (or Admin override).
        // New customers start at Bronze by default; no tier field on the create DTO.
        var c = new Customer
        {
            FirstName = dto.FirstName.Trim(),
            LastName = dto.LastName.Trim(),
            Email = dto.Email.Trim(),
            Phone = dto.Phone,
            Address = dto.Address,
            City = dto.City,
            PostalCode = dto.PostalCode,
            DateRegistered = DateTime.UtcNow,
            IsActive = true,
        };
        await _repo.AddAsync(c);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Customer.Create", "Customer", c.CustomerID.ToString(),
            newValues: new
            {
                c.FirstName, c.LastName, c.Email,
                LoyaltyTier = LoyaltyTierRules.EffectiveTier(c).ToString(),
            });

        return c.CustomerID;
    }

    public async Task UpdateAsync(CustomerUpdateDto dto, string actorUserId)
    {
        var c = await _repo.GetAsync(dto.CustomerID)
            ?? throw new NotFoundException($"Customer {dto.CustomerID} not found.");

        EnsureCustomerMayWrite(c);

        var oldValues = new
        {
            c.FirstName, c.LastName, c.Email, c.Phone, c.City,
            LoyaltyTier = LoyaltyTierRules.EffectiveTier(c).ToString(), c.IsActive,
        };

        c.FirstName = dto.FirstName.Trim();
        c.LastName = dto.LastName.Trim();
        c.Email = dto.Email.Trim();
        c.Phone = dto.Phone;
        c.Address = dto.Address;
        c.City = dto.City;
        c.PostalCode = dto.PostalCode;
        // LoyaltyTier is derived; editing profile does not change it.

        // Only Admin may flip IsActive / deactivate.
        if (_current.IsAdmin)
            c.IsActive = dto.IsActive;

        _repo.Update(c);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Customer.Update", "Customer", c.CustomerID.ToString(),
            oldValues: oldValues,
            newValues: new
            {
                c.FirstName, c.LastName, c.Email, c.Phone, c.City,
                LoyaltyTier = LoyaltyTierRules.EffectiveTier(c).ToString(), c.IsActive,
            });
    }

    public async Task OverrideTierAsync(int id, LoyaltyTier tier, string reason, string actorUserId)
    {
        if (!_current.IsAdmin)
            throw new ForbiddenException("Only Admin can override loyalty tier.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Override reason is required.");

        var c = await _repo.GetAsync(id)
            ?? throw new NotFoundException($"Customer {id} not found.");

        var oldTier = LoyaltyTierRules.EffectiveTier(c);
        c.ManualOverrideTier = tier;
        c.ManualOverrideReason = reason.Trim();
        c.ManualOverrideByUserId = actorUserId;
        c.ManualOverrideAt = DateTime.UtcNow;
        _repo.Update(c);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Customer.OverrideTier", "Customer", id.ToString(),
            oldValues: new { Tier = oldTier.ToString() },
            newValues: new { Tier = tier.ToString(), reason = reason.Trim() });
    }

    public async Task ClearTierOverrideAsync(int id, string actorUserId)
    {
        if (!_current.IsAdmin)
            throw new ForbiddenException("Only Admin can clear a tier override.");

        var c = await _repo.GetAsync(id)
            ?? throw new NotFoundException($"Customer {id} not found.");
        if (c.ManualOverrideTier is null) return; // no-op

        var oldTier = LoyaltyTierRules.EffectiveTier(c);
        c.ManualOverrideTier = null;
        c.ManualOverrideReason = null;
        c.ManualOverrideByUserId = null;
        c.ManualOverrideAt = null;
        _repo.Update(c);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Customer.ClearTierOverride", "Customer", id.ToString(),
            oldValues: new { Tier = oldTier.ToString() },
            newValues: new { Tier = LoyaltyTierRules.EffectiveTier(c).ToString() });
    }

    public async Task DeleteAsync(int id, string actorUserId)
    {
        if (!_current.IsAdmin)
            throw new ForbiddenException("Only Admin can delete customers.");

        var c = await _repo.GetAsync(id)
            ?? throw new NotFoundException($"Customer {id} not found.");

        var snapshot = new { c.FirstName, c.LastName, c.Email };
        await _repo.RemoveAsync(c);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Customer.Delete", "Customer", id.ToString(),
            oldValues: snapshot);
    }

    public async Task<List<CustomerLookupDto>> GetLookupAsync()
    {
        var all = await _repo.ListAsync();
        return all
            .Where(c => c.IsActive)
            .Select(c => new CustomerLookupDto
            {
                CustomerID = c.CustomerID,
                DisplayName = $"{c.FirstName} {c.LastName}",
                Email = c.Email,
            })
            .OrderBy(c => c.DisplayName)
            .ToList();
    }

    public async Task<int?> GetMyCustomerIdAsync()
    {
        if (!_current.IsCustomerScoped || string.IsNullOrEmpty(_current.UserId)) return null;
        var c = await _repo.GetByUserIdAsync(_current.UserId);
        return c?.CustomerID;
    }

    public async Task<List<RentalSphere.Modules.ReservationManagement.DTOs.ReservationListItemDto>>
        GetRecentReservationsAsync(int customerId, int take = 5)
    {
        // Defense-in-depth scope: Admin/Staff may see anyone's recent reservations;
        // a Customer role may only see their own.
        if (_current.IsCustomerScoped)
        {
            var own = await _repo.GetByUserIdAsync(_current.UserId ?? "");
            if (own is null || own.CustomerID != customerId)
                throw new ForbiddenException("You can only see your own reservations.");
        }

        var rows = await _db.Reservations
            .Include(r => r.Customer)
            .Include(r => r.Items)
            .Where(r => r.CustomerID == customerId)
            .OrderByDescending(r => r.RentalStartDate)
            .Take(take)
            .ToListAsync();

        return rows.Select(r => new RentalSphere.Modules.ReservationManagement.DTOs.ReservationListItemDto
        {
            ReservationID = r.ReservationID,
            CustomerID = r.CustomerID,
            CustomerName = r.Customer is null
                ? string.Empty
                : $"{r.Customer.FirstName} {r.Customer.LastName}".Trim(),
            ReservationDate = r.ReservationDate,
            RentalStartDate = r.RentalStartDate,
            RentalEndDate = r.RentalEndDate,
            ItemCount = r.Items?.Count ?? 0,
            TotalEstimatedCost = r.TotalEstimatedCost,
            Status = r.Status.ToString(),
        }).ToList();
    }

    public async Task EnsureForUserAsync(ApplicationUser user)
    {
        if (string.IsNullOrEmpty(user.Id)) return;
        if (await _repo.GetByUserIdAsync(user.Id) is not null) return;

        var c = new Customer
        {
            UserId = user.Id,
            FirstName = string.IsNullOrWhiteSpace(user.FirstName) ? user.Email ?? "Customer" : user.FirstName,
            LastName = string.IsNullOrWhiteSpace(user.LastName) ? string.Empty : user.LastName,
            Email = user.Email ?? string.Empty,
            DateRegistered = DateTime.UtcNow,
            IsActive = true,
        };
        try
        {
            await _repo.AddAsync(c);
            await _repo.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // A concurrent registration may have created the row; that's fine.
        }
    }

    // ---- helpers ----

    private void EnsureCustomerMayRead(Customer c)
    {
        if (_current.IsCustomerScoped && c.UserId != _current.UserId)
            throw new NotFoundException($"Customer {c.CustomerID} not found.");
    }

    private void EnsureCustomerMayWrite(Customer c)
    {
        // Staff can edit any customer; Customer can edit only their own profile; Admin is unconstrained.
        if (_current.IsCustomerScoped && c.UserId != _current.UserId)
            throw new ForbiddenException("You can only edit your own profile.");
    }

    private static CustomerListItemDto MapList(Customer c) => new()
    {
        CustomerID = c.CustomerID,
        FirstName = c.FirstName,
        LastName = c.LastName,
        Email = c.Email,
        Phone = c.Phone,
        LoyaltyTier = LoyaltyTierRules.EffectiveTier(c).ToString(),
        DateRegistered = c.DateRegistered,
        IsActive = c.IsActive,
        ReservationCount = 0, // populated in Details; Index skips the per-row query for performance.
    };

    private static CustomerDetailsDto MapDetails(Customer c, int reservationCount) => new()
    {
        CustomerID = c.CustomerID,
        UserId = c.UserId,
        FirstName = c.FirstName,
        LastName = c.LastName,
        Email = c.Email,
        Phone = c.Phone,
        Address = c.Address,
        City = c.City,
        PostalCode = c.PostalCode,
        LoyaltyTier = LoyaltyTierRules.EffectiveTier(c).ToString(),
        IsOverridden = c.ManualOverrideTier.HasValue,
        OverrideReason = c.ManualOverrideReason,
        OverrideByUserId = c.ManualOverrideByUserId,
        OverrideAt = c.ManualOverrideAt,
        TotalSpent = c.TotalSpent,
        DateRegistered = c.DateRegistered,
        IsActive = c.IsActive,
        ReservationCount = reservationCount,
    };
}