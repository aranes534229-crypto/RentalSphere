using System.ComponentModel.DataAnnotations;
using RentalSphere.Modules.CustomerCrm.Models;

namespace RentalSphere.Modules.CustomerCrm.DTOs;

public class CustomerNoteDto
{
    public int CustomerNoteID { get; set; }
    public int CustomerID { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsFlagged { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserName { get; set; }

    /// <summary>True when no NoteRead row exists for the current viewer.</summary>
    public bool IsUnreadForCurrentUser { get; set; }
}

public class CustomerNoteCreateDto
{
    [Required]
    public int CustomerID { get; set; }

    [Required, StringLength(2000, MinimumLength = 1)]
    public string Body { get; set; } = string.Empty;

    public bool IsFlagged { get; set; }
}

public class CustomerFollowUpDto
{
    public int CustomerFollowUpID { get; set; }
    public int CustomerID { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime FollowUpDate { get; set; }
    public string Status { get; set; } = FollowUpStatus.Pending.ToString();
    public string? AssignedToUserId { get; set; }
    public string? AssignedToUserName { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByUserName { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}

public class CustomerFollowUpCreateDto
{
    [Required]
    public int CustomerID { get; set; }

    [Required, StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public DateTime FollowUpDate { get; set; }

    public string? AssignedToUserId { get; set; }
}

public class CustomerFollowUpCompleteDto
{
    [Required]
    public int CustomerFollowUpID { get; set; }

    [StringLength(2000)]
    public string? ResolutionNotes { get; set; }
}

public static class CustomerCrmMappings
{
    public static CustomerNoteDto Map(CustomerNote n) => new()
    {
        CustomerNoteID = n.CustomerNoteID,
        CustomerID = n.CustomerID,
        Body = n.Body,
        IsFlagged = n.IsFlagged,
        CreatedAt = n.CreatedAt,
        CreatedByUserName = n.CreatedByUser is null
            ? null
            : $"{n.CreatedByUser.FirstName} {n.CreatedByUser.LastName}",
    };

    public static CustomerFollowUpDto Map(CustomerFollowUp f) => new()
    {
        CustomerFollowUpID = f.CustomerFollowUpID,
        CustomerID = f.CustomerID,
        Reason = f.Reason,
        FollowUpDate = f.FollowUpDate,
        Status = f.Status.ToString(),
        AssignedToUserId = f.AssignedToUserId,
        AssignedToUserName = f.AssignedToUser is null
            ? null
            : $"{f.AssignedToUser.FirstName} {f.AssignedToUser.LastName}",
        CompletedAt = f.CompletedAt,
        ResolutionNotes = f.ResolutionNotes,
        CreatedAt = f.CreatedAt,
        CreatedByUserName = f.CreatedByUser is null
            ? null
            : $"{f.CreatedByUser.FirstName} {f.CreatedByUser.LastName}",
        AcknowledgedAt = f.AcknowledgedAt,
    };
}
