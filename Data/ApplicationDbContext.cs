using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RentalSphere.Identity;
using RentalSphere.Identity.Models;
using RentalSphere.Modules.Billing.Models;
using RentalSphere.Modules.CustomerCrm.Models;
using RentalSphere.Modules.CustomerManagement.Models;
using RentalSphere.Modules.DamagePenalty.Models;
using RentalSphere.Modules.Equipment.Models;
using RentalSphere.Modules.Maintenance.Models;
using RentalSphere.Modules.RentalTransaction.Models;
using RentalSphere.Modules.ReservationManagement.Models;

namespace RentalSphere.Data;

/// <summary>
/// EF Core database context. Extends IdentityDbContext so the AspNet* identity tables
/// (Users, Roles, Claims, etc.) are created automatically. Domain entities from each
/// module are added here as that module ships.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Identity / audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Equipment Management
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<EquipmentCatalog> Equipment => Set<EquipmentCatalog>();
    public DbSet<EquipmentItem> EquipmentItems => Set<EquipmentItem>();

    // Customer Management
    public DbSet<Customer> Customers => Set<Customer>();

    // Reservation Management
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationItem> ReservationItems => Set<ReservationItem>();

    // Rental Transaction Management
    public DbSet<RentalTransaction> RentalTransactions => Set<RentalTransaction>();
    public DbSet<RentalTransactionItem> RentalTransactionItems => Set<RentalTransactionItem>();

    // Maintenance Management
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();

    // Billing
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();

    // Damage & Penalty
    public DbSet<DamagePenalty> DamagePenalties => Set<DamagePenalty>();

    // Customer CRM
    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();
    public DbSet<CustomerFollowUp> CustomerFollowUps => Set<CustomerFollowUp>();
    public DbSet<NoteRead> NoteReads => Set<NoteRead>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AuditLog>(b =>
        {
            b.ToTable("AuditLogs");
            b.HasKey(x => x.AuditLogID);
            b.Property(x => x.ActorUserId).IsRequired().HasMaxLength(450);
            b.Property(x => x.Action).IsRequired().HasMaxLength(100);
            b.Property(x => x.EntityName).IsRequired().HasMaxLength(100);
            b.Property(x => x.EntityId).IsRequired().HasMaxLength(100);
            b.Property(x => x.OldValuesJson);
            b.Property(x => x.NewValuesJson);
            b.Property(x => x.IpAddress).HasMaxLength(64);
            b.HasIndex(x => x.Timestamp);
            b.HasIndex(x => x.ActorUserId);
        });

        // --- Equipment Management ---
        builder.Entity<Category>(b =>
        {
            b.ToTable("Categories");
            b.HasKey(x => x.CategoryID);
            b.Property(x => x.Name).IsRequired().HasMaxLength(100);
            b.Property(x => x.Description).HasMaxLength(500);
            b.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<EquipmentCatalog>(b =>
        {
            b.ToTable("Equipment");
            b.HasKey(x => x.EquipmentID);
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.DailyRate).HasPrecision(10, 2);
            b.Property(x => x.ImageURL).HasMaxLength(500);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasOne(x => x.Category)
                .WithMany(c => c.Equipment)
                .HasForeignKey(x => x.CategoryID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.CategoryID);
            b.HasIndex(x => x.Name);
        });

        builder.Entity<EquipmentItem>(b =>
        {
            b.ToTable("EquipmentItems");
            b.HasKey(x => x.ItemID);
            b.Property(x => x.SerialNumber).IsRequired().HasMaxLength(100);
            b.Property(x => x.AvailabilityStatus).HasConversion<int>();
            b.Property(x => x.Location).HasMaxLength(200);
            b.HasOne(x => x.Equipment)
                .WithMany(e => e.Items)
                .HasForeignKey(x => x.EquipmentID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.EquipmentID, x.AvailabilityStatus });
            b.HasIndex(x => x.SerialNumber).IsUnique();
        });

        // --- Customer Management ---
        builder.Entity<Customer>(b =>
        {
            b.ToTable("Customers");
            b.HasKey(x => x.CustomerID);
            b.Property(x => x.FirstName).IsRequired().HasMaxLength(100);
            b.Property(x => x.LastName).IsRequired().HasMaxLength(100);
            b.Property(x => x.Email).IsRequired().HasMaxLength(256);
            b.Property(x => x.Phone).HasMaxLength(40);
            b.Property(x => x.Address).HasMaxLength(500);
            b.Property(x => x.City).HasMaxLength(100);
            b.Property(x => x.PostalCode).HasMaxLength(20);
            b.Property(x => x.LoyaltyTier).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.TotalSpent).HasPrecision(12, 2);
            b.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => x.UserId).IsUnique();
            b.HasIndex(x => new { x.LastName, x.FirstName });
            b.HasIndex(x => x.Email);
        });

        // --- Reservation Management ---
        builder.Entity<Reservation>(b =>
        {
            b.ToTable("Reservations");
            b.HasKey(x => x.ReservationID);
            b.Property(x => x.TotalEstimatedCost).HasPrecision(12, 2);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.Status, x.RentalStartDate });
            b.HasIndex(x => x.CustomerID);
        });

        builder.Entity<ReservationItem>(b =>
        {
            b.ToTable("ReservationItems");
            b.HasKey(x => x.ReservationItemID);
            b.Property(x => x.Notes).HasMaxLength(500);
            b.HasOne(x => x.Reservation)
                .WithMany(r => r.Items)
                .HasForeignKey(x => x.ReservationID)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Equipment)
                .WithMany()
                .HasForeignKey(x => x.EquipmentID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.AssignedItem)
                .WithMany()
                .HasForeignKey(x => x.AssignedItemID)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.ReservationID, x.EquipmentID });
        });

        // --- Rental Transaction Management ---
        builder.Entity<RentalTransaction>(b =>
        {
            b.ToTable("RentalTransactions");
            b.HasKey(x => x.RentalTransactionID);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.TotalAmount).HasPrecision(12, 2);
            b.Property(x => x.ConditionNotes).HasMaxLength(2000);
            b.HasOne(x => x.Reservation)
                .WithMany()
                .HasForeignKey(x => x.ReservationID)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.ProcessedByUser)
                .WithMany()
                .HasForeignKey(x => x.ProcessedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.ReturnedToUser)
                .WithMany()
                .HasForeignKey(x => x.ReturnedToUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.Status, x.CheckoutDate });
            b.HasIndex(x => x.CustomerID);
            b.HasIndex(x => x.ReservationID).IsUnique().HasFilter("[ReservationID] IS NOT NULL");
        });

        builder.Entity<RentalTransactionItem>(b =>
        {
            b.ToTable("RentalTransactionItems");
            b.HasKey(x => x.RentalTransactionItemID);
            b.HasOne(x => x.RentalTransaction)
                .WithMany(t => t.Items)
                .HasForeignKey(x => x.RentalTransactionID)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Equipment)
                .WithMany()
                .HasForeignKey(x => x.EquipmentID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.EquipmentItem)
                .WithMany()
                .HasForeignKey(x => x.EquipmentItemID)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.RentalTransactionID, x.EquipmentID });
            b.HasIndex(x => x.EquipmentItemID);
        });

        // --- Maintenance Management ---
        builder.Entity<MaintenanceRecord>(b =>
        {
            b.ToTable("MaintenanceRecords");
            b.HasKey(x => x.MaintenanceRecordID);
            b.Property(x => x.Reason).IsRequired().HasMaxLength(500);
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.Property(x => x.Cost).HasPrecision(10, 2);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasOne(x => x.EquipmentItem)
                .WithMany()
                .HasForeignKey(x => x.EquipmentItemID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.OpenedByUser)
                .WithMany()
                .HasForeignKey(x => x.OpenedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.ClosedByUser)
                .WithMany()
                .HasForeignKey(x => x.ClosedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.Status, x.StartedAt });
            b.HasIndex(x => x.EquipmentItemID);
        });

        // --- Billing ---
        builder.Entity<Invoice>(b =>
        {
            b.ToTable("Invoices");
            b.HasKey(x => x.InvoiceID);
            b.Property(x => x.InvoiceNumber).IsRequired().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.SubTotal).HasPrecision(12, 2);
            b.Property(x => x.TotalAmount).HasPrecision(12, 2);
            b.Property(x => x.AmountPaid).HasPrecision(12, 2);
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.Property(x => x.VoidedReason).HasMaxLength(500);
            b.HasOne(x => x.RentalTransaction)
                .WithMany()
                .HasForeignKey(x => x.RentalTransactionID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.VoidedByUser)
                .WithMany()
                .HasForeignKey(x => x.VoidedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.InvoiceNumber).IsUnique();
            b.HasIndex(x => x.RentalTransactionID).IsUnique();
            b.HasIndex(x => new { x.Status, x.InvoiceDate });
            b.HasIndex(x => x.CustomerID);
        });

        builder.Entity<InvoiceLine>(b =>
        {
            b.ToTable("InvoiceLines");
            b.HasKey(x => x.InvoiceLineID);
            b.Property(x => x.LineType).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Description).IsRequired().HasMaxLength(500);
            b.Property(x => x.Quantity).HasPrecision(10, 2);
            b.Property(x => x.UnitAmount).HasPrecision(12, 2);
            b.Property(x => x.LineTotal).HasPrecision(12, 2);
            b.Property(x => x.WaivedAmount).HasPrecision(12, 2);
            b.HasOne(x => x.Invoice)
                .WithMany(i => i.Lines)
                .HasForeignKey(x => x.InvoiceID)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.InvoiceID, x.LineType });
        });

        builder.Entity<Payment>(b =>
        {
            b.ToTable("Payments");
            b.HasKey(x => x.PaymentID);
            b.Property(x => x.Amount).HasPrecision(12, 2);
            b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ReferenceNumber).HasMaxLength(100);
            b.Property(x => x.Notes).HasMaxLength(500);
            b.HasOne(x => x.Invoice)
                .WithMany(i => i.Payments)
                .HasForeignKey(x => x.InvoiceID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.RecordedByUser)
                .WithMany()
                .HasForeignKey(x => x.RecordedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.InvoiceID, x.PaymentDate });
        });

        // --- Damage & Penalty ---
        builder.Entity<DamagePenalty>(b =>
        {
            b.ToTable("DamagePenalties");
            b.HasKey(x => x.DamagePenaltyID);
            b.Property(x => x.PenaltyType).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Description).IsRequired().HasMaxLength(500);
            b.Property(x => x.Amount).HasPrecision(12, 2);
            b.Property(x => x.WaivedAmount).HasPrecision(12, 2);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.HasOne(x => x.RentalTransaction)
                .WithMany()
                .HasForeignKey(x => x.RentalTransactionID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Equipment)
                .WithMany()
                .HasForeignKey(x => x.EquipmentID)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.EquipmentItem)
                .WithMany()
                .HasForeignKey(x => x.EquipmentItemID)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.ReportedByUser)
                .WithMany()
                .HasForeignKey(x => x.ReportedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.ResolvedByUser)
                .WithMany()
                .HasForeignKey(x => x.ResolvedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.RentalTransactionID, x.Status });
        });

        // --- Customer CRM ---
        builder.Entity<CustomerNote>(b =>
        {
            b.ToTable("CustomerNotes");
            b.HasKey(x => x.CustomerNoteID);
            b.Property(x => x.Body).IsRequired().HasMaxLength(2000);
            b.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerID)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.CustomerID);
            b.HasIndex(x => x.CreatedAt);
        });

        builder.Entity<CustomerFollowUp>(b =>
        {
            b.ToTable("CustomerFollowUps");
            b.HasKey(x => x.CustomerFollowUpID);
            b.Property(x => x.Reason).IsRequired().HasMaxLength(500);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ResolutionNotes).HasMaxLength(2000);
            b.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerID)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.AssignedToUser)
                .WithMany()
                .HasForeignKey(x => x.AssignedToUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.CustomerID);
            b.HasIndex(x => new { x.Status, x.FollowUpDate });
        });

        builder.Entity<NoteRead>(b =>
        {
            b.ToTable("NoteReads");
            b.HasKey(x => x.NoteReadID);
            b.Property(x => x.UserId).IsRequired().HasMaxLength(450);
            b.HasOne(x => x.CustomerNote)
                .WithMany()
                .HasForeignKey(x => x.CustomerNoteID)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.CustomerNoteID, x.UserId }).IsUnique();
            b.HasIndex(x => new { x.UserId, x.ReadAt });
        });
    }
}
