// Verification script: Batch 6 — Reports module.
// Run from repo root:
//   dotnet run --project Tools/VerifyBatch6.csproj

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Common.Constants;
using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Data;
using RentalSphere.Identity;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Reports.Services;
using System.Security.Claims;

namespace RentalSphere.Tools.VerifyBatch6;

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

        var audit = new AuditLogger(db, new HttpContextAccessor());

        var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "verify-batch6"),
            new Claim(ClaimTypes.Name, "verify-batch6"),
            new Claim(ClaimTypes.Role, RoleNames.Admin),
        }, authenticationType: "Test"));

        var customerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "verify-batch6-customer"),
            new Claim(ClaimTypes.Name, "verify-batch6-customer"),
            new Claim(ClaimTypes.Role, RoleNames.Customer),
        }, authenticationType: "Test"));

        var adminCurrent = new StubCurrentUser(adminPrincipal);
        var customerCurrent = new StubCurrentUser(customerPrincipal);

        var svc = new ReportsService(db, adminCurrent);
        var svcAsCustomer = new ReportsService(db, customerCurrent);

        var today = DateTime.UtcNow.Date;

        // ---- Test 1: Admin bundle loads ----
        var bundle = await svc.GetBundleAsync(today.AddDays(-30), today, "Daily");
        Assert("T1: admin gets non-null bundle", bundle is not null);
        Assert("T2: bundle Start/End populated", bundle.Start == today.AddDays(-30) && bundle.End == today);
        Assert("T3: Granularity echoes input", bundle.Granularity == "Daily");

        // ---- Test 4: Revenue list is a list (may be empty if no invoices in window) ----
        Assert("T4: revenue list is non-null", bundle.Revenue is not null);
        Assert("T5: utilization list is non-null", bundle.Utilization is not null);
        Assert("T6: outstanding list is non-null", bundle.OutstandingBalances is not null);
        Assert("T7: late returns list is non-null", bundle.LateReturns is not null);
        Assert("T8: damage trends list is non-null", bundle.DamageTrends is not null);

        // ---- Test 9: Granularity switches ----
        var weekly = await svc.GetBundleAsync(today.AddDays(-60), today, "Weekly");
        Assert("T9: Weekly granularity applied", weekly.Granularity == "Weekly");
        var monthly = await svc.GetBundleAsync(today.AddDays(-365), today, "Monthly");
        Assert("T10: Monthly granularity applied", monthly.Granularity == "Monthly");

        // ---- Test 11: Invalid range rejected ----
        var caught = false;
        try
        {
            await svc.GetBundleAsync(today, today, "Daily");
        }
        catch (InvalidOperationException) { caught = true; }
        Assert("T11: end <= start rejected", caught);

        // ---- Test 12: > 365 day window rejected ----
        caught = false;
        try
        {
            await svc.GetBundleAsync(today.AddDays(-400), today, "Daily");
        }
        catch (InvalidOperationException) { caught = true; }
        Assert("T12: > 365 day window rejected", caught);

        // ---- Test 13: Customer role forbidden ----
        caught = false;
        try
        {
            await svcAsCustomer.GetBundleAsync(today.AddDays(-30), today, "Daily");
        }
        catch (ForbiddenException) { caught = true; }
        Assert("T13: customer role forbidden from reports", caught);

        // ---- Test 14: Invalid granularity normalizes to Daily ----
        var fallback = await svc.GetBundleAsync(today.AddDays(-7), today, "Bogus");
        Assert("T14: invalid granularity normalizes to Daily", fallback.Granularity == "Daily");

        // ---- Test 15: Totals match sum of rows ----
        var reSum = bundle.Revenue.Sum(r => r.Revenue);
        var outSum = bundle.OutstandingBalances.Sum(o => o.Outstanding);
        Assert("T15: TotalRevenue matches Revenue.Sum",
            bundle.TotalRevenue == reSum);
        Assert("T16: TotalOutstanding matches Outstanding.Sum",
            bundle.TotalOutstanding == outSum);

        // ---- Test 17: OutstandingBalances contain only Unpaid/PartiallyPaid ----
        if (bundle.OutstandingBalances.Any())
        {
            var allOpen = bundle.OutstandingBalances.All(o => o.Status is "Unpaid" or "PartiallyPaid");
            Assert("T17: outstanding only Unpaid/PartiallyPaid", allOpen);
        }
        else
        {
            Assert("T17: outstanding only Unpaid/PartiallyPaid (vacuously true)", true);
        }

        // ---- Test 18: LateReturns with DaysLate >= 1 ----
        if (bundle.LateReturns.Any())
        {
            var allLate = bundle.LateReturns.All(l => l.DaysLate >= 1);
            Assert("T18: all late returns have DaysLate >= 1", allLate);
        }
        else
        {
            Assert("T18: late returns empty", true);
        }

        // ---- Test 19: Utilization percentages in [0, 100] ----
        if (bundle.Utilization.Any())
        {
            var allPct = bundle.Utilization.All(u => u.UtilizationPercent >= 0 && u.UtilizationPercent <= 100);
            Assert("T19: utilization percentages in [0, 100]", allPct);
        }
        else
        {
            Assert("T19: utilization empty", true);
        }

        // ---- Test 20: Damage trends totals match ----
        var dmgSum = bundle.DamageTrends.Sum(d => d.DamageAmount);
        Assert("T20: TotalDamageAmount matches trends sum",
            bundle.TotalDamageAmount == dmgSum);

        Console.WriteLine();
        Console.WriteLine($"=== Batch 6 verification: {_passed} passed, {_failed} failed ===");
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
