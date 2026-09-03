using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Data;
using RentalSphere.Identity;
using RentalSphere.Identity.Models;

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

    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        const int pageSize = 50;
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
            .Skip((page - 1) * pageSize)
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

        ViewData["Search"] = search;
        ViewData["Page"] = page;
        ViewData["TotalPages"] = (int)Math.Ceiling(total / (double)pageSize);
        ViewData["Total"] = total;
        ViewData["ActorLookup"] = actorLookup;

        return View(entries);
    }
}
