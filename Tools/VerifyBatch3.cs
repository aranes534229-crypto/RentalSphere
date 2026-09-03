// Verification script: exercises Batch 3 modules — Rental Transaction lifecycle,
// Equipment Availability calendar, and Maintenance open/close.
// Run from repo root:
//   dotnet run --project Tools/VerifyBatch3.csproj

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Identity;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.CustomerManagement.Repositories;
using RentalSphere.Modules.CustomerManagement.Services;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.EquipmentAvailability.Services;
using RentalSphere.Modules.Maintenance.DTOs;
using RentalSphere.Modules.Maintenance.Models;
using RentalSphere.Modules.Maintenance.Repositories;
using RentalSphere.Modules.Maintenance.Services;
using RentalSphere.Modules.RentalTransaction.DTOs;
using RentalSphere.Modules.RentalTransaction.Models;
using RentalSphere.Modules.RentalTransaction.Repositories;
using RentalSphere.Modules.RentalTransaction.Services;
using RentalSphere.Modules.ReservationManagement.DTOs;
using RentalSphere.Modules.ReservationManagement.Models;
using RentalSphere.Modules.ReservationManagement.Repositories;
using RentalSphere.Modules.ReservationManagement.Services;
using System.Security.Claims;

namespace RentalSphere.Tools.VerifyBatch3;

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

        // Clean stale verification data (idempotent).
        var staleCustomers = await db.Customers.Where(c => c.Email.StartsWith("verify-batch3-")).ToListAsync();
        if (staleCustomers.Any())
        {
            var staleCustIds = staleCustomers.Select(c => c.CustomerID).ToList();

            // Walk backwards through FKs to free the tables for deletion.
            var staleRentals = await db.RentalTransactions
                .Where(t => staleCustIds.Contains(t.CustomerID))
                .ToListAsync();
            var staleRentalIds = staleRentals.Select(r => r.RentalTransactionID).ToList();
            var staleRentalItems = await db.RentalTransactionItems
                .Where(i => staleRentalIds.Contains(i.RentalTransactionID))
                .ToListAsync();
            db.RentalTransactionItems.RemoveRange(staleRentalItems);
            db.RentalTransactions.RemoveRange(staleRentals);

            var staleReservations = await db.Reservations
                .Where(r => staleCustIds.Contains(r.CustomerID))
                .ToListAsync();
            var staleResIds = staleReservations.Select(r => r.ReservationID).ToList();
            var staleResItems = await db.ReservationItems
                .Where(i => staleResIds.Contains(i.ReservationID))
                .ToListAsync();
            db.ReservationItems.RemoveRange(staleResItems);
            db.Reservations.RemoveRange(staleReservations);

            var staleItems = await db.EquipmentItems
                .Where(i => i.SerialNumber.StartsWith("VERIFY-BATCH3-"))
                .ToListAsync();
            var staleItemIds = staleItems.Select(i => i.ItemID).ToList();
            var staleMaint = await db.MaintenanceRecords
                .Where(m => staleItemIds.Contains(m.EquipmentItemID))
                .ToListAsync();
            db.MaintenanceRecords.RemoveRange(staleMaint);
            db.EquipmentItems.RemoveRange(staleItems);

            db.Customers.RemoveRange(staleCustomers);
            await db.SaveChangesAsync();
        }

        var equipment = await db.Equipment.FirstAsync(e => e.Name.StartsWith("Round Banquet Table"));
        var otherEquipment = await db.Equipment.FirstAsync(e => e.Name.StartsWith("Folding Chair"));
        var baselineStock = equipment.StockQuantity;
        var otherBaselineStock = otherEquipment.StockQuantity;

        // Always reset the verify pool to 10+10 fresh Available items — leaks from previous
        // runs would otherwise pollute other verification scripts that count Available units.
        var existingItems = await db.EquipmentItems
            .Where(i => i.SerialNumber.StartsWith("VERIFY-BATCH3-"))
            .ToListAsync();
        if (existingItems.Any())
        {
            var leftoverMaint = await db.MaintenanceRecords
                .Where(m => existingItems.Select(i => i.ItemID).Contains(m.EquipmentItemID))
                .ToListAsync();
            db.MaintenanceRecords.RemoveRange(leftoverMaint);
            db.EquipmentItems.RemoveRange(existingItems);
            await db.SaveChangesAsync();
        }
        var seedItems = new List<EquipmentItem>();
        for (int n = 1; n <= 10; n++)
        {
            seedItems.Add(new EquipmentItem
            {
                EquipmentID = equipment.EquipmentID,
                SerialNumber = $"VERIFY-BATCH3-TBL-{n:000}",
                AvailabilityStatus = AvailabilityStatus.Available,
                Location = "Verification pool",
                LastStatusChange = DateTime.UtcNow,
            });
        }
        for (int n = 1; n <= 10; n++)
        {
            seedItems.Add(new EquipmentItem
            {
                EquipmentID = otherEquipment.EquipmentID,
                SerialNumber = $"VERIFY-BATCH3-CHAIR-{n:000}",
                AvailabilityStatus = AvailabilityStatus.Available,
                Location = "Verification pool",
                LastStatusChange = DateTime.UtcNow,
            });
        }
        db.EquipmentItems.AddRange(seedItems);
        await db.SaveChangesAsync();

        // Seed admin Identity user for FK targets.
        var adminIdentityUser = await db.Users.FirstOrDefaultAsync(u => u.Id == "verify-batch3");
        if (adminIdentityUser is null)
        {
            db.Users.Add(new ApplicationUser
            {
                Id = "verify-batch3",
                UserName = "verify-batch3@test.local",
                Email = "verify-batch3@test.local",
                FirstName = "Verify",
                LastName = "Batch3",
                IsActive = true,
                SecurityStamp = "verify-batch3",
                ConcurrencyStamp = "verify-batch3",
            });
            await db.SaveChangesAsync();
        }
        // Seed customer identity user.
        var customerIdentityUser = await db.Users.FirstOrDefaultAsync(u => u.Id == "verify-batch3-cust");
        if (customerIdentityUser is null)
        {
            db.Users.Add(new ApplicationUser
            {
                Id = "verify-batch3-cust",
                UserName = "verify-batch3-cust@test.local",
                Email = "verify-batch3-cust@test.local",
                FirstName = "Batch3",
                LastName = "Customer",
                IsActive = true,
                SecurityStamp = "verify-batch3-cust",
                ConcurrencyStamp = "verify-batch3-cust",
            });
            await db.SaveChangesAsync();
        }

        // Wire up the services (admin scope).
        var customerRepo = new CustomerRepository(db);
        var equipmentRepo = new EquipmentRepository(db);
        var equipmentItemRepo = new EquipmentItemRepository(db);
        var reservationRepo = new ReservationRepository(db);
        var rentalRepo = new RentalTransactionRepository(db);
        var maintRepo = new MaintenanceRepository(db);
        var audit = new AuditLogger(db, new Microsoft.AspNetCore.Http.HttpContextAccessor());

        var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] {
                new Claim(ClaimTypes.NameIdentifier, "verify-batch3"),
                new Claim(ClaimTypes.Name, "verify-batch3"),
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
        var billingSvc = new RentalSphere.Modules.Billing.Services.BillingService(
            new RentalSphere.Modules.Billing.Repositories.BillingRepository(db),
            rentalRepo,
            new RentalSphere.Modules.DamagePenalty.Repositories.DamagePenaltyRepository(db),
            customerRepo,
            adminCurrent, audit,
            new RentalSphere.Modules.Billing.Services.BillingOptions(100m, 14));
        var rentalSvc = new RentalTransactionService(
            rentalRepo, reservationRepo, equipmentItemRepo, equipmentItemSvc,
            customerRepo, billingSvc, adminCurrent, audit);
        var maintSvc = new MaintenanceService(maintRepo, equipmentItemSvc, adminCurrent, audit);
        var availSvc = new AvailabilityService(db);

        int failed = 0;
        async Task Check(string label, Func<Task<bool>> assertion)
        {
            var ok = await assertion();
            Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} - {label}");
            if (!ok) failed++;
        }

        // Walk-in admin customer for tests.
        var newCustomerId = await customerSvc.CreateAsync(new RentalSphere.Modules.CustomerManagement.DTOs.CustomerCreateDto
        {
            FirstName = "Marie",
            LastName = "Curie",
            Email = "verify-batch3-marie@example.local",
            Phone = "555-0103",
            City = "Quezon City",
        }, actorUserId: "verify-batch3");
        await customerSvc.OverrideTierAsync(newCustomerId, LoyaltyTier.Silver, "verify seed", actorUserId: "verify-batch3");

        // Link a second customer to the seeded Identity user (for scoping test).
        var customerCustSvc = new CustomerService(customerRepo, db, adminCurrent, audit);
        await customerCustSvc.EnsureForUserAsync(new ApplicationUser
        {
            Id = "verify-batch3-cust",
            UserName = "verify-batch3-cust@test.local",
            Email = "verify-batch3-cust@test.local",
            FirstName = "Batch3",
            LastName = "Customer",
        });

        // ---------- Test 1: Checkout pins concrete EquipmentItems ----------
        Console.WriteLine("\nTest 1: Checkout pins concrete items and flips them to CheckedOut");
        var start = DateTime.UtcNow.Date.AddDays(60);
        var end = start.AddDays(2);
        var reservationId = await reservationSvc.CreateAsync(new ReservationCreateDto
        {
            CustomerID = newCustomerId,
            RentalStartDate = start,
            RentalEndDate = end,
            Items = new List<ReservationItemInputDto>
            {
                new() { EquipmentID = equipment.EquipmentID, Quantity = 2 },
            },
        }, actorUserId: "verify-batch3");
        await reservationSvc.ConfirmAsync(reservationId, actorUserId: "verify-batch3");

        var txId = await rentalSvc.CheckoutAsync(reservationId, "verify-batch3");
        await Check("Rental transaction created", async () =>
        {
            var t = await db.RentalTransactions.FindAsync(txId);
            return t is not null && t.Status == RentalTransactionStatus.Active;
        });
        await Check("2 EquipmentItems moved to CheckedOut", async () =>
        {
            var count = await db.EquipmentItems
                .CountAsync(i => i.EquipmentID == equipment.EquipmentID
                              && i.AvailabilityStatus == AvailabilityStatus.CheckedOut);
            return count >= 2;
        });
        await Check("2 RentalTransactionItems recorded with pinned units", async () =>
        {
            var items = await db.RentalTransactionItems
                .Where(i => i.RentalTransactionID == txId)
                .ToListAsync();
            return items.Count == 2 && items.All(i => i.EquipmentItemID.HasValue);
        });

        // ---------- Test 2: Return restores items ----------
        Console.WriteLine("\nTest 2: Return restores items to Available and sets ReturnDate");
        await rentalSvc.ReturnAsync(new RentalTransactionReturnDto
        {
            RentalTransactionID = txId,
            ConditionNotes = "Returned clean.",
            FlaggedForDamage = false,
        }, actorUserId: "verify-batch3");
        await Check("Transaction status = Returned", async () =>
        {
            var t = await db.RentalTransactions.FindAsync(txId);
            return t is not null && t.Status == RentalTransactionStatus.Returned && t.ReturnDate.HasValue;
        });
        await Check("Pinned items back to Available", async () =>
        {
            var stillCheckedOut = await db.RentalTransactionItems
                .Where(i => i.RentalTransactionID == txId && i.EquipmentItemID.HasValue)
                .Join(db.EquipmentItems,
                      i => i.EquipmentItemID!.Value,
                      ei => ei.ItemID,
                      (i, ei) => ei)
                .CountAsync(ei => ei.AvailabilityStatus == AvailabilityStatus.CheckedOut);
            return stillCheckedOut == 0;
        });

        // ---------- Test 3: TotalAmount snapshot preserved ----------
        Console.WriteLine("\nTest 3: TotalAmount snapshot preserved");
        var txRow = await db.RentalTransactions.FindAsync(txId);
        var reservationRow = await db.Reservations.FindAsync(reservationId);
        await Check("TotalAmount matches reservation estimate", () =>
            Task.FromResult(txRow is not null
                         && reservationRow is not null
                         && txRow.TotalAmount == reservationRow.TotalEstimatedCost));

        // ---------- Test 4: Customer scoping ----------
        Console.WriteLine("\nTest 4: Customer scoping on transaction Details");
        var customerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] {
                new Claim(ClaimTypes.NameIdentifier, "verify-batch3-cust"),
                new Claim(ClaimTypes.Name, "verify-batch3-cust"),
                new Claim(ClaimTypes.Role, RoleNames.Customer),
            },
            authenticationType: "Test"));
        var customerCurrent = new StubCurrentUser(customerPrincipal);
        var customerRentalSvc = new RentalTransactionService(
            rentalRepo, reservationRepo, equipmentItemRepo, equipmentItemSvc,
            customerRepo, billingSvc, customerCurrent, audit);

        var crossBlocked = false;
        try
        {
            await customerRentalSvc.GetDetailsAsync(txId);
        }
        catch (NotFoundException)
        {
            crossBlocked = true;
        }
        await Check("Customer A's GetDetails on Customer B's transaction throws NotFoundException",
            () => Task.FromResult(crossBlocked));

        // ---------- Test 5: Two-checkout same reservation blocked ----------
        Console.WriteLine("\nTest 5: Re-checkout of same reservation blocked");
        var secondCheckoutBlocked = false;
        try
        {
            await rentalSvc.CheckoutAsync(reservationId, "verify-batch3");
        }
        catch (InvalidOperationException)
        {
            secondCheckoutBlocked = true;
        }
        await Check("Re-checkout rejected", () => Task.FromResult(secondCheckoutBlocked));

        // ---------- Test 6: Availability calendar reports correct counts ----------
        Console.WriteLine("\nTest 6: Availability calendar counts");
        var calStart = DateTime.UtcNow.Date.AddDays(80);
        var calEnd = calStart.AddDays(3);
        var calendar = await availSvc.GetCalendarAsync(calStart, calEnd);
        var eqRow = calendar.Rows.FirstOrDefault(r => r.EquipmentID == equipment.EquipmentID);
        await Check("Calendar returns equipment row", () => Task.FromResult(eqRow is not null));
        await Check("Calendar cells all >= 0", () =>
            Task.FromResult(eqRow?.Cells.All(c => c.Available >= 0) ?? false));
        await Check("Calendar reflects 0 locked on a clear window",
            () => Task.FromResult(eqRow?.Cells.All(c => c.Available >= 10) ?? false));

        // ---------- Test 7: Maintenance Open flips item + Close flips back ----------
        Console.WriteLine("\nTest 7: Maintenance open/close lifecycle");
        var availableItem = await db.EquipmentItems
            .Where(i => i.EquipmentID == equipment.EquipmentID
                     && i.AvailabilityStatus == AvailabilityStatus.Available)
            .FirstAsync();
        var maintId = await maintSvc.OpenAsync(new MaintenanceOpenDto
        {
            EquipmentItemID = availableItem.ItemID,
            Reason = "Wobbly leg",
            Cost = 250m,
        }, actorUserId: "verify-batch3");
        await Check("Maintenance record created", async () =>
        {
            var m = await db.MaintenanceRecords.FindAsync(maintId);
            return m is not null && m.Status == MaintenanceStatus.InProgress;
        });
        await Check("Item moved to InMaintenance", async () =>
        {
            var item = await db.EquipmentItems.FindAsync(availableItem.ItemID);
            return item is not null && item.AvailabilityStatus == AvailabilityStatus.InMaintenance;
        });
        await Check("StockQuantity decremented by 1", async () =>
        {
            await db.Entry(equipment).ReloadAsync();
            return equipment.StockQuantity == baselineStock - 1;
        });

        await maintSvc.CloseAsync(new MaintenanceCloseDto
        {
            MaintenanceRecordID = maintId,
            Notes = "Tightened bolt.",
        }, actorUserId: "verify-batch3");
        await Check("Item back to Available after close", async () =>
        {
            var item = await db.EquipmentItems.FindAsync(availableItem.ItemID);
            return item is not null && item.AvailabilityStatus == AvailabilityStatus.Available;
        });
        await Check("StockQuantity restored", async () =>
        {
            await db.Entry(equipment).ReloadAsync();
            return equipment.StockQuantity == baselineStock;
        });

        // ---------- Test 8: Maintenance Open blocked if one already open ----------
        Console.WriteLine("\nTest 8: Maintenance Open blocks duplicate open");
        // Open a fresh record (Test 7 closed its record), then try to open another.
        var dupItem = await db.EquipmentItems
            .Where(i => i.EquipmentID == equipment.EquipmentID
                     && i.AvailabilityStatus == AvailabilityStatus.Available)
            .FirstAsync();
        var firstMaintId = await maintSvc.OpenAsync(new MaintenanceOpenDto
        {
            EquipmentItemID = dupItem.ItemID,
            Reason = "First reason",
        }, actorUserId: "verify-batch3");
        var blockedDup = false;
        try
        {
            await maintSvc.OpenAsync(new MaintenanceOpenDto
            {
                EquipmentItemID = dupItem.ItemID,
                Reason = "Second reason",
            }, actorUserId: "verify-batch3");
        }
        catch (InvalidOperationException)
        {
            blockedDup = true;
        }
        await Check("Second open on same item rejected", () => Task.FromResult(blockedDup));
        // Cleanup so the next test rerun starts clean.
        await maintSvc.CloseAsync(new MaintenanceCloseDto { MaintenanceRecordID = firstMaintId }, actorUserId: "verify-batch3");

        // ---------- Test 9: Audit log entries ----------
        Console.WriteLine("\nTest 9: Audit log entries recorded");
        var auditEntries = await db.AuditLogs
            .Where(a => a.Action.StartsWith("RentalTransaction.")
                     || a.Action.StartsWith("Maintenance."))
            .CountAsync();
        await Check($"At least 5 Batch 3 audit entries (got {auditEntries})",
            () => Task.FromResult(auditEntries >= 5));

        Console.WriteLine($"\n{(failed == 0 ? "ALL TESTS PASSED" : $"{failed} test(s) FAILED")}");
        return failed == 0 ? 0 : 1;
    }
}
