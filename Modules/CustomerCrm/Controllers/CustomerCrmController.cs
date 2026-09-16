using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Identity;
using RentalSphere.Modules.CustomerCrm.DTOs;
using RentalSphere.Modules.CustomerCrm.Models;
using RentalSphere.Modules.CustomerCrm.Repositories;
using RentalSphere.Modules.CustomerCrm.Services;
using RentalSphere.Modules.CustomerCrm.ViewModels;

namespace RentalSphere.Modules.CustomerCrm.Controllers;

/// <summary>
/// CRM tab for a single customer. Admin/Staff use the Index action with a customerId;
/// Customer role is sent here by /Customers/MyProfile, scoped to themselves.
/// </summary>
[Authorize]
public class CustomerCrmController : Controller
{
    private readonly ICustomerCrmService _service;
    private readonly ICustomerCrmRepository _repo;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ICurrentUser _current;

    public CustomerCrmController(
        ICustomerCrmService service,
        ICustomerCrmRepository repo,
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ICurrentUser current)
    {
        _service = service;
        _repo = repo;
        _db = db;
        _users = users;
        _current = current;
    }

    // GET /CustomerCrm/Dashboard — Admin/Staff landing. Lists all customers with
    // note + follow-up counts. Paged + sortable to match the Maintenance/Billing
    // module landing pages.
    [HttpGet]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
    public async Task<IActionResult> Dashboard(string? search, int page = 1, int pageSize = 20, string? sort = null)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);

        // Build the base query (filter + sort) so we can page it server-side.
        var q = _db.Customers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(c =>
                c.FirstName.ToLower().Contains(s) ||
                c.LastName.ToLower().Contains(s) ||
                c.Email.ToLower().Contains(s));
        }

        q = sort?.ToLowerInvariant() switch
        {
            "name"         => q.OrderBy(c => c.LastName).ThenBy(c => c.FirstName),
            "name_desc"    => q.OrderByDescending(c => c.LastName).ThenByDescending(c => c.FirstName),
            "email"        => q.OrderBy(c => c.Email),
            "email_desc"   => q.OrderByDescending(c => c.Email),
            _              => q.OrderBy(c => c.LastName).ThenBy(c => c.FirstName),
        };

        var totalCount = await q.CountAsync();
        var skip = (page - 1) * pageSize;
        var pagedCustomers = await q.Skip(skip).Take(pageSize).ToListAsync();

        // NOTE: aggregate counts below run over the full filtered set so the KPI
        // summary (bell badge numbers) stays a whole-customer view, not just this
        // page. We re-query the filtered id set rather than the page slice.
        var allIds = await q.Select(c => c.CustomerID).ToListAsync();

        var noteCounts = await _db.CustomerNotes
            .Where(n => allIds.Contains(n.CustomerID))
            .GroupBy(n => n.CustomerID)
            .Select(g => new { CustomerID = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerID, x => x.Count);

        var today = DateTime.UtcNow.Date;
        var openFollowUps = await _db.CustomerFollowUps
            .Where(f => allIds.Contains(f.CustomerID)
                     && f.Status == FollowUpStatus.Pending
                     && f.FollowUpDate < today)
            .GroupBy(f => f.CustomerID)
            .Select(g => new { CustomerID = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerID, x => x.Count);

        var viewerId = _current.UserId ?? "";
        var pendingByCustomer = await _db.CustomerFollowUps
            .Where(f => allIds.Contains(f.CustomerID)
                     && f.Status == FollowUpStatus.Pending
                     && f.AssignedToUserId == viewerId)
            .GroupBy(f => f.CustomerID)
            .Select(g => new { CustomerID = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerID, x => x.Count);

        // Per-customer unread note count for the current user.
        var unreadNotesByCustomer = await _db.CustomerNotes
            .Where(n => allIds.Contains(n.CustomerID)
                     && !_db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                              && r.UserId == viewerId))
            .GroupBy(n => n.CustomerID)
            .Select(g => new { CustomerID = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerID, x => x.Count);

        var totalPending = pendingByCustomer.Values.Sum();
        var totalUnreadNotes = unreadNotesByCustomer.Values.Sum();

        var vm = new CustomerCrmDashboardViewModel
        {
            Search = search,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Sort = sort,
            TotalPendingForMe = totalPending,
            TotalUnreadNotesForMe = totalUnreadNotes,
            TotalUnreadCrmItems = totalPending + totalUnreadNotes,
            Rows = pagedCustomers.Select(c => new CustomerCrmDashboardRow
            {
                CustomerID = c.CustomerID,
                CustomerName = $"{c.FirstName} {c.LastName}".Trim(),
                CustomerEmail = c.Email,
                IsActive = c.IsActive,
                NoteCount = noteCounts.GetValueOrDefault(c.CustomerID, 0),
                OverdueFollowUps = openFollowUps.GetValueOrDefault(c.CustomerID, 0),
                PendingForMe = pendingByCustomer.GetValueOrDefault(c.CustomerID, 0),
                UnreadNotesForMe = unreadNotesByCustomer.GetValueOrDefault(c.CustomerID, 0),
            }).ToList(),
        };

        return View(vm);
    }

    // GET /CustomerCrm?customerId=5
    [HttpGet]
    public async Task<IActionResult> Index(int customerId)
    {
        var tab = await _service.GetTabAsync(customerId);

        var vm = new CustomerCrmViewModel
        {
            CustomerID = tab.CustomerID,
            CustomerName = tab.CustomerName,
            CustomerEmail = tab.CustomerEmail,
            Notes = tab.Notes,
            FollowUps = tab.FollowUps,
            NewNote = new CustomerNoteCreateDto { CustomerID = customerId },
            NewFollowUp = new CustomerFollowUpCreateDto
            {
                CustomerID = customerId,
                FollowUpDate = DateTime.UtcNow.Date.AddDays(1),
                // Default the assignee to the staff member who opened the tab,
                // so scheduling a follow-up is a one-click action.
                AssignedToUserId = (_current.IsAdmin || _current.IsStaff) ? _current.UserId : null,
            },
        };

        if (_current.IsAdmin || _current.IsStaff)
        {
            vm.StaffOptions = await LoadStaffOptionsAsync();
        }

        return View(vm);
    }

    // POST /CustomerCrm/AddNote
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
    public async Task<IActionResult> AddNote(CustomerNoteCreateDto dto, int? customerId, string? tab)
    {
        // Fall back to the route/form value if the bound DTO didn't pick it up.
        var resolvedCustomerId = dto.CustomerID > 0 ? dto.CustomerID : customerId ?? 0;
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(dto.Body))
        {
            TempData["Error"] = "Note body is required.";
            return RedirectToAction(nameof(Index), new { customerId = resolvedCustomerId, tab });
        }
        if (resolvedCustomerId <= 0)
        {
            TempData["Error"] = "Could not identify the customer. Open the CRM tab from a customer record.";
            return RedirectToAction(nameof(Dashboard));
        }

        dto.CustomerID = resolvedCustomerId;
        await _service.AddNoteAsync(dto, _current.UserId!);
        TempData["Success"] = "Note added.";
        return RedirectToAction(nameof(Index), new { customerId = resolvedCustomerId, tab });
    }

    // POST /CustomerCrm/AddFollowUp
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
    public async Task<IActionResult> AddFollowUp(CustomerFollowUpCreateDto dto, int? customerId, string? tab)
    {
        var resolvedCustomerId = dto.CustomerID > 0 ? dto.CustomerID : customerId ?? 0;
        if (!ModelState.IsValid
            || string.IsNullOrWhiteSpace(dto.Reason)
            || dto.FollowUpDate == default)
        {
            TempData["Error"] = "Follow-up reason and date are required.";
            return RedirectToAction(nameof(Index), new { customerId = resolvedCustomerId, tab });
        }
        if (resolvedCustomerId <= 0)
        {
            TempData["Error"] = "Could not identify the customer. Open the CRM tab from a customer record.";
            return RedirectToAction(nameof(Dashboard));
        }

        dto.CustomerID = resolvedCustomerId;
        try
        {
            await _service.AddFollowUpAsync(dto, _current.UserId!);
            TempData["Success"] = "Follow-up scheduled.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { customerId = resolvedCustomerId, tab });
    }

    // POST /CustomerCrm/CompleteFollowUp
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
    public async Task<IActionResult> CompleteFollowUp(int followUpId, int customerId, string? resolutionNotes, string? tab)
    {
        await _service.CompleteFollowUpAsync(
            new CustomerFollowUpCompleteDto
            {
                CustomerFollowUpID = followUpId,
                ResolutionNotes = resolutionNotes,
            },
            _current.UserId!);

        TempData["Success"] = "Follow-up marked done.";
        return RedirectToAction(nameof(Index), new { customerId, tab });
    }

    // POST /CustomerCrm/CancelFollowUp
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
    public async Task<IActionResult> CancelFollowUp(int followUpId, int customerId, string? tab)
    {
        await _service.CancelFollowUpAsync(followUpId, _current.UserId!);
        TempData["Success"] = "Follow-up cancelled.";
        return RedirectToAction(nameof(Index), new { customerId, tab });
    }

    // POST /CustomerCrm/ToggleNoteRead — flips read state for the current user on a single note.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
    public async Task<IActionResult> ToggleNoteRead(int noteId, int customerId, bool makeRead, string? tab)
    {
        if (string.IsNullOrEmpty(_current.UserId))
            return RedirectToAction(nameof(Index), new { customerId, tab });

        if (makeRead)
        {
            await _service.MarkNoteReadAsync(noteId, _current.UserId);
            TempData["Success"] = "Note marked as read.";
        }
        else
        {
            await _service.MarkNoteUnreadAsync(noteId, _current.UserId);
            TempData["Success"] = "Note marked as unread.";
        }
        return RedirectToAction(nameof(Index), new { customerId, tab });
    }

    // POST /CustomerCrm/MarkAllNotesRead — bulk-mark all notes on this customer as read.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
    public async Task<IActionResult> MarkAllNotesRead(int customerId, string? tab)
    {
        if (string.IsNullOrEmpty(_current.UserId))
            return RedirectToAction(nameof(Index), new { customerId, tab });

        var count = await _service.MarkNotesReadForUserAsync(customerId, _current.UserId);
        TempData["Success"] = count > 0
            ? $"{count} note(s) marked as read."
            : "All notes were already marked as read.";
        return RedirectToAction(nameof(Index), new { customerId, tab });
    }

    // Note: AckAllMyFollowUps and ToggleFollowUpAck actions were removed when
    // Ack was merged into Mark done. The follow-up workflow is now: Pending → Done/Cancelled.
    // No more "I've seen it but I'm still working on it" middle state.

    // GET /CustomerCrm/MyFollowUps  (Customer role)
    [HttpGet]
    [Authorize(Roles = RoleNames.Customer)]
    public async Task<IActionResult> MyFollowUps()
    {
        var rows = await _service.ListMyFollowUpsAsync(_current.UserId!);
        return View(rows);
    }
    // Note: the previous Follow-up Queue action and view have been removed.
    // Per-row toggle buttons on the CRM tab now handle the same flow.

    private async Task<List<SelectListItem>> LoadStaffOptionsAsync()
    {
        var staffRoleId = await _db.Roles
            .Where(r => r.Name == RoleNames.Staff || r.Name == RoleNames.Admin)
            .Select(r => r.Id)
            .ToListAsync();

        var userRoles = await _db.UserRoles
            .Where(ur => staffRoleId.Contains(ur.RoleId))
            .Select(ur => ur.UserId)
            .ToListAsync();

        return await _db.Users
            .Where(u => userRoles.Contains(u.Id))
            .OrderBy(u => u.FirstName)
            .Select(u => new SelectListItem
            {
                Value = u.Id,
                Text = $"{u.FirstName} {u.LastName}".Trim(),
            })
            .ToListAsync();
    }
}
