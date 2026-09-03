using Microsoft.EntityFrameworkCore;
using RentalSphere.Data;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.ReservationManagement.Models;

namespace RentalSphere.Modules.ReservationManagement.BackgroundServices;

public class ReservationExpiryOptions
{
    public double PendingExpiryHours { get; set; } = 0.5;
    public int ConfirmedExpiryDays { get; set; } = 1;
    public int ScanIntervalMinutes { get; set; } = 5;
}

/// <summary>
/// Periodically promotes stale reservations to Expired:
///   - Pending reservations older than PendingExpiryHours.
///   - Confirmed reservations whose RentalStartDate is more than
///     ConfirmedExpiryDays in the past (i.e. customer never showed up
///     and staff never checked out the unit).
/// Confirmed-row expiry also releases any pinned EquipmentItems back
/// to Available so they rejoin the rental pool.
/// </summary>
public class ReservationExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationExpiryWorker> _logger;
    private readonly ReservationExpiryOptions _opts;

    public ReservationExpiryWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ReservationExpiryWorker> logger,
        ReservationExpiryOptions opts)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _opts = opts;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First run after a short delay so the app finishes startup.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReservationExpiryWorker scan failed; will retry next interval.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_opts.ScanIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;
        var pendingCutoff = now.AddHours(-_opts.PendingExpiryHours);
        var confirmedCutoff = now.Date.AddDays(-_opts.ConfirmedExpiryDays);

        // 1. Stale Pending.
        var stalePending = await db.Reservations
            .Where(r => r.Status == ReservationStatus.Pending
                     && r.ReservationDate < pendingCutoff)
            .ToListAsync(ct);

        foreach (var r in stalePending) r.Status = ReservationStatus.Expired;

        // 2. Stale Confirmed (rental start has passed without checkout).
        var staleConfirmed = await db.Reservations
            .Include(r => r.Items)
            .Where(r => r.Status == ReservationStatus.Confirmed
                     && r.RentalStartDate < confirmedCutoff)
            .ToListAsync(ct);

        var releasedEquipmentIds = new List<int>();
        foreach (var r in staleConfirmed)
        {
            r.Status = ReservationStatus.Expired;
            releasedEquipmentIds.AddRange(r.Items.Select(i => i.EquipmentID));
        }

        if (stalePending.Any() || staleConfirmed.Any())
        {
            await db.SaveChangesAsync(ct);
        }

        // Release any Reserved items the Confirm step pinned — they go back to Available.
        if (releasedEquipmentIds.Any())
        {
            var distinctIds = releasedEquipmentIds.Distinct().ToList();
            var pinned = await db.EquipmentItems
                .Where(i => distinctIds.Contains(i.EquipmentID)
                         && i.AvailabilityStatus == AvailabilityStatus.Reserved)
                .ToListAsync(ct);

            foreach (var item in pinned)
            {
                item.AvailabilityStatus = AvailabilityStatus.Available;
                item.LastStatusChange = now;
            }

            if (pinned.Any())
            {
                await db.SaveChangesAsync(ct);
            }
        }

        if (stalePending.Any() || staleConfirmed.Any())
        {
            _logger.LogInformation(
                "ReservationExpiryWorker expired {Pending} pending and {Confirmed} confirmed reservations.",
                stalePending.Count, staleConfirmed.Count);
        }
    }
}
