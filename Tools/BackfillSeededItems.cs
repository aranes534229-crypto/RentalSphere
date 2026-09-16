// One-off: clears the old under-stocked EquipmentItem seed and re-creates rows so
// the items list matches each catalog row's StockQuantity. Run once after the
// DomainSeeder change that dropped the Math.Min(..., 3) cap.
//
//   dotnet run --project Tools/BackfillSeededItems.csproj

using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.Equipment.Models;

namespace RentalSphere.Tools.BackfillSeededItems;

public static class Program
{
    public static async Task<int> Main()
    {
        // SQLite at the repo root, matching appsettings.json.
        var connectionString = "Data Source=rentalsphere.db";
        var dbOpts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using var db = new ApplicationDbContext(dbOpts);

        // Only the rows the original seeder created (SEED- prefix). Real rentals use
        // EQ- / TBL- / CHR- etc., so this never touches customer data.
        var seeded = await db.EquipmentItems
            .Where(i => i.SerialNumber.StartsWith("SEED-"))
            .ToListAsync();
        Console.WriteLine($"Removing {seeded.Count} seeded EquipmentItem rows.");

        // Unlink any FK references that would block the delete.
        var ids = seeded.Select(i => i.ItemID).ToList();
        if (ids.Count > 0)
        {
            var maint = await db.MaintenanceRecords
                .Where(m => ids.Contains(m.EquipmentItemID))
                .ToListAsync();
            db.MaintenanceRecords.RemoveRange(maint);
            await db.SaveChangesAsync();

            db.EquipmentItems.RemoveRange(seeded);
            await db.SaveChangesAsync();
        }

        // Re-run the seed block by hand: one EquipmentItem row per StockQuantity unit.
        var equipment = await db.Equipment.OrderBy(e => e.EquipmentID).ToListAsync();
        var now = DateTime.UtcNow;
        var added = 0;
        foreach (var eq in equipment)
        {
            var existing = await db.EquipmentItems
                .Where(i => i.EquipmentID == eq.EquipmentID)
                .CountAsync();
            for (var i = existing + 1; i <= eq.StockQuantity; i++)
            {
                db.EquipmentItems.Add(new EquipmentItem
                {
                    EquipmentID = eq.EquipmentID,
                    SerialNumber = $"SEED-{eq.EquipmentID:000}-{i:000}",
                    AvailabilityStatus = AvailabilityStatus.Available,
                    Location = "Warehouse A",
                    LastStatusChange = now,
                });
                added++;
            }
        }
        if (added > 0) await db.SaveChangesAsync();
        Console.WriteLine($"Added {added} new EquipmentItem rows. EquipmentItem count: {await db.EquipmentItems.CountAsync()}");
        return 0;
    }
}
