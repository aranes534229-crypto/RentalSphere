namespace RentalSphere.Modules.EquipmentAvailability.DTOs;

/// <summary>
/// One column (date) in the availability calendar for one piece of equipment.
/// </summary>
public class AvailabilityCellDto
{
    public DateTime Date { get; set; }
    public int Available { get; set; }
}

/// <summary>
/// One row of the calendar: equipment header + per-date available counts.
/// </summary>
public class AvailabilityRowDto
{
    public int EquipmentID { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int TotalUnits { get; set; }
    public List<AvailabilityCellDto> Cells { get; set; } = new();
}

public class AvailabilityCalendarDto
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public List<DateTime> Dates { get; set; } = new();
    public List<AvailabilityRowDto> Rows { get; set; } = new();
}
