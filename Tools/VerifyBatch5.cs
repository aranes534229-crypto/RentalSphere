// Verification script: exercises Batch 5 — Customer CRM module
// (notes + follow-ups) and Equipment Availability calendar.
// Run from repo root:
//   dotnet run --project Tools/VerifyBatch5.csproj

using Microsoft.AspNetCore.Http;
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
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.CustomerManagement.Repositories;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.EquipmentAvailability.DTOs;
using RentalSphere.Modules.EquipmentAvailability.Services;
using RentalSphere.Modules.ReservationManagement.Models;
using RentalSphere.Modules.ReservationManagement.Repositories;
using System.Security.Claims;

namespace RentalSphere.Tools.VerifyBatch5;

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
    private static int _passed;
    private static int _failed;

    public static async Task<int> Main()
    {
        var connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=RentalSphereDb;Trusted_Connection=True;TrustServerCertificate=True;";

        var dbOpts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var db = new ApplicationDbContext(dbOpts);

        // Cleanup stale verify data.
        var stale = await db.Customers
            .Where(c => c.Email.StartsWith("verify-batch5-"))
            .ToListAsync();
        if (stale.Any())
        {
            var staleIds = stale.Select(c => c.CustomerID).ToList();
            var staleNotes = await db.CustomerNotes.Where(n => staleIds.Contains(n.CustomerID)).ToListAsync();
            var staleFus = await db.CustomerFollowUps.Where(f => staleIds.Contains(f.CustomerID)).ToListAsync();
            db.CustomerNotes.RemoveRange(staleNotes);
            db.CustomerFollowUps.RemoveRange(staleFus);
            db.Customers.RemoveRange(stale);
            await db.SaveChangesAsync();
        }

        if (await db.Users.FirstOrDefaultAsync(u => u.Id == "verify-batch5") is null)
        {
            db.Users.Add(new ApplicationUser
            {
                Id = "verify-batch5",
                UserName = "verify-batch5@test.local",
                Email = "verify-batch5@test.local",
                FirstName = "Verify",
                LastName = "Batch5",
                IsActive = true,
                SecurityStamp = "verify-batch5",
                ConcurrencyStamp = "verify-batch5",
            });
            await db.SaveChangesAsync();
        }

        // Seed customer with linked user (for the customer-scoping test).
        var linkedCustomer = await db.Customers
            .FirstOrDefaultAsync(c => c.Email == "verify-batch5-linked@test.local");
        if (linkedCustomer is null)
        {
            if (await db.Users.FirstOrDefaultAsync(u => u.Id == "verify-batch5-customer") is null)
            {
                db.Users.Add(new ApplicationUser
                {
                    Id = "verify-batch5-customer",
                    UserName = "verify-batch5-customer@test.local",
                    Email = "verify-batch5-customer@test.local",
                    FirstName = "Customer",
                    LastName = "Linked",
                    IsActive = true,
                    SecurityStamp = "verify-batch5-customer",
                    ConcurrencyStamp = "verify-batch5-customer",
                });
                await db.SaveChangesAsync();
            }

            linkedCustomer = new Customer
            {
                FirstName = "Linked",
                LastName = "Customer",
                Email = "verify-batch5-linked@test.local",
                UserId = "verify-batch5-customer",
                IsActive = true,
                DateRegistered = DateTime.UtcNow,
            };
            db.Customers.Add(linkedCustomer);
            await db.SaveChangesAsync();
        }

        // Seed an unlinked customer.
        var plainCustomer = await db.Customers
            .FirstOrDefaultAsync(c => c.Email == "verify-batch5-plain@test.local");
        if (plainCustomer is null)
        {
            plainCustomer = new Customer
            {
                FirstName = "Plain",
                LastName = "Customer",
                Email = "verify-batch5-plain@test.local",
                IsActive = true,
                DateRegistered = DateTime.UtcNow,
            };
            db.Customers.Add(plainCustomer);
            await db.SaveChangesAsync();
        }

        var audit = new AuditLogger(db, new HttpContextAccessor());
        var customerRepo = new CustomerRepository(db);
        var crmRepo = new CustomerCrmRepository(db);
        var equipmentRepo = new EquipmentRepository(db);
        var reservationRepo = new ReservationRepository(db);

        var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "verify-batch5"),
            new Claim(ClaimTypes.Name, "verify-batch5"),
            new Claim(ClaimTypes.Role, RoleNames.Admin),
        }, authenticationType: "Test"));

        var customerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "verify-batch5-customer"),
            new Claim(ClaimTypes.Name, "verify-batch5-customer"),
            new Claim(ClaimTypes.Role, RoleNames.Customer),
        }, authenticationType: "Test"));

        var otherCustomerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "verify-batch5"),
            new Claim(ClaimTypes.Name, "verify-batch5"),
            new Claim(ClaimTypes.Role, RoleNames.Customer),
        }, authenticationType: "Test"));

        var adminCurrent = new StubCurrentUser(adminPrincipal);
        var customerCurrent = new StubCurrentUser(customerPrincipal);
        var otherCustomerCurrent = new StubCurrentUser(otherCustomerPrincipal);

        var crmSvc = new CustomerCrmService(crmRepo, customerRepo, db, adminCurrent, audit);
        var crmSvcAsCustomer = new CustomerCrmService(crmRepo, customerRepo, db, customerCurrent, audit);
        var crmSvcAsOtherCustomer = new CustomerCrmService(crmRepo, customerRepo, db, otherCustomerCurrent, audit);
        var availabilitySvc = new AvailabilityService(db);

        // ---- Test 1: Admin adds a note ----
        var noteDto = new CustomerNoteCreateDto
        {
            CustomerID = plainCustomer.CustomerID,
            Body = "Customer requested follow-up quote for 200 chairs.",
            IsFlagged = true,
        };
        var noteId = await crmSvc.AddNoteAsync(noteDto, "verify-batch5");
        var note = await db.CustomerNotes.FindAsync(noteId);
        Assert("T1: note persisted with IsFlagged=true",
            note is not null && note.Body == noteDto.Body && note.IsFlagged);

        // ---- Test 2: Customer cannot add notes ----
        var caught = false;
        try
        {
            await crmSvcAsCustomer.AddNoteAsync(
                new CustomerNoteCreateDto
                {
                    CustomerID = linkedCustomer.CustomerID,
                    Body = "Self-note attempt",
                },
                "verify-batch5-customer");
        }
        catch (ForbiddenException) { caught = true; }
        Assert("T2: Customer role forbidden from AddNote", caught);

        // ---- Test 3: Admin schedules a follow-up ----
        var fuDto = new CustomerFollowUpCreateDto
        {
            CustomerID = plainCustomer.CustomerID,
            Reason = "Confirm satisfaction post-event",
            FollowUpDate = DateTime.UtcNow.Date.AddDays(7),
        };
        var fuId = await crmSvc.AddFollowUpAsync(fuDto, "verify-batch5");
        var fu = await db.CustomerFollowUps.FindAsync(fuId);
        Assert("T3: follow-up persisted with Pending status",
            fu is not null && fu.Status == FollowUpStatus.Pending);

        // ---- Test 4: Past date rejected ----
        caught = false;
        try
        {
            await crmSvc.AddFollowUpAsync(
                new CustomerFollowUpCreateDto
                {
                    CustomerID = plainCustomer.CustomerID,
                    Reason = "Bad",
                    FollowUpDate = DateTime.UtcNow.Date.AddDays(-1),
                },
                "verify-batch5");
        }
        catch (InvalidOperationException) { caught = true; }
        Assert("T4: past-date follow-up rejected", caught);

        // ---- Test 5: Customer completes follow-up is forbidden ----
        caught = false;
        try
        {
            await crmSvcAsCustomer.CompleteFollowUpAsync(
                new CustomerFollowUpCompleteDto { CustomerFollowUpID = fuId },
                "verify-batch5-customer");
        }
        catch (ForbiddenException) { caught = true; }
        Assert("T5: Customer role forbidden from CompleteFollowUp", caught);

        // ---- Test 6: Admin completes follow-up ----
        await crmSvc.CompleteFollowUpAsync(
            new CustomerFollowUpCompleteDto
            {
                CustomerFollowUpID = fuId,
                ResolutionNotes = "Customer satisfied.",
            },
            "verify-batch5");
        var fu2 = await db.CustomerFollowUps.FindAsync(fuId);
        Assert("T6: Admin completes follow-up",
            fu2!.Status == FollowUpStatus.Done && fu2.CompletedAt.HasValue);

        // ---- Test 7: Cancelling a Done follow-up is rejected ----
        caught = false;
        try
        {
            await crmSvc.CancelFollowUpAsync(fuId, "verify-batch5");
        }
        catch (InvalidOperationException) { caught = true; }
        Assert("T7: cancelling a Done follow-up rejected", caught);

        // ---- Test 8: Customer sees their own CRM tab ----
        var tab = await crmSvcAsCustomer.GetTabAsync(linkedCustomer.CustomerID);
        Assert("T8: customer can read their own CRM tab",
            tab.CustomerID == linkedCustomer.CustomerID);

        // ---- Test 9: Customer cannot read another customer's CRM tab ----
        caught = false;
        try
        {
            await crmSvcAsOtherCustomer.GetTabAsync(linkedCustomer.CustomerID);
        }
        catch (ForbiddenException) { caught = true; }
        Assert("T9: customer forbidden from another customer's CRM", caught);

        // ---- Test 10: ListMyFollowUps returns the linked customer's follow-ups ----
        var myFus = await crmSvcAsCustomer.ListMyFollowUpsAsync("verify-batch5-customer");
        Assert("T10: ListMyFollowUps scoped to linked customer", myFus is not null);

        // ---- Test 11: Overdue queue contains past-due follow-ups ----
        var pastFu = new CustomerFollowUp
        {
            CustomerID = plainCustomer.CustomerID,
            Reason = "Overdue check",
            FollowUpDate = DateTime.UtcNow.Date.AddDays(-3),
            Status = FollowUpStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "verify-batch5",
        };
        db.CustomerFollowUps.Add(pastFu);
        await db.SaveChangesAsync();
        var overdue = await crmRepo.ListOverdueFollowUpsAsync(DateTime.UtcNow.Date.AddDays(1));
        Assert("T11: overdue queue contains past-due Pending follow-ups",
            overdue.Any(f => f.CustomerFollowUpID == pastFu.CustomerFollowUpID));

        // ---- Test 12: Audit logs written ----
        var auditLogs = await db.AuditLogs
            .Where(a => a.ActorUserId == "verify-batch5"
                     && (a.Action == "CustomerNote.Create"
                      || a.Action == "CustomerFollowUp.Create"
                      || a.Action == "CustomerFollowUp.Complete"))
            .CountAsync();
        Assert("T12: audit log entries written for CRM actions", auditLogs >= 3);

        // ---- Test 13: Availability calendar returns rows ----
        var calendar = await availabilitySvc.GetCalendarAsync(
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(7));
        Assert("T13: availability calendar returns rows",
            calendar.Rows.Any());

        // ---- Test 14: Availability rejects invalid date range ----
        caught = false;
        try
        {
            await availabilitySvc.GetCalendarAsync(
                DateTime.UtcNow.Date.AddDays(5),
                DateTime.UtcNow.Date);
        }
        catch (InvalidOperationException) { caught = true; }
        Assert("T14: availability rejects end <= start", caught);

        Console.WriteLine();
        Console.WriteLine($"=== Batch 5 verification: {_passed} passed, {_failed} failed ===");
        return _failed == 0 ? 0 : 1;
    }

    private static void Assert(string label, bool condition)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  PASS  {label}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  FAIL  {label}");
        }
    }
}
