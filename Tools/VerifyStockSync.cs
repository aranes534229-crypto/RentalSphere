// Verification script: exercises EquipmentItemService against LocalDB to confirm
// StockQuantity syncs correctly on item create/status-change/delete. Not shipped
// in the runtime; kept in Tools/ for re-runs after future changes.
//
// Run from repo root:
//   dotnet run --project Tools/VerifyStockSync.csproj

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Equipment.DTOs;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.Services;
using System.Security.Claims;

namespace RentalSphere.Tools.VerifyStockSync;

public static class Program
{
    public static async Task<int> Main()
    {
        var connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=RentalSphereDb;Trusted_Connection=True;TrustServerCertificate=True;";

        var dbOpts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var db = new ApplicationDbContext(dbOpts);

        // Reset any prior verification items so the script is idempotent.
        var stale = await db.EquipmentItems.Where(i => i.SerialNumber.StartsWith("VERIFY-")).ToListAsync();
        if (stale.Any())
        {
            var staleIds = stale.Select(i => i.ItemID).ToList();
            // MaintenanceRecord has FK Restrict on EquipmentItem; clear any open ones first.
            var staleMaint = await db.MaintenanceRecords
                .Where(m => staleIds.Contains(m.EquipmentItemID))
                .ToListAsync();
            db.MaintenanceRecords.RemoveRange(staleMaint);
            await db.SaveChangesAsync();
            // Unlink RentalTransactionItem.EquipmentItemID (SetNull handles this on delete,
            // but the existing rows need to detach cleanly — EF will set them to NULL).
            db.EquipmentItems.RemoveRange(stale);
            await db.SaveChangesAsync();
        }

        var equipment = await db.Equipment.FirstAsync(e => e.Name.StartsWith("Round Banquet Table"));
        var baselineStock = equipment.StockQuantity;
        Console.WriteLine($"Baseline Equipment: {equipment.Name}");
        Console.WriteLine($"Baseline StockQuantity: {baselineStock}");

        var itemRepo = new EquipmentItemRepository(db);
        var equipmentRepo = new EquipmentRepository(db);
        var categoryRepo = new CategoryRepository(db);
        var audit = new AuditLogger(db, new HttpContextAccessor());

        // Fake an Admin principal so service-layer RBAC allows write operations.
        var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] {
                new Claim(ClaimTypes.NameIdentifier, "verify-script"),
                new Claim(ClaimTypes.Name, "verify-script"),
                new Claim(ClaimTypes.Role, RoleNames.Admin),
            },
            authenticationType: "Test"));
        var httpContext = new DefaultHttpContext { User = adminPrincipal };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var currentUser = new CurrentUser(accessor);
        var service = new EquipmentItemService(itemRepo, equipmentRepo, currentUser, audit);

        int failed = 0;
        async Task Check(string label, Func<Task<bool>> assertion)
        {
            var ok = await assertion();
            Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} - {label}");
            if (!ok) failed++;
        }

        Console.WriteLine("\nTest 1: Create Available item increments StockQuantity");
        var newId = await service.CreateAsync(new EquipmentItemCreateDto
        {
            EquipmentID = equipment.EquipmentID,
            SerialNumber = "VERIFY-001",
            AvailabilityStatus = "Available",
            Location = "Test shelf",
        }, actorUserId: "verify-script");

        await Check("StockQuantity went from baseline to baseline+1", async () =>
        {
            await db.Entry(equipment).ReloadAsync();
            return equipment.StockQuantity == baselineStock + 1;
        });

        Console.WriteLine("\nTest 2: ChangeStatus to InMaintenance decrements StockQuantity");
        await service.ChangeStatusAsync(newId, "InMaintenance", actorUserId: "verify-script");
        await Check("StockQuantity back to baseline", async () =>
        {
            await db.Entry(equipment).ReloadAsync();
            return equipment.StockQuantity == baselineStock;
        });

        Console.WriteLine("\nTest 3: ChangeStatus back to Available increments again");
        await service.ChangeStatusAsync(newId, "Available", actorUserId: "verify-script");
        await Check("StockQuantity back to baseline+1", async () =>
        {
            await db.Entry(equipment).ReloadAsync();
            return equipment.StockQuantity == baselineStock + 1;
        });

        Console.WriteLine("\nTest 4: Delete Available item decrements StockQuantity");
        await service.DeleteAsync(newId, actorUserId: "verify-script");
        await Check("StockQuantity back to baseline", async () =>
        {
            await db.Entry(equipment).ReloadAsync();
            return equipment.StockQuantity == baselineStock;
        });

        Console.WriteLine("\nTest 5: Create Retired item does NOT change StockQuantity");
        var retiredId = await service.CreateAsync(new EquipmentItemCreateDto
        {
            EquipmentID = equipment.EquipmentID,
            SerialNumber = "VERIFY-002",
            AvailabilityStatus = "Retired",
            Location = "Discard pile",
        }, actorUserId: "verify-script");
        await Check("StockQuantity unchanged after creating Retired item", async () =>
        {
            await db.Entry(equipment).ReloadAsync();
            return equipment.StockQuantity == baselineStock;
        });
        await service.DeleteAsync(retiredId, actorUserId: "verify-script");

        Console.WriteLine("\nTest 6: Cannot delete a Category with Equipment (FK restrict)");
        var tablesCategory = await db.Categories.FirstAsync(c => c.Name == "Tables & Chairs");
        var canDeleteCategory = false;
        try
        {
            db.Categories.Remove(tablesCategory);
            await db.SaveChangesAsync();
            canDeleteCategory = true;
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is InvalidOperationException)
        {
            // expected - FK restrict surfaces as either a SQL FK violation
            // or as EF's "severed required relationship" guard.
        }
        await Check("Deleting a Category with Equipment is rejected", () => Task.FromResult(!canDeleteCategory));

        Console.WriteLine("\nTest 7: Audit log received entries from our operations");
        var auditCount = await db.AuditLogs.CountAsync(a => a.Action.StartsWith("EquipmentItem."));
        await Check($"At least 4 EquipmentItem audit entries present (got {auditCount})",
            () => Task.FromResult(auditCount >= 4));

        Console.WriteLine($"\n{(failed == 0 ? "ALL TESTS PASSED" : $"{failed} test(s) FAILED")}");
        return failed == 0 ? 0 : 1;
    }
}
