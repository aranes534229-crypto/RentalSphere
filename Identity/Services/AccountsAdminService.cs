using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Models;
using RentalSphere.Identity.Models.ViewModels;

namespace RentalSphere.Identity.Services;

public interface IAccountsAdminService
{
    Task<List<UserListItemViewModel>> ListAsync(string? search = null);
    Task<UserDetailsViewModel> GetAsync(string id);
    Task CreateAsync(UserCreateViewModel model, string actorUserId);
    Task UpdateAsync(UserEditViewModel model, string actorUserId);
    Task DeleteAsync(string id, string actorUserId);
    Task ToggleActiveAsync(string id, string actorUserId);
    Task AssignRoleAsync(string userId, string role, string actorUserId);
    Task RemoveRoleAsync(string userId, string role, string actorUserId);
    Task<List<string>> GetUserRolesAsync(string userId);
}

public class AccountsAdminService : IAccountsAdminService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly IAuditLogger _audit;
    private readonly ICurrentUser _current;

    public AccountsAdminService(
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole> roles,
        IAuditLogger audit,
        ICurrentUser current)
    {
        _users = users;
        _roles = roles;
        _audit = audit;
        _current = current;
    }

    public async Task<List<UserListItemViewModel>> ListAsync(string? search = null)
    {
        var query = _users.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(s)) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(s)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(s)));
        }

        var users = await query.OrderBy(u => u.Email).ToListAsync();

        var result = new List<UserListItemViewModel>();
        foreach (var u in users)
        {
            var roles = await _users.GetRolesAsync(u);
            result.Add(new UserListItemViewModel
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                FirstName = u.FirstName,
                LastName = u.LastName,
                IsActive = u.IsActive,
                DateRegistered = u.DateRegistered,
                Roles = roles.ToList(),
            });
        }
        return result;
    }

    public async Task<UserDetailsViewModel> GetAsync(string id)
    {
        var user = await _users.FindByIdAsync(id)
            ?? throw new NotFoundException($"User '{id}' not found.");

        var roles = await _users.GetRolesAsync(user);
        return new UserDetailsViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
            IsActive = user.IsActive,
            DateRegistered = user.DateRegistered,
            Roles = roles.ToList(),
        };
    }

    public async Task CreateAsync(UserCreateViewModel model, string actorUserId)
    {
        if (await _users.FindByEmailAsync(model.Email) is not null)
            throw new InvalidOperationException($"A user with email '{model.Email}' already exists.");

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FirstName = model.FirstName,
            LastName = model.LastName,
            PhoneNumber = model.PhoneNumber,
            IsActive = true,
            EmailConfirmed = true,
        };

        var result = await _users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        if (!string.IsNullOrEmpty(model.Role))
        {
            if (!await _roles.RoleExistsAsync(model.Role))
                throw new InvalidOperationException($"Role '{model.Role}' does not exist.");
            var addRole = await _users.AddToRoleAsync(user, model.Role);
            if (!addRole.Succeeded)
                throw new InvalidOperationException(string.Join("; ", addRole.Errors.Select(e => e.Description)));
        }

        await _audit.LogAsync(
            actorUserId, "User.Create", "ApplicationUser", user.Id,
            newValues: new { user.Email, user.FirstName, user.LastName, Role = model.Role });
    }

    public async Task UpdateAsync(UserEditViewModel model, string actorUserId)
    {
        var user = await _users.FindByIdAsync(model.Id)
            ?? throw new NotFoundException($"User '{model.Id}' not found.");

        var oldValues = new { user.Email, user.FirstName, user.LastName, user.PhoneNumber, user.IsActive };

        user.FirstName = model.FirstName;
        user.LastName = model.LastName;
        user.PhoneNumber = model.PhoneNumber;
        user.IsActive = model.IsActive;

        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        await _audit.LogAsync(
            actorUserId, "User.Update", "ApplicationUser", user.Id,
            oldValues: oldValues,
            newValues: new { user.Email, user.FirstName, user.LastName, user.PhoneNumber, user.IsActive });
    }

    public async Task DeleteAsync(string id, string actorUserId)
    {
        var user = await _users.FindByIdAsync(id)
            ?? throw new NotFoundException($"User '{id}' not found.");

        // Safety: don't allow deleting the only remaining admin.
        if (await _users.IsInRoleAsync(user, RoleNames.Admin))
        {
            var adminCount = (await _users.GetUsersInRoleAsync(RoleNames.Admin)).Count;
            if (adminCount <= 1)
                throw new InvalidOperationException("Cannot delete the last Admin account.");
        }

        var snapshot = new { user.Email, user.FirstName, user.LastName };
        var result = await _users.DeleteAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        await _audit.LogAsync(
            actorUserId, "User.Delete", "ApplicationUser", id,
            oldValues: snapshot);
    }

    public async Task ToggleActiveAsync(string id, string actorUserId)
    {
        var user = await _users.FindByIdAsync(id)
            ?? throw new NotFoundException($"User '{id}' not found.");

        // Same safety for admin deactivation.
        if (user.IsActive && await _users.IsInRoleAsync(user, RoleNames.Admin))
        {
            var adminCount = (await _users.GetUsersInRoleAsync(RoleNames.Admin))
                .Count(u => u.IsActive);
            if (adminCount <= 1)
                throw new InvalidOperationException("Cannot deactivate the last active Admin.");
        }

        var oldActive = user.IsActive;
        user.IsActive = !user.IsActive;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        await _audit.LogAsync(
            actorUserId, user.IsActive ? "User.Activate" : "User.Deactivate",
            "ApplicationUser", user.Id,
            oldValues: new { IsActive = oldActive },
            newValues: new { IsActive = user.IsActive });
    }

    public async Task AssignRoleAsync(string userId, string role, string actorUserId)
    {
        var user = await _users.FindByIdAsync(userId)
            ?? throw new NotFoundException($"User '{userId}' not found.");

        if (!await _roles.RoleExistsAsync(role))
            throw new NotFoundException($"Role '{role}' not found.");

        if (await _users.IsInRoleAsync(user, role)) return;

        var oldRoles = (await _users.GetRolesAsync(user)).ToList();
        var addResult = await _users.AddToRoleAsync(user, role);
        if (!addResult.Succeeded)
            throw new InvalidOperationException(string.Join("; ", addResult.Errors.Select(e => e.Description)));

        await _audit.LogAsync(
            actorUserId, "Role.Assign", "ApplicationUser", user.Id,
            oldValues: new { Roles = oldRoles },
            newValues: new { Role = role, AllRoles = (await _users.GetRolesAsync(user)).ToList() });
    }

    public async Task RemoveRoleAsync(string userId, string role, string actorUserId)
    {
        var user = await _users.FindByIdAsync(userId)
            ?? throw new NotFoundException($"User '{userId}' not found.");

        if (!await _users.IsInRoleAsync(user, role)) return;

        // Same safety: keep at least one admin.
        if (role == RoleNames.Admin)
        {
            var adminCount = (await _users.GetUsersInRoleAsync(RoleNames.Admin)).Count;
            if (adminCount <= 1)
                throw new InvalidOperationException("Cannot remove Admin role from the last admin.");
        }

        var oldRoles = (await _users.GetRolesAsync(user)).ToList();
        var result = await _users.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        await _audit.LogAsync(
            actorUserId, "Role.Remove", "ApplicationUser", user.Id,
            oldValues: new { Roles = oldRoles },
            newValues: new { AllRoles = (await _users.GetRolesAsync(user)).ToList() });
    }

    public async Task<List<string>> GetUserRolesAsync(string userId)
    {
        var user = await _users.FindByIdAsync(userId)
            ?? throw new NotFoundException($"User '{userId}' not found.");
        return (await _users.GetRolesAsync(user)).ToList();
    }
}
