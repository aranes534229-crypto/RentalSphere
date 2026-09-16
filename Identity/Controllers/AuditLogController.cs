using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Data;
using RentalSphere.Identity;
using RentalSphere.Identity.Models;
using RentalSphere.Identity.Models.ViewModels;

namespace RentalSphere.Identity.Controllers;

/// <summary>
/// Read-only audit log viewer. Admin-only.
/// </summary>
[Authorize(Roles = RoleNames.Admin)]
public class AuditLogController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public AuditLogController(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);
        var skip = (page - 1) * pageSize;

        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(a =>
                a.Action.Contains(s) ||
                a.EntityName.Contains(s) ||
                a.EntityId.Contains(s) ||
                a.ActorUserId.Contains(s));
        }

        var total = await query.CountAsync();
        var entries = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();

        // Resolve actor email for display.
        var userIds = entries.Select(e => e.ActorUserId).Distinct().ToList();
        var actorLookup = new Dictionary<string, string>();
        foreach (var uid in userIds)
        {
            var u = await _users.FindByIdAsync(uid);
            actorLookup[uid] = u?.Email ?? uid;
        }

        var vm = new AuditLogIndexViewModel
        {
            Items = entries,
            Search = search,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            ActorLookup = actorLookup,
        };
        return View(vm);
    }
}
