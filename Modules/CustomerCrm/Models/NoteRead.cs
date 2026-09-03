using RentalSphere.Identity;

namespace RentalSphere.Modules.CustomerCrm.Models;

/// <summary>
/// Per-staff read state on a CustomerNote. One row per (Note, User) that has
/// read the note. Absence of a row means unread for that user.
/// </summary>
public class NoteRead
{
    public int NoteReadID { get; set; }
    public int CustomerNoteID { get; set; }
    public CustomerNote? CustomerNote { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}
