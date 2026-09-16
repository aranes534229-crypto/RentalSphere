using System.ComponentModel.DataAnnotations;
using RentalSphere.Modules.RentalTransaction.Models;

namespace RentalSphere.Modules.RentalTransaction.DTOs;

public class RentalTransactionItemDto
{
    public int RentalTransactionItemID { get; set; }
    public int EquipmentID { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal DailyRate { get; set; }
    public decimal LineTotal { get; set; }
    public int? EquipmentItemID { get; set; }
    public string? SerialNumber { get; set; }
}

public class RentalTransactionListItemDto
{
    public int RentalTransactionID { get; set; }
    public int ReservationID { get; set; }
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime CheckoutDate { get; set; }
    public DateTime ExpectedReturnDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Active";
}

public class RentalTransactionDetailsDto
{
    public int RentalTransactionID { get; set; }
    public int ReservationID { get; set; }
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTime CheckoutDate { get; set; }
    public DateTime ExpectedReturnDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string? ConditionNotes { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Active";
    public string? ProcessedByUserName { get; set; }
    public string? ReturnedToUserName { get; set; }
    public List<RentalTransactionItemDto> Items { get; set; } = new();
}

public class RentalTransactionReturnDto
{
    [Required]
    public int RentalTransactionID { get; set; }

    [StringLength(2000)]
    [Display(Name = "Condition notes")]
    public string? ConditionNotes { get; set; }

    /// <summary>
    /// True when the equipment needs to be quarantined for maintenance /
    /// inspection (damaged, dirty, or in need of repair). Triggers a unit
    /// status flip to <c>InMaintenance</c> and an open maintenance ticket
    /// per pinned unit. May be combined with <see cref="FlagForLatePenalty"/>.
    /// </summary>
    [Display(Name = "Flag for maintenance / inspection")]
    public bool FlagForMaintenance { get; set; }

    /// <summary>
    /// True when the rental is being returned past its expected return
    /// date (late fee applies). Triggers a single transaction-level
    /// <c>LateFee</c> DamagePenalty row; equipment units return to
    /// <c>Available</c>. May be combined with <see cref="FlagForMaintenance"/>.
    /// </summary>
    [Display(Name = "Assess late return penalty")]
    public bool FlagForLatePenalty { get; set; }

    /// <summary>
    /// Per-line unit assignments to quarantine when the maintenance flag is
    /// on. Only consulted for lines whose <c>EquipmentItemID</c> is null
    /// (unpinned). Entries for already-pinned lines are ignored.
    /// </summary>
    public List<DamageItemAssignmentDto> DamageAssignments { get; set; } = new();
}

public class DamageItemAssignmentDto
{
    [Required]
    public int RentalTransactionItemID { get; set; }

    [Required]
    public int EquipmentItemID { get; set; }
}
