// Verification script: exercises Batch 4 modules — Billing (invoice generation,
// payment, waive, void) and Damage & Penalty (create, sync to invoice, waive).
// Run from repo root:
//   dotnet run --project Tools/VerifyBatch4.csproj

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Identity;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Billing.DTOs;
using RentalSphere.Modules.Billing.Models;
using RentalSphere.Modules.Billing.Repositories;
using RentalSphere.Modules.Billing.Services;
using RentalSphere.Modules.CustomerManagement.DTOs;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.CustomerManagement.Repositories;
using RentalSphere.Modules.CustomerManagement.Services;
using RentalSphere.Modules.DamagePenalty.DTOs;
using RentalSphere.Modules.DamagePenalty.Models;
using RentalSphere.Modules.DamagePenalty.Repositories;
using RentalSphere.Modules.DamagePenalty.Services;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Equipment.Repositories;
using RentalSphere.Modules.Equipment.Services;
using RentalSphere.Modules.RentalTransaction.DTOs;
using RentalSphere.Modules.RentalTransaction.Models;
using RentalSphere.Modules.RentalTransaction.Repositories;
using RentalSphere.Modules.RentalTransaction.Services;
using RentalSphere.Modules.ReservationManagement.DTOs;
using RentalSphere.Modules.ReservationManagement.Models;
using RentalSphere.Modules.ReservationManagement.Repositories;
using RentalSphere.Modules.ReservationManagement.Services;
using System.Security.Claims;

namespace RentalSphere.Tools.VerifyBatch4;

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

        var config = new BillingOptions(LateFeePerDay: 100m, PaymentTermsDays: 14);

        var dbOpts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var db = new ApplicationDbContext(dbOpts);

        // Idempotent cleanup of stale verification data.
        var stale = await db.Customers
            .Where(c => c.Email.StartsWith("verify-batch4-"))
            .ToListAsync();
        if (stale.Any())
        {
            var staleIds = stale.Select(c => c.CustomerID).ToList();

            // Wipe payments + invoice lines + invoices first (Restrict FKs), then damage,
            // then rentals, then reservations, then customers.
            var staleRentals = await db.RentalTransactions
                .Where(t => staleIds.Contains(t.CustomerID))
                .ToListAsync();
            var staleRentalIds = staleRentals.Select(r => r.RentalTransactionID).ToList();

            var staleInvoices = await db.Invoices
                .Where(i => staleRentalIds.Contains(i.RentalTransactionID))
                .ToListAsync();
            var staleInvoiceIds = staleInvoices.Select(i => i.InvoiceID).ToList();
            var stalePayments = await db.Payments.Where(p => staleInvoiceIds.Contains(p.InvoiceID)).ToListAsync();
            var staleInvoiceLines = await db.InvoiceLines.Where(l => staleInvoiceIds.Contains(l.InvoiceID)).ToListAsync();
            db.Payments.RemoveRange(stalePayments);
            db.InvoiceLines.RemoveRange(staleInvoiceLines);
            db.Invoices.RemoveRange(staleInvoices);

            var staleDamage = await db.DamagePenalties
                .Where(d => staleRentalIds.Contains(d.RentalTransactionID))
                .ToListAsync();
            db.DamagePenalties.RemoveRange(staleDamage);

            var staleRentalItems = await db.RentalTransactionItems
                .Where(i => staleRentalIds.Contains(i.RentalTransactionID))
                .ToListAsync();
            db.RentalTransactionItems.RemoveRange(staleRentalItems);
            db.RentalTransactions.RemoveRange(staleRentals);

            var staleReservations = await db.Reservations
                .Where(r => staleIds.Contains(r.CustomerID))
                .ToListAsync();
            var staleResIds = staleReservations.Select(r => r.ReservationID).ToList();
            var staleResItems = await db.ReservationItems
                .Where(i => staleResIds.Contains(i.ReservationID))
                .ToListAsync();
            db.ReservationItems.RemoveRange(staleResItems);
            db.Reservations.RemoveRange(staleReservations);

            db.Customers.RemoveRange(stale);
            await db.SaveChangesAsync();
        }

        // Reset equipment-items pool for VerifyBatch4 — fresh pool, no leakage from Batch 3.
        var existingVerifyItems = await db.EquipmentItems
            .Where(i => i.SerialNumber.StartsWith("VERIFY-BATCH4-"))
            .ToListAsync();
        if (existingVerifyItems.Any())
        {
            var itemIds = existingVerifyItems.Select(i => i.ItemID).ToList();
            var staleMaint = await db.MaintenanceRecords
                .Where(m => itemIds.Contains(m.EquipmentItemID))
                .ToListAsync();
            db.MaintenanceRecords.RemoveRange(staleMaint);
            db.EquipmentItems.RemoveRange(existingVerifyItems);
            await db.SaveChangesAsync();
        }

        var equipment = await db.Equipment.FirstAsync(e => e.Name.StartsWith("Round Banquet Table"));
        var confirmEquipment = equipment;

        for (int n = 1; n <= 10; n++)
        {
            db.EquipmentItems.Add(new EquipmentItem
            {
                EquipmentID = confirmEquipment.EquipmentID,
                SerialNumber = $"VERIFY-BATCH4-TBL-{n:000}",
                AvailabilityStatus = AvailabilityStatus.Available,
                Location = "Verification pool",
                LastStatusChange = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        // Seed admin Identity user.
        if (await db.Users.FirstOrDefaultAsync(u => u.Id == "verify-batch4") is null)
        {
            db.Users.Add(new ApplicationUser
            {
                Id = "verify-batch4",
                UserName = "verify-batch4@test.local",
                Email = "verify-batch4@test.local",
                FirstName = "Verify",
                LastName = "Batch4",
                IsActive = true,
                SecurityStamp = "verify-batch4",
                ConcurrencyStamp = "verify-batch4",
            });
            await db.SaveChangesAsync();
        }

        // Repos + services.
        var customerRepo = new CustomerRepository(db);
        var equipmentRepo = new EquipmentRepository(db);
        var equipmentItemRepo = new EquipmentItemRepository(db);
        var reservationRepo = new ReservationRepository(db);
        var rentalRepo = new RentalTransactionRepository(db);
        var billingRepo = new BillingRepository(db);
        var damageRepo = new DamagePenaltyRepository(db);
        var audit = new AuditLogger(db, new HttpContextAccessor());

        var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "verify-batch4"),
                new Claim(ClaimTypes.Name, "verify-batch4"),
                new Claim(ClaimTypes.Role, RoleNames.Admin),
            }, authenticationType: "Test"));
        var staffPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "verify-batch4-staff"),
                new Claim(ClaimTypes.Name, "verify-batch4-staff"),
                new Claim(ClaimTypes.Role, RoleNames.Staff),
            }, authenticationType: "Test"));

        var adminCurrent = new StubCurrentUser(adminPrincipal);
        var staffCurrent = new StubCurrentUser(staffPrincipal);

        var customerSvc = new CustomerService(customerRepo, db, adminCurrent, audit);
        var equipmentItemSvc = new EquipmentItemService(equipmentItemRepo, equipmentRepo, adminCurrent, audit);
        var reservationSvc = new ReservationService(
            reservationRepo, customerRepo, equipmentRepo, equipmentItemRepo,
            equipmentItemSvc, db, adminCurrent, audit);

        var billingSvc = new BillingService(
            billingRepo, rentalRepo, damageRepo, customerRepo, adminCurrent, audit, options: config);
        var damageSvc = new DamagePenaltyService(damageRepo, billingSvc, adminCurrent, audit);

        var rentalSvc = new RentalTransactionService(
            rentalRepo, reservationRepo, equipmentItemRepo, equipmentItemSvc,
            customerRepo, billingSvc, adminCurrent, audit);

        int failed = 0;
        async Task Check(string label, Func<Task<bool>> assertion)
        {
            var ok = await assertion();
            Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} - {label}");
            if (!ok) failed++;
        }

        // Seed a customer to attach rentals to.
        var customerId = await customerSvc.CreateAsync(new CustomerCreateDto
        {
            FirstName = "Lise",
            LastName = "Meitner",
            Email = "verify-batch4-lise@example.local",
            Phone = "555-0104",
            City = "Pasig",
        }, actorUserId: "verify-batch4");
        await customerSvc.OverrideTierAsync(customerId, LoyaltyTier.Gold, "verify seed", actorUserId: "verify-batch4");

        async Task<int> CreateAndCheckoutRentalAsync(int days, bool flagDamage = false)
        {
            var start = DateTime.UtcNow.Date.AddDays(100);
            var end = start.AddDays(days);
            var resId = await reservationSvc.CreateAsync(new ReservationCreateDto
            {
                CustomerID = customerId,
                RentalStartDate = start,
                RentalEndDate = end,
                Items = new List<ReservationItemInputDto>
                {
                    new() { EquipmentID = equipment.EquipmentID, Quantity = 2 },
                },
            }, actorUserId: "verify-batch4");
            await reservationSvc.ConfirmAsync(resId, actorUserId: "verify-batch4");
            var txId = await rentalSvc.CheckoutAsync(resId, "verify-batch4");
            await rentalSvc.ReturnAsync(new RentalTransactionReturnDto
            {
                RentalTransactionID = txId,
                ConditionNotes = flagDamage ? "Visible damage to top." : "Returned clean.",
                FlaggedForDamage = flagDamage,
            }, actorUserId: "verify-batch4");
            return txId;
        }

        // ---------- Test 1: Return generates invoice with Rental lines ----------
        Console.WriteLine("\nTest 1: Return auto-generates invoice with Rental lines");
        var tx1 = await CreateAndCheckoutRentalAsync(days: 2);
        var invoice1 = await db.Invoices
            .Include(i => i.Lines)
            .FirstAsync(i => i.RentalTransactionID == tx1);
        await Check("Invoice exists for transaction", () =>
            Task.FromResult(invoice1.InvoiceID > 0));
        await Check("Invoice has 2 Rental lines (one per pinned unit)", async () =>
        {
            var rentalLines = invoice1.Lines.Where(l => l.LineType == InvoiceLineType.Rental).ToList();
            return rentalLines.Count == 2
                && rentalLines.All(l => l.LineTotal > 0);
        });
        await Check("Subtotal equals sum of LineTotals", async () =>
        {
            var sum = invoice1.Lines.Sum(l => l.LineTotal);
            return invoice1.SubTotal == sum && invoice1.TotalAmount == sum && invoice1.Status == InvoiceStatus.Unpaid;
        });
        await Check("InvoiceNumber has INV-YYYYMM-#### shape", () =>
            Task.FromResult(System.Text.RegularExpressions.Regex.IsMatch(
                invoice1.InvoiceNumber, @"^INV-\d{6}-\d{4,}$")));

        // ---------- Test 2: Late fee line added when Returned past ExpectedReturnDate ----------
        Console.WriteLine("\nTest 2: Late-return adds LateFee line");
        var tx2 = await CreateAndCheckoutRentalAsync(days: 2);
        // Backdate ExpectedReturnDate so ReturnDate is "late".
        var tx2Row = await db.RentalTransactions.FindAsync(tx2);
        var lateBy = 3;
        tx2Row!.ExpectedReturnDate = DateTime.UtcNow.AddDays(-lateBy);
        db.RentalTransactions.Update(tx2Row);
        await db.SaveChangesAsync();
        // Idempotent regeneration: delete any existing invoice first.
        var existing2 = await db.Invoices.FirstOrDefaultAsync(i => i.RentalTransactionID == tx2);
        if (existing2 is not null)
        {
            var itsLines = await db.InvoiceLines.Where(l => l.InvoiceID == existing2.InvoiceID).ToListAsync();
            db.InvoiceLines.RemoveRange(itsLines);
            db.Invoices.Remove(existing2);
            await db.SaveChangesAsync();
        }
        await billingSvc.GenerateForReturnAsync(tx2, "verify-batch4");
        var invoice2 = await db.Invoices.Include(i => i.Lines)
            .FirstAsync(i => i.RentalTransactionID == tx2);
        await Check("LateFee line present, days × LateFeePerDay", () =>
        {
            var late = invoice2.Lines.FirstOrDefault(l => l.LineType == InvoiceLineType.LateFee);
            return Task.FromResult(late is not null && late.LineTotal == lateBy * 100m);
        });

        // ---------- Test 3: FlaggedForDamage seeds a DamagePenalty + zero line ----------
        Console.WriteLine("\nTest 3: FlaggedForDamage seeds DamagePenalty + zero invoice line");
        var tx3 = await CreateAndCheckoutRentalAsync(days: 2, flagDamage: true);
        var invoice3 = await db.Invoices.Include(i => i.Lines)
            .FirstAsync(i => i.RentalTransactionID == tx3);
        await Check("DamagePenalty row created in Pending with Amount=0", async () =>
        {
            var dp = await db.DamagePenalties
                .FirstOrDefaultAsync(p => p.RentalTransactionID == tx3);
            return dp is not null && dp.Status == DamagePenaltyStatus.Pending && dp.Amount == 0m;
        });
        await Check("Invoice has DamagePenalty line referencing the penalty", () =>
        {
            var damageLine = invoice3.Lines.FirstOrDefault(l => l.LineType == InvoiceLineType.DamagePenalty);
            return Task.FromResult(damageLine is not null
                && damageLine.UnitAmount == 0m
                && damageLine.LineTotal == 0m
                && damageLine.ReferenceId.HasValue);
        });

        // ---------- Test 4: UpdateAmount on DamagePenalty syncs InvoiceLine + recomputes ----------
        Console.WriteLine("\nTest 4: UpdateAmount syncs to InvoiceLine");
        var penalty4 = await db.DamagePenalties.FirstAsync(p => p.RentalTransactionID == tx3);
        await damageSvc.UpdateAmountAsync(penalty4.DamagePenaltyID, 750m, "verify-batch4");
        var syncedLine = await db.InvoiceLines
            .FirstAsync(l => l.InvoiceID == invoice3.InvoiceID
                          && l.LineType == InvoiceLineType.DamagePenalty);
        await Check("InvoiceLine UnitAmount reflects new penalty amount", () =>
            Task.FromResult(syncedLine.UnitAmount == 750m && syncedLine.LineTotal == 750m));
        var invoice3After = await db.Invoices.Include(i => i.Lines).FirstAsync(i => i.InvoiceID == invoice3.InvoiceID);
        await Check("Invoice TotalAmount recomputed (now includes ₱750)", () =>
            Task.FromResult(invoice3After.TotalAmount == invoice3After.Lines.Sum(l => l.LineTotal)));

        // ---------- Test 5: RecordPayment rejects amount > balance ----------
        Console.WriteLine("\nTest 5: RecordPayment rejects amount over balance");
        var rejected = false;
        try
        {
            await billingSvc.RecordPaymentAsync(new PaymentCreateDto
            {
                InvoiceID = invoice1.InvoiceID,
                Amount = invoice1.TotalAmount + 100m,
                PaymentDate = DateTime.UtcNow,
                Method = "Cash",
            }, "verify-batch4");
        }
        catch (InvalidOperationException) { rejected = true; }
        await Check("Over-balance payment rejected", () => Task.FromResult(rejected));

        // ---------- Test 6: RecordPayment flips status to Paid ----------
        Console.WriteLine("\nTest 6: RecordPayment flips status to Paid");
        await billingSvc.RecordPaymentAsync(new PaymentCreateDto
        {
            InvoiceID = invoice1.InvoiceID,
            Amount = invoice1.TotalAmount,
            PaymentDate = DateTime.UtcNow,
            Method = "Cash",
            ReferenceNumber = "TEST-PAY-001",
        }, "verify-batch4");
        var invoice1After = await db.Invoices.FindAsync(invoice1.InvoiceID);
        await Check("Status = Paid after full payment", () =>
            Task.FromResult(invoice1After!.Status == InvoiceStatus.Paid && invoice1After.AmountPaid == invoice1.TotalAmount));

        // ---------- Test 7: RBAC — Staff cannot WaiveLine ----------
        Console.WriteLine("\nTest 7: Staff WaiveLine blocked");
        var staffBillingSvc = new BillingService(
            billingRepo, rentalRepo, damageRepo, customerRepo, staffCurrent, audit, options: config);
        var staffBlocked = false;
        try
        {
            await staffBillingSvc.WaiveLineAsync(syncedLine.InvoiceLineID, 50m, null, "verify-batch4-staff");
        }
        catch (ForbiddenException) { staffBlocked = true; }
        await Check("Staff WaiveLine throws ForbiddenException", () => Task.FromResult(staffBlocked));

        // ---------- Test 8: Admin WaiveLine reduces TotalAmount ----------
        Console.WriteLine("\nTest 8: Admin WaiveLine reduces TotalAmount");
        var beforeWaive = (await db.Invoices.FindAsync(invoice3After.InvoiceID))!.TotalAmount;
        await billingSvc.WaiveLineAsync(syncedLine.InvoiceLineID, 200m, "Customer goodwill", "verify-batch4");
        var afterWaive = await db.Invoices.FindAsync(invoice3After.InvoiceID);
        await Check("TotalAmount decreased by waive amount", () =>
            Task.FromResult(afterWaive!.TotalAmount == beforeWaive - 200m));
        var reloaded = await db.InvoiceLines.FindAsync(syncedLine.InvoiceLineID);
        await Check("Line WaivedAmount updated", () =>
            Task.FromResult(reloaded!.WaivedAmount == 200m));

        // ---------- Test 9: Void rejected when AmountPaid > 0 ----------
        Console.WriteLine("\nTest 9: Void rejected when AmountPaid > 0");
        var voidBlocked = false;
        try
        {
            await billingSvc.VoidAsync(invoice1.InvoiceID, "test", "verify-batch4");
        }
        catch (InvalidOperationException) { voidBlocked = true; }
        await Check("Void with payments rejected", () => Task.FromResult(voidBlocked));

        // ---------- Test 10: Void on unpaid invoice sets Voided ----------
        Console.WriteLine("\nTest 10: Admin Void on unpaid invoice flips status");
        var unpaidInvoiceId = invoice2.InvoiceID;
        await billingSvc.VoidAsync(unpaidInvoiceId, "Created in error", "verify-batch4");
        var voided = await db.Invoices.FindAsync(unpaidInvoiceId);
        await Check("Status = Voided", () => Task.FromResult(voided!.Status == InvoiceStatus.Voided));
        await Check("VoidedReason and VoidedAt set", () =>
            Task.FromResult(voided.VoidedAt.HasValue
                && voided.VoidedReason == "Created in error"));

        // ---------- Test 11: Customer-scope: customer blocked from billing service ----------
        Console.WriteLine("\nTest 11: Customer role blocked at service layer");
        var customerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "verify-batch4-cust"),
                new Claim(ClaimTypes.Role, RoleNames.Customer),
            }, authenticationType: "Test"));
        var custCurrent = new StubCurrentUser(customerPrincipal);
        var custBillingSvc = new BillingService(
            billingRepo, rentalRepo, damageRepo, customerRepo, custCurrent, audit, options: config);
        var custBlocked = false;
        try
        {
            await custBillingSvc.ListAsync();
        }
        catch (ForbiddenException) { custBlocked = true; }
        await Check("Customer cannot list invoices", () => Task.FromResult(custBlocked));

        // ---------- Test 12: Audit log entries for Batch 4 actions ----------
        Console.WriteLine("\nTest 12: Audit log captures Batch 4 actions");
        var actions = await db.AuditLogs
            .Where(a => a.ActorUserId == "verify-batch4"
                     && (a.Action == "Invoice.Generate"
                      || a.Action == "Payment.Record"
                      || a.Action == "Invoice.WaiveLine"
                      || a.Action == "Invoice.Void"))
            .Select(a => a.Action)
            .Distinct()
            .ToListAsync();
        await Check("Audit captured Invoice.Generate", () => Task.FromResult(actions.Contains("Invoice.Generate")));
        await Check("Audit captured Payment.Record", () => Task.FromResult(actions.Contains("Payment.Record")));
        await Check("Audit captured Invoice.WaiveLine", () => Task.FromResult(actions.Contains("Invoice.WaiveLine")));
        await Check("Audit captured Invoice.Void", () => Task.FromResult(actions.Contains("Invoice.Void")));

        Console.WriteLine();
        if (failed == 0)
        {
            Console.WriteLine("ALL TESTS PASSED");
            return 0;
        }
        Console.WriteLine($"{failed} TEST(S) FAILED");
        return 1;
    }
}
