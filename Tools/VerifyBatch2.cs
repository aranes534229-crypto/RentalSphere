// Verification script: exercises Customer + Reservation services against LocalDB.
// Confirms auto-create on register, customer scoping, availability check, confirm/cancel lifecycle.
// Run from repo root:
//   dotnet run --project Tools/VerifyBatch2.csproj

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Identity;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.CustomerManagement.Repositories;
using RentalSphere.Modules.CustomerManagement.Services;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.ReservationManagement.DTOs;
using RentalSphere.Modules.ReservationManagement.Models;
using RentalSphere.Modules.ReservationManagement.Repositories;
using RentalSphere.Modules.ReservationManagement.Services;
using System.Security.Claims;

namespace RentalSphere.Tools.VerifyBatch2;

/// <summary>
/// Minimal ICurrentUser impl that holds its principal by reference, bypassing
/// HttpContextAccessor (which uses AsyncLocal and behaves inconsistently across
/// test instances).
/// </summary>
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
        var staleCustomers = await db.Customers.Where(c => c.Email.StartsWith("verify-batch2-")).ToListAsync();
        if (staleCustomers.Any())
        {
            var staleCustIds = staleCustomers.Select(c => c.CustomerID).ToList();

            // RentalTransactions → RentalTransactionItems (FK Restrict on Customer; clear first).
            var staleRentals = await db.RentalTransactions
                .Where(t => staleCustIds.Contains(t.CustomerID))
                .ToListAsync();
            var staleRentalIds = staleRentals.Select(t => t.RentalTransactionID).ToList();
            if (staleRentalIds.Any())
            {
                var staleRentalItems = await db.RentalTransactionItems
                    .Where(i => staleRentalIds.Contains(i.RentalTransactionID))
                    .ToListAsync();
                db.RentalTransactionItems.RemoveRange(staleRentalItems);
                db.RentalTransactions.RemoveRange(staleRentals);
                await db.SaveChangesAsync();
            }

            var staleReservations = await db.Reservations
                .Where(r => staleCustIds.Contains(r.CustomerID))
                .ToListAsync();
            db.Reservations.RemoveRange(staleReservations);
            await db.SaveChangesAsync();
            db.Customers.RemoveRange(staleCustomers);
            await db.SaveChangesAsync();
        }

        var equipment = await db.Equipment.FirstAsync(e => e.Name.StartsWith("Round Banquet Table"));
        var otherEquipment = await db.Equipment.FirstAsync(e => e.Name.StartsWith("Folding Chair"));
        var baselineStock = equipment.StockQuantity;
        var otherBaselineStock = otherEquipment.StockQuantity;

        var staleItems = await db.EquipmentItems
            .Where(i => i.SerialNumber.StartsWith("VERIFY-BATCH2-"))
            .ToListAsync();
        if (staleItems.Any())
        {
            db.EquipmentItems.RemoveRange(staleItems);
            await db.SaveChangesAsync();
        }
        var verifyItems = new List<EquipmentItem>();
        for (int n = 1; n <= 10; n++)
        {
            verifyItems.Add(new EquipmentItem
            {
                EquipmentID = equipment.EquipmentID,
                SerialNumber = $"VERIFY-BATCH2-TBL-{n:000}",
                AvailabilityStatus = AvailabilityStatus.Available,
                Location = "Verification pool",
                LastStatusChange = DateTime.UtcNow,
            });
        }
        for (int n = 1; n <= 10; n++)
        {
            verifyItems.Add(new EquipmentItem
            {
                EquipmentID = otherEquipment.EquipmentID,
                SerialNumber = $"VERIFY-BATCH2-CHAIR-{n:000}",
                AvailabilityStatus = AvailabilityStatus.Available,
                Location = "Verification pool",
                LastStatusChange = DateTime.UtcNow,
            });
        }
        db.EquipmentItems.AddRange(verifyItems);
        await db.SaveChangesAsync();
        var totalAvailableItems = verifyItems.Count(i => i.EquipmentID == equipment.EquipmentID);
        Console.WriteLine($"Equipment: {equipment.Name} (id={equipment.EquipmentID})");
        Console.WriteLine($"Baseline StockQuantity: {baselineStock}");
        Console.WriteLine($"Available units (verify pool): {totalAvailableItems}");

        var customerRepo = new CustomerRepository(db);
        var equipmentRepo = new EquipmentRepository(db);
        var equipmentItemRepo = new EquipmentItemRepository(db);
        var reservationRepo = new ReservationRepository(db);
        var audit = new AuditLogger(db, new Microsoft.AspNetCore.Http.HttpContextAccessor());

        var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] {
                new Claim(ClaimTypes.NameIdentifier, "verify-batch2"),
                new Claim(ClaimTypes.Name, "verify-batch2"),
                new Claim(ClaimTypes.Role, RoleNames.Admin),
            },
            authenticationType: "Test"));
        var adminCurrent = new StubCurrentUser(adminPrincipal);

        var customerSvc = new CustomerService(customerRepo, db, adminCurrent, audit);
        var equipmentItemSvc = new EquipmentItemService(equipmentItemRepo, equipmentRepo, adminCurrent, audit);
        var equipmentSvc = new EquipmentService(equipmentRepo, new CategoryRepository(db), adminCurrent, audit);
        var reservationSvc = new ReservationService(
            reservationRepo, customerRepo, equipmentRepo, equipmentItemRepo,
            equipmentItemSvc, db, adminCurrent, audit);

        int failed = 0;
        async Task Check(string label, Func<Task<bool>> assertion)
        {
            var ok = await assertion();
            Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} - {label}");
            if (!ok) failed++;
        }

        // ---------- Test 1: EnsureForUserAsync auto-creates Customer row ----------
        Console.WriteLine("\nTest 1: EnsureForUserAsync auto-creates Customer");
        var fakeUser = new ApplicationUser
        {
            Id = "verify-batch2-user-a",
            UserName = "verify-batch2-a@test.local",
            Email = "verify-batch2-a@test.local",
            FirstName = "Ada",
            LastName = "Lovelace",
            IsActive = true,
            SecurityStamp = "verify-batch2-a",
            ConcurrencyStamp = "verify-batch2-a",
        };
        var existingIdentityUser = await db.Users.FirstOrDefaultAsync(u => u.Id == fakeUser.Id);
        if (existingIdentityUser is null)
        {
            db.Users.Add(fakeUser);
            await db.SaveChangesAsync();
        }

        // Also seed an admin Identity user that maps to the "verify-batch2" actor id,
        // so Reservation.CreatedByUserId FK is satisfied.
        var adminIdentityUser = await db.Users.FirstOrDefaultAsync(u => u.Id == "verify-batch2");
        if (adminIdentityUser is null)
        {
            db.Users.Add(new ApplicationUser
            {
                Id = "verify-batch2",
                UserName = "verify-batch2@test.local",
                Email = "verify-batch2@test.local",
                FirstName = "Verify",
                LastName = "Admin",
                IsActive = true,
                SecurityStamp = "verify-batch2",
                ConcurrencyStamp = "verify-batch2",
            });
            await db.SaveChangesAsync();
        }
        await customerSvc.EnsureForUserAsync(fakeUser);
        await Check("Customer row created with linked UserId", async () =>
        {
            var c = await db.Customers.FirstOrDefaultAsync(c => c.UserId == fakeUser.Id);
            return c is not null && c.FirstName == "Ada" && c.LastName == "Lovelace";
        });

        // ---------- Test 2: CreateAsync from Admin ----------
        Console.WriteLine("\nTest 2: Admin creates a walk-in Customer");
        var newCustomerId = await customerSvc.CreateAsync(new RentalSphere.Modules.CustomerManagement.DTOs.CustomerCreateDto
        {
            FirstName = "Grace",
            LastName = "Hopper",
            Email = "verify-batch2-grace@example.local",
            Phone = "555-0100",
            City = "Manila",
        }, actorUserId: "verify-batch2");
        // Tier is no longer set on create; pin a manual override to keep the test's
        // intent (Grace is a Gold customer) without exposing the field on the DTO.
        await customerSvc.OverrideTierAsync(newCustomerId, LoyaltyTier.Gold, "verify seed", actorUserId: "verify-batch2");
        await Check("Customer ID assigned and persisted", async () =>
        {
            var c = await db.Customers.FirstOrDefaultAsync(c => c.CustomerID == newCustomerId);
            return c is not null && c.ManualOverrideTier == LoyaltyTier.Gold;
        });

        // ---------- Test 3: Customer scoping (Customer A can't read Customer B's details) ----------
        Console.WriteLine("\nTest 3: Customer scoping prevents cross-customer read");
        var customerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] {
                new Claim(ClaimTypes.NameIdentifier, fakeUser.Id),
                new Claim(ClaimTypes.Name, fakeUser.UserName!),
                new Claim(ClaimTypes.Role, RoleNames.Customer),
            },
            authenticationType: "Test"));
        var customerCurrent = new StubCurrentUser(customerPrincipal);
        var customerScopedCustomerSvc = new CustomerService(customerRepo, db, customerCurrent, audit);
        var crossReadBlocked = false;
        try
        {
            await customerScopedCustomerSvc.GetDetailsAsync(newCustomerId);
        }
        catch (NotFoundException)
        {
            crossReadBlocked = true;
        }
        await Check("Customer A reading Customer B throws NotFoundException", () => Task.FromResult(crossReadBlocked));

        // ---------- Test 4: Availability check passes when equipment is free ----------
        Console.WriteLine("\nTest 4: Availability check passes when free");
        var start = DateTime.UtcNow.Date.AddDays(30);
        var end = start.AddDays(3);
        var availability = await reservationSvc.CheckAvailabilityAsync(start, end, new List<ReservationItemInputDto>
        {
            new() { EquipmentID = equipment.EquipmentID, Quantity = 2 },
        });
        await Check($"2 units available between {start:yyyy-MM-dd} and {end:yyyy-MM-dd}", () => Task.FromResult(availability.IsAvailable && availability.Lines[0].Available >= 2));

        // ---------- Test 5: Create + Confirm + Assigned items + StockQuantity decrement ----------
        Console.WriteLine("\nTest 5: Create reservation, confirm, verify assigned items + stock");
        var reservationId = await reservationSvc.CreateAsync(new ReservationCreateDto
        {
            CustomerID = newCustomerId,
            RentalStartDate = start,
            RentalEndDate = end,
            Items = new List<ReservationItemInputDto>
            {
                new() { EquipmentID = equipment.EquipmentID, Quantity = 2 },
            },
            Notes = "Verify batch 2 test",
        }, actorUserId: "verify-batch2");

        await reservationSvc.ConfirmAsync(reservationId, actorUserId: "verify-batch2");

        await Check("Reservation status = Confirmed", async () =>
        {
            var r = await db.Reservations.FindAsync(reservationId);
            return r is not null && r.Status == ReservationStatus.Confirmed;
        });

        await Check("2 EquipmentItems moved to Reserved", async () =>
        {
            var reservedCount = await db.EquipmentItems
                .CountAsync(i => i.EquipmentID == equipment.EquipmentID && i.AvailabilityStatus == AvailabilityStatus.Reserved);
            return reservedCount >= 2;
        });

        await Check("StockQuantity decremented by 2", async () =>
        {
            await db.Entry(equipment).ReloadAsync();
            return equipment.StockQuantity == baselineStock - 2;
        });

        // ---------- Test 6: Availability check fails on overlapping window ----------
        Console.WriteLine("\nTest 6: Availability check fails for overlapping range");
        var overlap = await reservationSvc.CheckAvailabilityAsync(start.AddDays(1), end.AddDays(1), new List<ReservationItemInputDto>
        {
            new() { EquipmentID = equipment.EquipmentID, Quantity = totalAvailableItems },
        });
        await Check("Overlap against all units reports IsAvailable=false", () => Task.FromResult(!overlap.IsAvailable));

        // ---------- Test 7: Cancel of Confirmed releases items + restores StockQuantity ----------
        Console.WriteLine("\nTest 7: Cancel of Confirmed reservation releases items");
        await reservationSvc.CancelAsync(reservationId, actorUserId: "verify-batch2", reason: "verify test");
        await Check("Reservation status = Cancelled", async () =>
        {
            var r = await db.Reservations.FindAsync(reservationId);
            return r is not null && r.Status == ReservationStatus.Cancelled;
        });
        await Check("StockQuantity restored to baseline", async () =>
        {
            await db.Entry(equipment).ReloadAsync();
            return equipment.StockQuantity == baselineStock;
        });

        // ---------- Test 8: Multi-item reservation ----------
        Console.WriteLine("\nTest 8: Multi-item reservation");
        var multiStart = DateTime.UtcNow.Date.AddDays(45);
        var multiEnd = multiStart.AddDays(2);
        var multiId = await reservationSvc.CreateAsync(new ReservationCreateDto
        {
            CustomerID = newCustomerId,
            RentalStartDate = multiStart,
            RentalEndDate = multiEnd,
            Items = new List<ReservationItemInputDto>
            {
                new() { EquipmentID = equipment.EquipmentID, Quantity = 1 },
                new() { EquipmentID = otherEquipment.EquipmentID, Quantity = 5 },
            },
        }, actorUserId: "verify-batch2");
        await reservationSvc.ConfirmAsync(multiId, actorUserId: "verify-batch2");

        await Check("Multi-item reservation has 2 line items", async () =>
        {
            var count = await db.ReservationItems.CountAsync(ri => ri.ReservationID == multiId);
            return count == 2;
        });

        await reservationSvc.CancelAsync(multiId, actorUserId: "verify-batch2", reason: "cleanup");
        await Check("Multi-item cancel restores both stocks", async () =>
        {
            await db.Entry(otherEquipment).ReloadAsync();
            await db.Entry(equipment).ReloadAsync();
            return equipment.StockQuantity == baselineStock && otherEquipment.StockQuantity == otherBaselineStock;
        });

        // ---------- Test 9: Audit log entries ----------
        Console.WriteLine("\nTest 9: Audit log entries recorded for reservation operations");
        var auditEntries = await db.AuditLogs
            .Where(a => a.Action.StartsWith("Reservation.") || a.Action.StartsWith("Customer."))
            .CountAsync();
        await Check($"At least 5 Customer/Reservation audit entries (got {auditEntries})",
            () => Task.FromResult(auditEntries >= 5));

        Console.WriteLine($"\n{(failed == 0 ? "ALL TESTS PASSED" : $"{failed} test(s) FAILED")}");
        return failed == 0 ? 0 : 1;
    }
}