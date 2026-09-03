// Verification script: exercises the new computed-tier + Admin-override flow.
// Confirms:
//   - new customers start at Bronze (no tier field on create)
//   - Update does not change tier (and doesn't expose a field to)
//   - LoyaltyTierRules computes correct tier from spend
//   - Staff cannot call OverrideTierAsync
//   - Admin with empty reason is rejected
//   - Admin with reason pins the override; ClearTierOverrideAsync reverts
//   - Customer role editing own profile cannot change tier
//   - Follow-up ack lifecycle: assignee acks, others don't
// Run from repo root:
//   dotnet run --project Tools/VerifyBatch_Customers.csproj

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Identity;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.CustomerCrm.DTOs;
using RentalSphere.Modules.CustomerCrm.Models;
using RentalSphere.Modules.CustomerCrm.Repositories;
using RentalSphere.Modules.CustomerCrm.Services;
using RentalSphere.Modules.CustomerManagement.DTOs;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.CustomerManagement.Repositories;
using RentalSphere.Modules.CustomerManagement.Services;
using System.Security.Claims;

namespace RentalSphere.Tools.VerifyBatch_Customers;

internal sealed class StubCurrentUser : ICurrentUser
{
    private readonly ClaimsPrincipal? _principal;
    public StubCurrentUser(ClaimsPrincipal? principal) { _principal = principal; }

    public string? UserId => _principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? UserName => _principal?.Identity?.Name;
    public bool IsAuthenticated => _principal?.Identity?.IsAuthenticated ?? false;
    public bool IsInRole(string role) => _principal?.IsInRole(role) ?? false;
    public bool IsAdmin => IsInRole(RoleNames.Admin);
    public bool IsStaff => IsInRole(RoleNames.Staff);
    public bool IsCustomer => IsInRole(RoleNames.Customer);
    public bool IsCustomerScoped => IsAuthenticated && IsCustomer;
}

public static class Program
{
    public static async Task<int> Main()
    {
        var connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=RentalSphereDb;Trusted_Connection=True;TrustServerCertificate=True;";

        var dbOpts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var db = new ApplicationDbContext(dbOpts);

        // Clean verification data so the script is idempotent.
        var stale = await db.Customers.Where(c => c.Email.StartsWith("verify-batch-cust-")).ToListAsync();
        if (stale.Any())
        {
            db.Customers.RemoveRange(stale);
            await db.SaveChangesAsync();
        }

        var customerRepo = new CustomerRepository(db);
        var audit = new AuditLogger(db, new Microsoft.AspNetCore.Http.HttpContextAccessor());

        // Ensure the admin identity user exists for FK references.
        await EnsureIdentityUserAsync(db, "verify-batch-cust", "Verify", "Admin");

        var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "verify-batch-cust"),
                new Claim(ClaimTypes.Name, "verify-batch-cust"),
                new Claim(ClaimTypes.Role, RoleNames.Admin),
            },
            authenticationType: "Test"));
        var staffPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "verify-batch-cust-staff"),
                new Claim(ClaimTypes.Name, "verify-batch-cust-staff"),
                new Claim(ClaimTypes.Role, RoleNames.Staff),
            },
            authenticationType: "Test"));

        var adminSvc = new CustomerService(customerRepo, db, new StubCurrentUser(adminPrincipal), audit);
        var staffSvc = new CustomerService(customerRepo, db, new StubCurrentUser(staffPrincipal), audit);

        int failed = 0;
        async Task Check(string label, Func<Task<bool>> assertion)
        {
            var ok = await assertion();
            Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} - {label}");
            if (!ok) failed++;
        }

        // ---------- Test 1: New customer defaults to Bronze with TotalSpent=0 ----------
        Console.WriteLine("\nTest 1: New customer starts at Bronze");
        var newId = await adminSvc.CreateAsync(new CustomerCreateDto
        {
            FirstName = "Margaret",
            LastName = "Hamilton",
            Email = "verify-batch-cust-margaret@example.local",
            City = "Taguig",
        }, actorUserId: "verify-batch-cust");
        await Check("Created customer ID assigned", async () =>
        {
            var c = await db.Customers.FirstOrDefaultAsync(c => c.CustomerID == newId);
            return c is not null;
        });
        await Check("Default tier = Bronze (no override, zero spend)", async () =>
        {
            var c = await db.Customers.FirstAsync(c => c.CustomerID == newId);
            return c.ManualOverrideTier is null
                && c.TotalSpent == 0m
                && LoyaltyTierRules.EffectiveTier(c) == LoyaltyTier.Bronze;
        });
        await Check("Details DTO reports tier=Bronze, IsOverridden=false", async () =>
        {
            var d = await adminSvc.GetDetailsAsync(newId);
            return d.LoyaltyTier == "Bronze" && !d.IsOverridden;
        });

        // ---------- Test 2: Update no longer writes tier ----------
        Console.WriteLine("\nTest 2: Update cannot change tier");
        var dto = await adminSvc.GetDetailsAsync(newId);
        var updateForm = new CustomerUpdateDto
        {
            CustomerID = dto.CustomerID,
            FirstName = "Margaret",
            LastName = "Hamilton-Updated",
            Email = dto.Email,
            Phone = dto.Phone,
            Address = dto.Address,
            City = "Quezon City",
            PostalCode = dto.PostalCode,
            IsActive = dto.IsActive,
        };
        await adminSvc.UpdateAsync(updateForm, actorUserId: "verify-batch-cust");
        await Check("Update persists other fields", async () =>
        {
            var c = await db.Customers.FirstAsync(c => c.CustomerID == newId);
            return c.LastName == "Hamilton-Updated" && c.City == "Quezon City";
        });
        await Check("Update did not change tier or set override", async () =>
        {
            var c = await db.Customers.FirstAsync(c => c.CustomerID == newId);
            return c.ManualOverrideTier is null && c.TotalSpent == 0m;
        });

        // ---------- Test 3: LoyaltyTierRules.Compute ----------
        Console.WriteLine("\nTest 3: LoyaltyTierRules.Compute maps spend correctly");
        await Check("0 → Bronze", () => Task.FromResult(LoyaltyTierRules.Compute(0m) == LoyaltyTier.Bronze));
        await Check("19,999 → Bronze", () => Task.FromResult(LoyaltyTierRules.Compute(19_999m) == LoyaltyTier.Bronze));
        await Check("20,000 → Silver", () => Task.FromResult(LoyaltyTierRules.Compute(20_000m) == LoyaltyTier.Silver));
        await Check("49,999 → Silver", () => Task.FromResult(LoyaltyTierRules.Compute(49_999m) == LoyaltyTier.Silver));
        await Check("50,000 → Gold", () => Task.FromResult(LoyaltyTierRules.Compute(50_000m) == LoyaltyTier.Gold));
        await Check("99,999 → Gold", () => Task.FromResult(LoyaltyTierRules.Compute(99_999m) == LoyaltyTier.Gold));
        await Check("100,000 → Platinum", () => Task.FromResult(LoyaltyTierRules.Compute(100_000m) == LoyaltyTier.Platinum));
        await Check("1,000,000 → Platinum", () => Task.FromResult(LoyaltyTierRules.Compute(1_000_000m) == LoyaltyTier.Platinum));

        // ---------- Test 4: EffectiveTier: spend crosses thresholds ----------
        Console.WriteLine("\nTest 4: EffectiveTier reflects TotalSpent");
        var c4 = await db.Customers.FirstAsync(c => c.CustomerID == newId);
        c4.TotalSpent = 75_000m;
        await db.SaveChangesAsync();
        await Check("Spend=75k → Gold (no override)", () =>
        {
            var d = adminSvc.GetDetailsAsync(newId).Result;
            return Task.FromResult(d.LoyaltyTier == "Gold" && !d.IsOverridden);
        });

        // ---------- Test 5: Staff cannot call OverrideTierAsync ----------
        Console.WriteLine("\nTest 5: Staff override attempt is forbidden");
        var staffBlocked = false;
        try
        {
            await staffSvc.OverrideTierAsync(newId, LoyaltyTier.Platinum, "vip", actorUserId: "verify-batch-cust-staff");
        }
        catch (ForbiddenException)
        {
            staffBlocked = true;
        }
        await Check("Staff → ForbiddenException", () => Task.FromResult(staffBlocked));
        await Check("Override not persisted after staff attempt", async () =>
        {
            var c = await db.Customers.FirstAsync(c => c.CustomerID == newId);
            return c.ManualOverrideTier is null;
        });

        // ---------- Test 6: Admin with empty reason is rejected ----------
        Console.WriteLine("\nTest 6: Admin override with empty reason rejected");
        var emptyReasonBlocked = false;
        try
        {
            await adminSvc.OverrideTierAsync(newId, LoyaltyTier.Platinum, "   ", actorUserId: "verify-batch-cust");
        }
        catch (InvalidOperationException)
        {
            emptyReasonBlocked = true;
        }
        await Check("Empty reason → InvalidOperationException", () => Task.FromResult(emptyReasonBlocked));

        // ---------- Test 7: Admin with reason pins the override ----------
        Console.WriteLine("\nTest 7: Admin override sticks with reason");
        await adminSvc.OverrideTierAsync(newId, LoyaltyTier.Platinum, "VIP corporate account", actorUserId: "verify-batch-cust");
        await Check("Override persisted", async () =>
        {
            var c = await db.Customers.FirstAsync(c => c.CustomerID == newId);
            return c.ManualOverrideTier == LoyaltyTier.Platinum
                && c.ManualOverrideReason == "VIP corporate account"
                && c.ManualOverrideByUserId == "verify-batch-cust"
                && c.ManualOverrideAt.HasValue;
        });
        await Check("Details DTO reports override fields", async () =>
        {
            var d = await adminSvc.GetDetailsAsync(newId);
            return d.IsOverridden
                && d.LoyaltyTier == "Platinum"
                && d.OverrideReason == "VIP corporate account"
                && d.OverrideByUserId == "verify-batch-cust";
        });
        await Check("EffectiveTier is Platinum (override beats 75k spend)", () =>
        {
            var d = adminSvc.GetDetailsAsync(newId).Result;
            return Task.FromResult(d.LoyaltyTier == "Platinum");
        });

        // ---------- Test 8: ClearTierOverrideAsync reverts to spend-based ----------
        Console.WriteLine("\nTest 8: Clear override reverts to spend-based tier");
        await adminSvc.ClearTierOverrideAsync(newId, actorUserId: "verify-batch-cust");
        await Check("Override fields cleared", async () =>
        {
            var c = await db.Customers.FirstAsync(c => c.CustomerID == newId);
            return c.ManualOverrideTier is null
                && c.ManualOverrideReason is null
                && c.ManualOverrideByUserId is null
                && c.ManualOverrideAt is null;
        });
        await Check("EffectiveTier is Gold (spend=75k)", async () =>
        {
            var d = await adminSvc.GetDetailsAsync(newId);
            return d.LoyaltyTier == "Gold" && !d.IsOverridden;
        });

        // ---------- Test 9: Customer role cannot override their own tier ----------
        Console.WriteLine("\nTest 9: Customer role cannot override own tier");
        // Ensure the linked identity user exists before wiring the FK.
        await EnsureIdentityUserAsync(db, "verify-batch-cust-customer", "Margaret", "Customer");
        var c9 = await db.Customers.FirstAsync(c => c.CustomerID == newId);
        c9.UserId = "verify-batch-cust-customer";
        await db.SaveChangesAsync();

        var customerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "verify-batch-cust-customer"),
                new Claim(ClaimTypes.Name, "verify-batch-cust-customer"),
                new Claim(ClaimTypes.Role, RoleNames.Customer),
            },
            authenticationType: "Test"));
        var customerSvc = new CustomerService(customerRepo, db, new StubCurrentUser(customerPrincipal), audit);

        var customerBlocked = false;
        try
        {
            await customerSvc.OverrideTierAsync(newId, LoyaltyTier.Platinum, "i deserve it", actorUserId: "verify-batch-cust-customer");
        }
        catch (ForbiddenException)
        {
            customerBlocked = true;
        }
        await Check("Customer role → ForbiddenException", () => Task.FromResult(customerBlocked));

        // ---------- Test 10: Audit log entries ----------
        Console.WriteLine("\nTest 10: Audit log entries for override + clear");
        var overrideAudits = await db.AuditLogs
            .Where(a => a.Action == "Customer.OverrideTier" || a.Action == "Customer.ClearTierOverride")
            .CountAsync();
        await Check($"At least 2 override/clear audit entries (got {overrideAudits})",
            () => Task.FromResult(overrideAudits >= 2));

        // ---------- Test 11: Edit by customer on own profile cannot change tier ----------
        Console.WriteLine("\nTest 11: Customer editing own profile cannot change tier");
        // Set override again so we can prove the edit doesn't clear it.
        await adminSvc.OverrideTierAsync(newId, LoyaltyTier.Platinum, "VIP", actorUserId: "verify-batch-cust");
        var d11 = await customerSvc.GetDetailsAsync(newId);
        await customerSvc.UpdateAsync(new CustomerUpdateDto
        {
            CustomerID = d11.CustomerID,
            FirstName = "Margaret",
            LastName = "Hamilton-CustomerEdit",
            Email = d11.Email,
            Phone = d11.Phone,
            Address = d11.Address,
            City = "Pasig",
            PostalCode = d11.PostalCode,
            IsActive = d11.IsActive,
        }, actorUserId: "verify-batch-cust-customer");
        await Check("Other fields updated", async () =>
        {
            var c = await db.Customers.FirstAsync(c => c.CustomerID == newId);
            return c.City == "Pasig" && c.LastName == "Hamilton-CustomerEdit";
        });
        await Check("Override still in place after customer edit", async () =>
        {
            var c = await db.Customers.FirstAsync(c => c.CustomerID == newId);
            return c.ManualOverrideTier == LoyaltyTier.Platinum;
        });

        // ---------- Test 12: GetRecentReservationsAsync ----------
        Console.WriteLine("\nTest 12: Recent reservations list (MyProfile)");
        // No reservations exist for this verify customer yet — list should be empty.
        var recents = await customerSvc.GetRecentReservationsAsync(newId, take: 5);
        await Check("Empty list when no reservations", () => Task.FromResult(recents.Count == 0));

        // Customer role cannot see another customer's reservations.
        var crossCustomerBlocked = false;
        try
        {
            await customerSvc.GetRecentReservationsAsync(99999, take: 5);
        }
        catch (ForbiddenException)
        {
            crossCustomerBlocked = true;
        }
        await Check("Customer role blocked from other customer's reservations",
            () => Task.FromResult(crossCustomerBlocked));

        // Admin can call on any customer.
        var adminRecents = await adminSvc.GetRecentReservationsAsync(newId, take: 5);
        await Check("Admin can call GetRecentReservations for any customer",
            () => Task.FromResult(adminRecents is not null));

        // ---------- Test 13: Follow-up acknowledgement lifecycle ----------
        Console.WriteLine("\nTest 13: Follow-up ack lifecycle (assignee-only)");
        // Two staff members: Alice (admin) and Bob (other staff). Follow-ups are
        // assigned to Alice; Bob should not auto-ack them when he opens the CRM.
        await EnsureIdentityUserAsync(db, "verify-batch-cust-staff-bob", "Bob", "Staff");
        var staffBobPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "verify-batch-cust-staff-bob"),
                new Claim(ClaimTypes.Name, "Bob"),
                new Claim(ClaimTypes.Role, RoleNames.Staff),
            },
            authenticationType: "Test"));

        var crmRepo = new CustomerCrmRepository(db);
        var adminCrmSvc = new CustomerCrmService(crmRepo, customerRepo, db, new StubCurrentUser(adminPrincipal), audit);
        var bobCrmSvc = new CustomerCrmService(crmRepo, customerRepo, db, new StubCurrentUser(staffBobPrincipal), audit);

        // Admin creates a follow-up assigned to themselves, due today.
        var fuId = await adminCrmSvc.AddFollowUpAsync(new CustomerFollowUpCreateDto
        {
            CustomerID = newId,
            Reason = "Pending test — call customer",
            FollowUpDate = DateTime.UtcNow.Date,
            AssignedToUserId = "verify-batch-cust",
        }, actorUserId: "verify-batch-cust");

        await Check("Follow-up starts as Pending", async () =>
        {
            var f = await db.CustomerFollowUps.FindAsync(fuId);
            return f is not null && f.Status == FollowUpStatus.Pending;
        });

        await Check("CountPendingForUserAsync(admin) is 1", async () =>
        {
            var count = await adminCrmSvc.CountPendingForUserAsync("verify-batch-cust");
            return count == 1;
        });

        // Bob (other staff) opens the CRM tab — no auto-state-change (model is manual).
        var bobTab = await bobCrmSvc.GetTabAsync(newId);
        await Check("Bob's view does not change Alice's follow-up", async () =>
        {
            var f = await db.CustomerFollowUps.FindAsync(fuId);
            return f is not null && f.Status == FollowUpStatus.Pending;
        });

        // Alice opens the CRM tab — also no change.
        var aliceTab = await adminCrmSvc.GetTabAsync(newId);
        await Check("Alice's view does NOT change her own follow-up", async () =>
        {
            var f = await db.CustomerFollowUps.FindAsync(fuId);
            return f is not null && f.Status == FollowUpStatus.Pending;
        });
        await Check("Alice's count is still 1", async () =>
        {
            var count = await adminCrmSvc.CountPendingForUserAsync("verify-batch-cust");
            return count == 1;
        });

        // Mark it done.
        await adminCrmSvc.CompleteFollowUpAsync(new CustomerFollowUpCompleteDto
        {
            CustomerFollowUpID = fuId,
            ResolutionNotes = "Done",
        }, actorUserId: "verify-batch-cust");

        await Check("Follow-up is now Status=Done", async () =>
        {
            var f = await db.CustomerFollowUps.FindAsync(fuId);
            return f is not null && f.Status == FollowUpStatus.Done;
        });
        await Check("CountPendingForUserAsync drops to 0 after Mark done", async () =>
        {
            var count = await adminCrmSvc.CountPendingForUserAsync("verify-batch-cust");
            return count == 0;
        });

        // Unassigned follow-up: counts only if assigned. Unassigned doesn't show for the assignee count.
        var unassignedId = await adminCrmSvc.AddFollowUpAsync(new CustomerFollowUpCreateDto
        {
            CustomerID = newId,
            Reason = "Unassigned pending",
            FollowUpDate = DateTime.UtcNow.Date,
            AssignedToUserId = null,
        }, actorUserId: "verify-batch-cust");
        await Check("Unassigned follow-up does NOT count in admin's pending", async () =>
        {
            var count = await adminCrmSvc.CountPendingForUserAsync("verify-batch-cust");
            return count == 0;
        });

        // ---------- Test 14: NoteRead per-staff lifecycle ----------
        Console.WriteLine("\nTest 15: Note read state (per-staff, join table)");

        // Clean any stale NoteReads for the verify-batch-cust users so the
        // counts reflect only this run's notes.
        var verifyUserIds = new[] { "verify-batch-cust", "verify-batch-cust-staff-bob", "verify-batch-cust-staff-carol" };
        var staleReads = await db.NoteReads
            .Where(r => verifyUserIds.Contains(r.UserId))
            .ToListAsync();
        if (staleReads.Any())
        {
            db.NoteReads.RemoveRange(staleReads);
            await db.SaveChangesAsync();
        }
        // Also clean stale notes on this test's customer.
        var staleNotes = await db.CustomerNotes
            .Where(n => n.CustomerID == newId)
            .ToListAsync();
        if (staleNotes.Any())
        {
            db.CustomerNotes.RemoveRange(staleNotes);
            await db.SaveChangesAsync();
        }

        // Alice (admin) creates a note.
        var noteId = await adminCrmSvc.AddNoteAsync(new CustomerNoteCreateDto
        {
            CustomerID = newId,
            Body = "NoteRead test note",
            IsFlagged = false,
        }, actorUserId: "verify-batch-cust");

        // Note is unread for the admin (and only this customer was just cleaned).
        var noteIdsOnCustomer = await db.CustomerNotes
            .Where(n => n.CustomerID == newId)
            .Select(n => n.CustomerNoteID)
            .ToListAsync();
        var adminUnread = await db.CustomerNotes
            .Where(n => noteIdsOnCustomer.Contains(n.CustomerNoteID)
                     && !db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                            && r.UserId == "verify-batch-cust"))
            .CountAsync();
        await Check("Note is unread for admin on this customer", () => Task.FromResult(adminUnread == 1));

        // Opening the tab does NOT auto-mark-read anymore. Manual toggle is the source of truth.
        await adminCrmSvc.GetTabAsync(newId);
        await Check("Opening the tab does NOT create a NoteRead row (manual toggle only)", async () =>
        {
            return !await db.NoteReads.AnyAsync(r => r.CustomerNoteID == noteId
                                                  && r.UserId == "verify-batch-cust");
        });
        var adminUnreadAfterTabOpen = await db.CustomerNotes
            .Where(n => noteIdsOnCustomer.Contains(n.CustomerNoteID)
                     && !db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                            && r.UserId == "verify-batch-cust"))
            .CountAsync();
        await Check("Opening the tab does NOT change Alice's unread count",
            () => Task.FromResult(adminUnreadAfterTabOpen == 1));

        // Bob opens the tab — also no auto-mark.
        var bobUnread = await db.CustomerNotes
            .Where(n => noteIdsOnCustomer.Contains(n.CustomerNoteID)
                     && !db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                            && r.UserId == "verify-batch-cust-staff-bob"))
            .CountAsync();
        await Check("Bob's unread on this customer is 1 (he hasn't read anything)", () =>
            Task.FromResult(bobUnread == 1));
        await bobCrmSvc.GetTabAsync(newId);
        var bobUnreadAfter = await db.CustomerNotes
            .Where(n => noteIdsOnCustomer.Contains(n.CustomerNoteID)
                     && !db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                            && r.UserId == "verify-batch-cust-staff-bob"))
            .CountAsync();
        await Check("Bob opening the tab does NOT change his unread count",
            () => Task.FromResult(bobUnreadAfter == 1));
        await Check("No NoteRead rows exist yet (only manual toggles create them)", async () =>
        {
            return await db.NoteReads.CountAsync(r => r.CustomerNoteID == noteId) == 0;
        });

        // Bulk MarkAllNotesRead: Alice's view acks everything on this customer.
        var marked = await adminCrmSvc.MarkNotesReadForUserAsync(newId, "verify-batch-cust");
        await Check("MarkNotesReadForUserAsync returns 1 (one note was unread for Alice)", () =>
            Task.FromResult(marked == 1));
        await Check("After bulk mark, Alice's unread drops to 0", async () =>
        {
            var c = await db.CustomerNotes.CountAsync(n =>
                n.CustomerNoteID == noteId
                && !db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                       && r.UserId == "verify-batch-cust"));
            return c == 0;
        });

        // Per-row IsUnreadForCurrentUser reflects the new state.
        var tab = await adminCrmSvc.GetTabAsync(newId);
        var noteDto = tab.Notes.FirstOrDefault(n => n.CustomerNoteID == noteId);
        await Check("After bulk mark, the DTO flags note as not-unread-for-Alice", () =>
            Task.FromResult(noteDto is not null && !noteDto.IsUnreadForCurrentUser));

        // Bob is unaffected — separate per-user state.
        var tabForBob = await bobCrmSvc.GetTabAsync(newId);
        var noteDtoForBob = tabForBob.Notes.FirstOrDefault(n => n.CustomerNoteID == noteId);
        await Check("Bob's view still flags note as unread-for-him (Alice's read doesn't apply)", () =>
            Task.FromResult(noteDtoForBob is not null && noteDtoForBob.IsUnreadForCurrentUser));

        // Simulate a NEW staff member who hasn't read anything.
        await EnsureIdentityUserAsync(db, "verify-batch-cust-staff-carol", "Carol", "Staff");
        var carolPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "verify-batch-cust-staff-carol"),
                new Claim(ClaimTypes.Name, "Carol"),
                new Claim(ClaimTypes.Role, RoleNames.Staff),
            },
            authenticationType: "Test"));
        var carolCrmSvc = new CustomerCrmService(crmRepo, customerRepo, db, new StubCurrentUser(carolPrincipal), audit);

        // Carol opens the tab — should NOT auto-mark (the new model).
        var carolTab = await carolCrmSvc.GetTabAsync(newId);
        await Check("Carol hasn't read the note yet (no NoteRead row for her)", async () =>
        {
            return !await db.NoteReads.AnyAsync(r => r.CustomerNoteID == noteId
                                                && r.UserId == "verify-batch-cust-staff-carol");
        });
        var carolNoteDto = carolTab.Notes.FirstOrDefault(n => n.CustomerNoteID == noteId);
        await Check("Carol's view flags the note as unread-for-her", () =>
            Task.FromResult(carolNoteDto is not null && carolNoteDto.IsUnreadForCurrentUser));

        // Global unread count includes both notes and follow-ups (sum check).
        var total = await adminCrmSvc.CountUnreadCrmItemsForUserAsync("verify-batch-cust");
        var notesGlobal = await adminCrmSvc.CountUnreadNotesForUserAsync("verify-batch-cust");
        var fusGlobal = await adminCrmSvc.CountPendingForUserAsync("verify-batch-cust");
        await Check("Global unread = unread notes + pending follow-ups",
            () => Task.FromResult(total == notesGlobal + fusGlobal));

        // ---------- Test 16: Toggle read/unread on notes and follow-ups ----------
        Console.WriteLine("\nTest 16: Toggle read/unread state on notes and follow-ups");

        // Clean: ensure Alice hasn't read the note yet (it was acked earlier in Test 15).
        var aliceRead = await db.NoteReads
            .FirstOrDefaultAsync(r => r.CustomerNoteID == noteId && r.UserId == "verify-batch-cust");
        if (aliceRead is not null)
        {
            db.NoteReads.Remove(aliceRead);
            await db.SaveChangesAsync();
        }

        await Check("Note starts as unread for Alice (after cleanup)", async () =>
        {
            var c = await db.CustomerNotes.CountAsync(n =>
                n.CustomerNoteID == noteId
                && !db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                       && r.UserId == "verify-batch-cust"));
            return c == 1;
        });

        // Alice marks the note as read explicitly.
        await adminCrmSvc.MarkNoteReadAsync(noteId, "verify-batch-cust");
        await Check("MarkNoteReadAsync adds NoteRead row", async () =>
        {
            return await db.NoteReads.AnyAsync(r => r.CustomerNoteID == noteId
                                                 && r.UserId == "verify-batch-cust");
        });
        await Check("CountUnreadNotesForUserAsync(admin on this customer) drops to 0", async () =>
        {
            var c = await db.CustomerNotes.CountAsync(n =>
                n.CustomerNoteID == noteId
                && !db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                       && r.UserId == "verify-batch-cust"));
            return c == 0;
        });

        // Alice marks the note as unread.
        await adminCrmSvc.MarkNoteUnreadAsync(noteId, "verify-batch-cust");
        await Check("MarkNoteUnreadAsync removes NoteRead row", async () =>
        {
            return !await db.NoteReads.AnyAsync(r => r.CustomerNoteID == noteId
                                                  && r.UserId == "verify-batch-cust");
        });
        await Check("CountUnreadNotesForUserAsync(admin on this customer) rises back to 1", async () =>
        {
            var c = await db.CustomerNotes.CountAsync(n =>
                n.CustomerNoteID == noteId
                && !db.NoteReads.Any(r => r.CustomerNoteID == n.CustomerNoteID
                                       && r.UserId == "verify-batch-cust"));
            return c == 1;
        });

        // Idempotent: marking read when already read is a no-op.
        await adminCrmSvc.MarkNoteReadAsync(noteId, "verify-batch-cust");
        var readCount = await db.NoteReads.CountAsync(r => r.CustomerNoteID == noteId
                                                        && r.UserId == "verify-batch-cust");
        await adminCrmSvc.MarkNoteReadAsync(noteId, "verify-batch-cust");
        var readCount2 = await db.NoteReads.CountAsync(r => r.CustomerNoteID == noteId
                                                         && r.UserId == "verify-batch-cust");
        await Check("MarkNoteReadAsync is idempotent (no duplicate rows)",
            () => Task.FromResult(readCount == 1 && readCount2 == 1));

        // Mark unread when not read is a no-op.
        await adminCrmSvc.MarkNoteUnreadAsync(noteId, "verify-batch-cust");
        await adminCrmSvc.MarkNoteUnreadAsync(noteId, "verify-batch-cust");
        await Check("MarkNoteUnreadAsync is idempotent (no error when already unread)", () =>
            Task.FromResult(true));

        // ---------- Test 17: Mark done drops count (final smoke for the merged model) ----------
        Console.WriteLine("\nTest 17: Mark done drops count for assignee (merged model)");

        var fuForDoneId = await adminCrmSvc.AddFollowUpAsync(new CustomerFollowUpCreateDto
        {
            CustomerID = newId,
            Reason = "Mark-done smoke",
            FollowUpDate = DateTime.UtcNow.Date,
            AssignedToUserId = "verify-batch-cust",
        }, actorUserId: "verify-batch-cust");

        await Check("Pending count includes the new follow-up", async () =>
            await adminCrmSvc.CountPendingForUserAsync("verify-batch-cust") == 1);

        await adminCrmSvc.CompleteFollowUpAsync(new CustomerFollowUpCompleteDto
        {
            CustomerFollowUpID = fuForDoneId,
            ResolutionNotes = "Done",
        }, actorUserId: "verify-batch-cust");

        await Check("After Mark done, count is 0", async () =>
            await adminCrmSvc.CountPendingForUserAsync("verify-batch-cust") == 0);

        Console.WriteLine($"\n{(failed == 0 ? "ALL TESTS PASSED" : $"{failed} test(s) FAILED")}");
        return failed == 0 ? 0 : 1;
    }

    private static async Task EnsureIdentityUserAsync(ApplicationDbContext db, string id, string firstName, string lastName)
    {
        if (await db.Users.AnyAsync(u => u.Id == id)) return;
        db.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = $"{id}@test.local",
            Email = $"{id}@test.local",
            FirstName = firstName,
            LastName = lastName,
            IsActive = true,
            SecurityStamp = id,
            ConcurrencyStamp = id,
        });
        await db.SaveChangesAsync();
    }
}
