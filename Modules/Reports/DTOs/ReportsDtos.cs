namespace RentalSphere.Modules.Reports.DTOs;

public class RevenuePeriodRowDto
{
    /// <summary>Bucket label — daily "yyyy-MM-dd" or weekly "yyyy-Www" or monthly "yyyy-MM".</summary>
    public string Period { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public decimal Revenue { get; set; }
    public int InvoiceCount { get; set; }
    public int RentalTransactionCount { get; set; }
}

public class UtilizationRowDto
{
    public int EquipmentID { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int TotalUnits { get; set; }

    /// <summary>Sum of reserved unit-days across the window (Pending + Confirmed reservations, plus Active rentals).</summary>
    public int ReservedDays { get; set; }

    /// <summary>Total unit-days the equipment could have been booked (units × window length).</summary>
    public int AvailableDays { get; set; }

    /// <summary>ReservedDays / AvailableDays as a percentage in [0, 100].</summary>
    public decimal UtilizationPercent { get; set; }
}

public class OutstandingBalanceRowDto
{
    public int InvoiceID { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Outstanding { get; set; }
    public DateTime DueDate { get; set; }
    public int DaysOverdue { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class LateReturnRowDto
{
    public int RentalTransactionID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string EquipmentSummary { get; set; } = string.Empty;
    public DateTime ExpectedReturnDate { get; set; }
    public DateTime? ActualReturnDate { get; set; }
    public int DaysLate { get; set; }
    public decimal LateFee { get; set; }
}

public class DamageTrendRowDto
{
    public string Period { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public decimal DamageAmount { get; set; }
    public int PenaltyCount { get; set; }
    public int AffectedTransactions { get; set; }
}

public class ReportsBundleDto
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string Granularity { get; set; } = "Daily";

    public List<RevenuePeriodRowDto> Revenue { get; set; } = new();
    public List<UtilizationRowDto> Utilization { get; set; } = new();
    public List<OutstandingBalanceRowDto> OutstandingBalances { get; set; } = new();
    public List<LateReturnRowDto> LateReturns { get; set; } = new();
    public List<DamageTrendRowDto> DamageTrends { get; set; } = new();

    public decimal TotalRevenue { get; set; }
    public decimal TotalOutstanding { get; set; }
    public int LateReturnCount { get; set; }
    public decimal TotalDamageAmount { get; set; }

    /// <summary>Sum of MaintenanceRecord.Cost for closed records in the window.</summary>
    public decimal TotalMaintenanceExpenses { get; set; }

    /// <summary>TotalRevenue - TotalMaintenanceExpenses. Can be negative.</summary>
    public decimal NetRevenue { get; set; }
}
