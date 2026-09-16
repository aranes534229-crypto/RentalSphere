using RentalSphere.Common.Exceptions;
using RentalSphere.Common.Services;
using RentalSphere.Identity.Services;
using RentalSphere.Modules.Billing.DTOs;
using RentalSphere.Modules.Billing.Models;
using RentalSphere.Modules.Billing.Repositories;
using RentalSphere.Modules.CustomerManagement.Repositories;
using RentalSphere.Modules.DamagePenalty.Models;
using RentalSphere.Modules.DamagePenalty.Repositories;
using RentalSphere.Modules.DamagePenalty.Services;
using RentalSphere.Modules.RentalTransaction.Models;
using RentalSphere.Modules.RentalTransaction.Repositories;
using DPEntity = RentalSphere.Modules.DamagePenalty.Models.DamagePenalty;
using RTEntity = RentalSphere.Modules.RentalTransaction.Models.RentalTransaction;

namespace RentalSphere.Modules.Billing.Services;

/// <summary>
/// Tunable knobs for invoice generation. Wired from appsettings.json in Production,
/// defaults applied elsewhere. Keeps BillingService IConfiguration-free for testability.
/// </summary>
public record BillingOptions(
    decimal LateFeePerDay = 100m,
    int PaymentTermsDays = 14);

public interface IBillingService
{
    Task<int> GenerateForReturnAsync(int rentalTransactionId, string actorUserId);
    Task<List<InvoiceListItemDto>> ListAsync(string? statusFilter = null, string? search = null, string? sort = null);
    Task<int> CountUnpaidOrPartialAsync();
    Task<(List<InvoiceListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? statusFilter, string? search, string? sort, int skip, int take);
    Task<InvoiceDetailsDto?> GetDetailsAsync(int id);
    Task<InvoiceDetailsDto?> GetByTransactionAsync(int rentalTransactionId);
    Task RecordPaymentAsync(PaymentCreateDto dto, string actorUserId);
    Task WaiveLineAsync(int invoiceLineId, decimal amount, string? reason, string actorUserId);
    Task VoidAsync(int invoiceId, string reason, string actorUserId);

    /// <summary>
    /// Sync a DamagePenalty's Amount to its linked InvoiceLine, recompute SubTotal/TotalAmount.
    /// Called by DamagePenaltyService after UpdateAmount.
    /// </summary>
    Task SyncDamageLineAsync(int damagePenaltyId, string actorUserId);
}

public class BillingService : IBillingService
{
    private readonly IBillingRepository _repo;
    private readonly IRentalTransactionRepository _rentalRepo;
    private readonly IDamagePenaltyRepository _damageRepo;
    private readonly ICustomerRepository _customerRepo;
    private readonly ICurrentUser _current;
    private readonly IAuditLogger _audit;
    private readonly decimal _lateFeePerDay;
    private readonly int _paymentTermsDays;

    public BillingService(
        IBillingRepository repo,
        IRentalTransactionRepository rentalRepo,
        IDamagePenaltyRepository damageRepo,
        ICustomerRepository customerRepo,
        ICurrentUser current,
        IAuditLogger audit,
        BillingOptions? options = null)
    {
        _repo = repo;
        _rentalRepo = rentalRepo;
        _damageRepo = damageRepo;
        _customerRepo = customerRepo;
        _current = current;
        _audit = audit;
        _lateFeePerDay = options?.LateFeePerDay ?? 100m;
        _paymentTermsDays = options?.PaymentTermsDays ?? 14;
    }

    public async Task<int> GenerateForReturnAsync(int rentalTransactionId, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can generate invoices.");

        var tx = await _rentalRepo.GetWithItemsAsync(rentalTransactionId)
            ?? throw new NotFoundException($"Rental transaction {rentalTransactionId} not found.");

        if (await _repo.GetByTransactionAsync(rentalTransactionId) is not null)
            throw new InvalidOperationException(
                $"Rental transaction {rentalTransactionId} already has an invoice.");

        if (tx.Status != RentalTransactionStatus.Returned)
            throw new InvalidOperationException(
                $"Invoices can only be generated for Returned transactions (current: {tx.Status}).");

        // Actual days the unit was out, rounded up. Late returns extend the rental
        // itself at the item's own DailyRate — no separate LateFee line.
        var endDate = tx.ReturnDate?.Date ?? tx.ExpectedReturnDate.Date;
        var rentalDays = Math.Max(1,
            (int)Math.Ceiling((endDate - tx.CheckoutDate.Date).TotalDays));

        var lines = new List<InvoiceLine>();

        foreach (var li in tx.Items)
        {
            var dailyRate = li.Equipment?.DailyRate ?? 0m;
            // Unit column shows the daily rate; LineTotal carries rate × days × qty.
            // Late returns just extend rentalDays — the line item still describes the
            // rental; the customer sees a longer period, not a penalty line.
            var lineTotal = dailyRate * rentalDays * li.Quantity;
            lines.Add(new InvoiceLine
            {
                LineType = InvoiceLineType.Rental,
                Description = li.Equipment?.Name ?? "Equipment",
                Quantity = li.Quantity,
                UnitAmount = dailyRate,
                LineTotal = lineTotal,
                ReferenceId = li.RentalTransactionItemID,
            });
        }

        // Damage hook: consume the pending DamagePenalty rows that
        // ReturnAsync already seeded (one per pinned unit) and reference
        // each on a zero-amount invoice line. Do NOT create new penalty
        // rows here — that would double-seed after the rental-return path
        // already attached them to the same DbContext. If no pending rows
        // exist for a flagged return, fall back to a single transaction-
        // level penalty so an invoice can still be generated for legacy
        // returns created before the return-path seed.
        if (tx.FlaggedForDamage)
        {
            var pending = await _damageRepo.ListByTransactionAsync(rentalTransactionId);
            var pendingForInvoice = pending.Where(p => p.Status == DamagePenaltyStatus.Pending).ToList();

            if (pendingForInvoice.Count == 0)
            {
                // Legacy fallback: no per-unit pending rows were seeded at
                // return time (e.g. old return predates this fix). Create a
                // single transaction-level penalty.
                var fallback = new DPEntity
                {
                    RentalTransactionID = tx.RentalTransactionID,
                    PenaltyType = DamagePenaltyType.Damage,
                    Description = "Damage flagged at return — assess amount to finalize.",
                    Amount = 0m,
                    Status = DamagePenaltyStatus.Pending,
                    ReportedByUserId = _current.UserId,
                };
                await _damageRepo.AddAsync(fallback);
                await _damageRepo.SaveChangesAsync();
                pendingForInvoice.Add(fallback);
            }

            foreach (var penalty in pendingForInvoice)
            {
                lines.Add(new InvoiceLine
                {
                    LineType = InvoiceLineType.DamagePenalty,
                    Description = "Damage assessment (pending)",
                    Quantity = 1,
                    UnitAmount = 0m,
                    LineTotal = 0m,
                    ReferenceId = penalty.DamagePenaltyID,
                });
            }
        }

        var subTotal = lines.Sum(l => l.LineTotal);
        var invoiceNumber = await GenerateInvoiceNumberAsync();

        var invoice = new Invoice
        {
            RentalTransactionID = tx.RentalTransactionID,
            CustomerID = tx.CustomerID,
            InvoiceNumber = invoiceNumber,
            InvoiceDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(_paymentTermsDays),
            Status = InvoiceStatus.Unpaid,
            SubTotal = subTotal,
            TotalAmount = subTotal,
            AmountPaid = 0m,
            CreatedByUserId = _current.UserId,
            Lines = lines,
        };

        await _repo.AddAsync(invoice);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Invoice.Generate", "Invoice", invoice.InvoiceID.ToString(),
            newValues: new
            {
                invoice.InvoiceNumber,
                tx.RentalTransactionID,
                tx.CustomerID,
                LineCount = lines.Count,
                invoice.SubTotal,
                invoice.TotalAmount,
                FlaggedForDamage = tx.FlaggedForDamage,
            });

        return invoice.InvoiceID;
    }

    public async Task<List<InvoiceListItemDto>> ListAsync(string? statusFilter = null, string? search = null, string? sort = null)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can list invoices.");

        var rows = await _repo.ListAsync(statusFilter, search, sort);
        return rows.Select(MapList).ToList();
    }

    public async Task<(List<InvoiceListItemDto> Items, int TotalCount)> ListPagedAsync(
        string? statusFilter, string? search, string? sort, int skip, int take)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can list invoices.");

        var all = await _repo.ListAsync(statusFilter, search, sort);
        var total = all.Count;
        var page = all.Skip(skip).Take(take).Select(MapList).ToList();
        return (page, total);
    }

    public async Task<int> CountUnpaidOrPartialAsync()
    {
        if (!_current.IsAdmin && !_current.IsStaff) return 0;
        return await _repo.CountUnpaidOrPartialAsync();
    }

    public async Task<InvoiceDetailsDto?> GetDetailsAsync(int id)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view invoices.");

        var inv = await _repo.GetWithLinesAsync(id)
            ?? throw new NotFoundException($"Invoice {id} not found.");
        return MapDetails(inv);
    }

    public async Task<InvoiceDetailsDto?> GetByTransactionAsync(int rentalTransactionId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can view invoices.");

        var inv = await _repo.GetByTransactionAsync(rentalTransactionId);
        return inv is null ? null : MapDetails(inv);
    }

    public async Task RecordPaymentAsync(PaymentCreateDto dto, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can record payments.");

        var inv = await _repo.GetWithLinesAsync(dto.InvoiceID)
            ?? throw new NotFoundException($"Invoice {dto.InvoiceID} not found.");

        if (inv.Status == InvoiceStatus.Voided)
            throw new InvalidOperationException("Cannot record payment on a voided invoice.");

        var balance = inv.TotalAmount - inv.AmountPaid;
        if (dto.Amount > balance)
            throw new InvalidOperationException(
                $"Payment amount ₱{dto.Amount:N2} exceeds remaining balance ₱{balance:N2}.");

        if (!Enum.TryParse<PaymentMethod>(dto.Method, out var method))
            throw new InvalidOperationException($"Unknown payment method '{dto.Method}'.");

        var payment = new Payment
        {
            InvoiceID = inv.InvoiceID,
            PaymentDate = dto.PaymentDate,
            Amount = dto.Amount,
            Method = method,
            ReferenceNumber = dto.ReferenceNumber,
            Notes = dto.Notes,
            RecordedByUserId = _current.UserId,
        };
        await _repo.AddPaymentAsync(payment);

        inv.AmountPaid += dto.Amount;
        inv.Status = inv.AmountPaid >= inv.TotalAmount
            ? InvoiceStatus.Paid
            : InvoiceStatus.PartiallyPaid;
        _repo.Update(inv);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Payment.Record", "Payment", payment.PaymentID.ToString(),
            newValues: new
            {
                payment.InvoiceID,
                payment.Amount,
                payment.Method,
                InvoiceStatus = inv.Status.ToString(),
            });

        // Refresh customer lifetime spend so the loyalty tier recomputes on the
        // next read. The manual override (if any) is preserved — we only touch
        // TotalSpent here. Tier is derived from TotalSpent via LoyaltyTierRules.
        // ponytail: append-only spend; if refunds are added later, recompute
        // from Payment.Amount where Invoice.Status != Voided instead of
        // incrementing here.
        var customer = await _customerRepo.GetAsync(inv.CustomerID)
            ?? throw new NotFoundException($"Customer {inv.CustomerID} not found.");
        customer.TotalSpent += dto.Amount;
        _customerRepo.Update(customer);
        await _customerRepo.SaveChangesAsync();
    }

    public async Task WaiveLineAsync(int invoiceLineId, decimal amount, string? reason, string actorUserId)
    {
        if (!_current.IsAdmin)
            throw new ForbiddenException("Only Admin can waive invoice lines.");

        var line = await _repo.GetLineAsync(invoiceLineId)
            ?? throw new NotFoundException($"Invoice line {invoiceLineId} not found.");

        var inv = await _repo.GetWithLinesAsync(line.InvoiceID)
            ?? throw new NotFoundException($"Invoice {line.InvoiceID} not found.");

        if (amount <= 0)
            throw new InvalidOperationException("Waive amount must be positive.");

        var newWaived = line.WaivedAmount + amount;
        if (newWaived > line.LineTotal)
            throw new InvalidOperationException(
                $"Cannot waive ₱{amount:N2}; line total is ₱{line.LineTotal:N2} with ₱{line.WaivedAmount:N2} already waived.");

        line.WaivedAmount = newWaived;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            line.Description = string.IsNullOrEmpty(line.Description)
                ? $"[Waived: {reason}]"
                : $"{line.Description} [Waived: {reason}]";
        }

        // Recompute totals.
        inv.SubTotal = inv.Lines.Sum(l => l.LineTotal);
        inv.TotalAmount = inv.Lines.Sum(l => l.LineTotal - l.WaivedAmount);

        if (inv.TotalAmount <= 0m)
        {
            inv.Status = InvoiceStatus.Paid;
        }
        else if (inv.AmountPaid >= inv.TotalAmount)
        {
            inv.Status = InvoiceStatus.Paid;
        }
        else if (inv.AmountPaid > 0)
        {
            inv.Status = InvoiceStatus.PartiallyPaid;
        }
        else
        {
            inv.Status = InvoiceStatus.Unpaid;
        }

        _repo.Update(inv);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Invoice.WaiveLine", "InvoiceLine", line.InvoiceLineID.ToString(),
            oldValues: new { WaivedAmount = line.WaivedAmount - amount },
            newValues: new { WaivedAmount = line.WaivedAmount, inv.TotalAmount, Reason = reason });
    }

    public async Task VoidAsync(int invoiceId, string reason, string actorUserId)
    {
        if (!_current.IsAdmin)
            throw new ForbiddenException("Only Admin can void invoices.");

        var inv = await _repo.GetWithLinesAsync(invoiceId)
            ?? throw new NotFoundException($"Invoice {invoiceId} not found.");

        if (inv.Status == InvoiceStatus.Voided)
            throw new InvalidOperationException("Invoice is already voided.");

        if (inv.AmountPaid > 0m)
            throw new InvalidOperationException(
                $"Cannot void invoice with ₱{inv.AmountPaid:N2} already paid. Refund first.");

        inv.Status = InvoiceStatus.Voided;
        inv.VoidedAt = DateTime.UtcNow;
        inv.VoidedByUserId = _current.UserId;
        inv.VoidedReason = reason;
        _repo.Update(inv);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Invoice.Void", "Invoice", invoiceId.ToString(),
            oldValues: new { Status = "Unpaid/PartiallyPaid" },
            newValues: new { Status = "Voided", Reason = reason });
    }

    public async Task SyncDamageLineAsync(int damagePenaltyId, string actorUserId)
    {
        if (!_current.IsAdmin && !_current.IsStaff)
            throw new ForbiddenException("Only Admin or Staff can sync damage assessments.");

        var penalty = await _damageRepo.GetAsync(damagePenaltyId)
            ?? throw new NotFoundException($"Damage record {damagePenaltyId} not found.");

        var inv = await _repo.GetByTransactionAsync(penalty.RentalTransactionID)
            ?? throw new NotFoundException(
                $"No invoice exists for transaction {penalty.RentalTransactionID}.");

        var line = inv.Lines.FirstOrDefault(l =>
            l.LineType == InvoiceLineType.DamagePenalty && l.ReferenceId == damagePenaltyId)
            ?? throw new NotFoundException(
                $"No linked invoice line for damage record {damagePenaltyId}.");

        var effectiveAmount = penalty.Amount - penalty.WaivedAmount;
        line.UnitAmount = effectiveAmount;
        line.LineTotal = effectiveAmount * line.Quantity;
        line.Description = string.IsNullOrEmpty(penalty.Description)
            ? "Damage charge"
            : penalty.Description;

        inv.SubTotal = inv.Lines.Sum(l => l.LineTotal);
        inv.TotalAmount = inv.Lines.Sum(l => l.LineTotal - l.WaivedAmount);

        if (penalty.Status == DamagePenaltyStatus.Waived || effectiveAmount <= 0m)
        {
            // Linked penalty waived — keep line at zero but don't flip invoice status yet.
        }

        inv.Status = inv.AmountPaid >= inv.TotalAmount && inv.TotalAmount > 0
            ? InvoiceStatus.Paid
            : inv.AmountPaid > 0
                ? InvoiceStatus.PartiallyPaid
                : InvoiceStatus.Unpaid;

        _repo.Update(inv);
        await _repo.SaveChangesAsync();

        await _audit.LogAsync(
            actorUserId, "Invoice.SyncDamageLine", "Invoice", inv.InvoiceID.ToString(),
            oldValues: new { UnitAmount = line.UnitAmount },
            newValues: new { UnitAmount = effectiveAmount, inv.TotalAmount });
    }

    // ---- helpers ----

    private async Task<string> GenerateInvoiceNumberAsync()
    {
        var now = DateTime.UtcNow;
        var count = await _repo.CountByMonthAsync(now.Year, now.Month) + 1;
        return $"INV-{now:yyyyMM}-{count:0000}";
    }

    private static InvoiceListItemDto MapList(Invoice i) => new()
    {
        InvoiceID = i.InvoiceID,
        InvoiceNumber = i.InvoiceNumber,
        RentalTransactionID = i.RentalTransactionID,
        CustomerID = i.CustomerID,
        CustomerName = i.Customer is null ? string.Empty : $"{i.Customer.FirstName} {i.Customer.LastName}",
        InvoiceDate = i.InvoiceDate,
        DueDate = i.DueDate,
        TotalAmount = i.TotalAmount,
        AmountPaid = i.AmountPaid,
        Status = i.Status.ToString(),
    };

    private static InvoiceDetailsDto MapDetails(Invoice i) => new()
    {
        InvoiceID = i.InvoiceID,
        InvoiceNumber = i.InvoiceNumber,
        RentalTransactionID = i.RentalTransactionID,
        CustomerID = i.CustomerID,
        CustomerName = i.Customer is null ? string.Empty : $"{i.Customer.FirstName} {i.Customer.LastName}",
        CustomerEmail = i.Customer?.Email ?? string.Empty,
        InvoiceDate = i.InvoiceDate,
        DueDate = i.DueDate,
        SubTotal = i.SubTotal,
        TotalAmount = i.TotalAmount,
        AmountPaid = i.AmountPaid,
        Status = i.Status.ToString(),
        Notes = i.Notes,
        VoidedAt = i.VoidedAt,
        VoidedByUserName = i.VoidedByUser is null ? null : $"{i.VoidedByUser.FirstName} {i.VoidedByUser.LastName}",
        VoidedReason = i.VoidedReason,
        CreatedByUserName = i.CreatedByUser is null ? null : $"{i.CreatedByUser.FirstName} {i.CreatedByUser.LastName}",
        Lines = (i.Lines ?? new List<InvoiceLine>()).Select(l => new InvoiceLineDto
        {
            InvoiceLineID = l.InvoiceLineID,
            LineType = l.LineType,
            Description = l.Description,
            Quantity = l.Quantity,
            UnitAmount = l.UnitAmount,
            LineTotal = l.LineTotal,
            WaivedAmount = l.WaivedAmount,
            ReferenceId = l.ReferenceId,
        }).ToList(),
        Payments = (i.Payments ?? new List<Payment>()).OrderBy(p => p.PaymentDate).Select(p => new PaymentDto
        {
            PaymentID = p.PaymentID,
            PaymentDate = p.PaymentDate,
            Amount = p.Amount,
            Method = p.Method.ToString(),
            ReferenceNumber = p.ReferenceNumber,
            Notes = p.Notes,
            RecordedByUserName = p.RecordedByUser is null ? null : $"{p.RecordedByUser.FirstName} {p.RecordedByUser.LastName}",
        }).ToList(),
    };
}
