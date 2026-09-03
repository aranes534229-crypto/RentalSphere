using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Identity;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.Equipment.Models;

namespace RentalSphere.Data.Seed;

/// <summary>
/// Idempotent domain seed. Populates Categories, Equipment catalog, EquipmentItems,
/// Customers, Reservations, and a sample end-to-end rental transaction so the app has
/// data on a fresh install. Re-runs are safe — every block is guarded by an Any() check.
/// </summary>
public static class DomainSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, UserManager<ApplicationUser>? users = null)
    {
        if (!await db.Categories.AnyAsync())
        {
            db.Categories.AddRange(
                new Category { Name = "Tables & Chairs",  Description = "Seating and surface rentals." },
                new Category { Name = "Tents & Canopies", Description = "Outdoor shelter and shade structures." },
                new Category { Name = "Sound & Lighting", Description = "Audio systems, lighting rigs, and uplighting." },
                new Category { Name = "Catering Equipment", Description = "Chafing dishes, serving trays, beverage equipment." },
                new Category { Name = "Decor",             Description = "Backdrops, centerpieces, themed props." }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.Equipment.AnyAsync())
        {
            var tables     = await db.Categories.FirstAsync(c => c.Name == "Tables & Chairs");
            var tents      = await db.Categories.FirstAsync(c => c.Name == "Tents & Canopies");
            var sound      = await db.Categories.FirstAsync(c => c.Name == "Sound & Lighting");
            var catering   = await db.Categories.FirstAsync(c => c.Name == "Catering Equipment");
            var decor      = await db.Categories.FirstAsync(c => c.Name == "Decor");

            db.Equipment.AddRange(
                new EquipmentCatalog { Name = "Round Banquet Table (60in)", CategoryID = tables.CategoryID,  Description = "Seats 8. Wood-grain top.",         DailyRate = 5.00m,  StockQuantity = 50,  ImageURL = "/img/placeholders/table.jpg" },
                new EquipmentCatalog { Name = "Folding Chair",                CategoryID = tables.CategoryID,  Description = "White, commercial-grade.",        DailyRate = 1.50m,  StockQuantity = 200, ImageURL = "/img/placeholders/chair.jpg" },
                new EquipmentCatalog { Name = "10x10 ft Frame Tent",          CategoryID = tents.CategoryID,   Description = "White roof, no sidewalls.",       DailyRate = 50.00m, StockQuantity = 10,  ImageURL = "/img/placeholders/tent.jpg" },
                new EquipmentCatalog { Name = "PA Speaker Set",               CategoryID = sound.CategoryID,   Description = "Pair of 15in mains + stands.",   DailyRate = 80.00m, StockQuantity = 5,   ImageURL = "/img/placeholders/speaker.jpg" },
                new EquipmentCatalog { Name = "Stainless Chafing Dish",       CategoryID = catering.CategoryID,Description = "Includes fuel canisters.",        DailyRate = 10.00m, StockQuantity = 20,  ImageURL = "/img/placeholders/chafing.jpg" },
                new EquipmentCatalog { Name = "LED Uplight",                  CategoryID = decor.CategoryID,   Description = "RGB, wireless DMX.",              DailyRate = 3.00m,  StockQuantity = 100, ImageURL = "/img/placeholders/uplight.jpg" }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.EquipmentItems.AnyAsync())
        {
            var eqLookup = await db.Equipment.ToListAsync();
            var now = DateTime.UtcNow;
            foreach (var eq in eqLookup)
            {
                var units = Math.Min(eq.StockQuantity, 3); // keep seed pool small.
                for (var i = 1; i <= units; i++)
                {
                    db.EquipmentItems.Add(new EquipmentItem
                    {
                        EquipmentID = eq.EquipmentID,
                        SerialNumber = $"SEED-{eq.EquipmentID:000}-{i:000}",
                        AvailabilityStatus = AvailabilityStatus.Available,
                        Location = "Warehouse A",
                        LastStatusChange = now,
                    });
                }
            }
            await db.SaveChangesAsync();
        }

        if (!await db.Customers.AnyAsync(c => c.Email != null && c.Email.EndsWith("@example.test")))
        {
            var now = DateTime.UtcNow;

            // Link the demo "customer@rentalsphere.local" Identity user to a real Customer row
            // if that user exists and the link isn't already in place.
            ApplicationUser? demoCustomerUser = null;
            if (users is not null)
            {
                demoCustomerUser = await users.FindByEmailAsync("customer@rentalsphere.local");
            }

            var customers = new List<Customer>();

            // Add the example.test customers only if they don't already exist by email.
            async Task AddIfMissingAsync(string email, Func<Customer> factory)
            {
                if (!await db.Customers.AnyAsync(c => c.Email == email))
                {
                    customers.Add(factory());
                }
            }

            await AddIfMissingAsync("maria.santos@example.test", () => new Customer
            {
                FirstName = "Maria", LastName = "Santos",
                Email = "maria.santos@example.test",
                Phone = "+63 917 555 0101", City = "Manila",
                LoyaltyTier = LoyaltyTier.Gold,
                DateRegistered = now.AddMonths(-14), IsActive = true,
            });
            await AddIfMissingAsync("jose.reyes@example.test", () => new Customer
            {
                FirstName = "Jose", LastName = "Reyes",
                Email = "jose.reyes@example.test",
                Phone = "+63 918 555 0202", City = "Quezon City",
                LoyaltyTier = LoyaltyTier.Silver,
                DateRegistered = now.AddMonths(-6), IsActive = true,
            });
            await AddIfMissingAsync("ana.cruz@example.test", () => new Customer
            {
                FirstName = "Ana", LastName = "Cruz",
                Email = "ana.cruz@example.test",
                Phone = "+63 919 555 0303", City = "Makati",
                LoyaltyTier = LoyaltyTier.Platinum,
                DateRegistered = now.AddMonths(-22), IsActive = true,
            });

            // Demo Customer row linked to the seeded Customer identity user, only if not already present.
            if (demoCustomerUser is not null
                && !await db.Customers.AnyAsync(c => c.UserId == demoCustomerUser.Id))
            {
                customers.Add(new Customer
                {
                    FirstName = demoCustomerUser.FirstName,
                    LastName = demoCustomerUser.LastName,
                    Email = demoCustomerUser.Email!,
                    UserId = demoCustomerUser.Id,
                    Phone = "+63 920 555 0404",
                    City = "Pasig",
                    LoyaltyTier = LoyaltyTier.Bronze,
                    DateRegistered = now.AddMonths(-1),
                    IsActive = true,
                });
            }

            if (customers.Count > 0)
            {
                db.Customers.AddRange(customers);
                await db.SaveChangesAsync();
            }
        }

        // ----- CRM sample: one note + one follow-up for Maria Santos -----
        if (!await db.CustomerNotes.AnyAsync())
        {
            var maria = await db.Customers.FirstOrDefaultAsync(c => c.Email == "maria.santos@example.test");
            if (maria is not null && users is not null)
            {
                var staffUser = await users.FindByEmailAsync("staff@rentalsphere.local");
                db.CustomerNotes.Add(new RentalSphere.Modules.CustomerCrm.Models.CustomerNote
                {
                    CustomerID = maria.CustomerID,
                    Body = "Requested quote for wedding reception — 150 guests, prefers warm-white uplighting.",
                    IsFlagged = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-3),
                    CreatedByUserId = staffUser?.Id,
                });

                db.CustomerFollowUps.Add(new RentalSphere.Modules.CustomerCrm.Models.CustomerFollowUp
                {
                    CustomerID = maria.CustomerID,
                    Reason = "Send revised quote + venue walkthrough availability",
                    FollowUpDate = DateTime.UtcNow.Date.AddDays(2),
                    Status = RentalSphere.Modules.CustomerCrm.Models.FollowUpStatus.Pending,
                    AssignedToUserId = staffUser?.Id,
                    CreatedAt = DateTime.UtcNow.AddDays(-3),
                    CreatedByUserId = staffUser?.Id,
                });
                await db.SaveChangesAsync();
            }
        }
    }
}

