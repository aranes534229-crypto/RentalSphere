using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace RentalSphere.Modules.Equipment.DTOs;

public class EquipmentListItemDto
{
    public int EquipmentID { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryID { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal DailyRate { get; set; }
    public int StockQuantity { get; set; }
    public string Status { get; set; } = "Available";
    public DateTime DateAdded { get; set; }
    public string? ImageURL { get; set; }
    public string? SerialPrefix { get; set; }
}

public class EquipmentDetailsDto
{
    public int EquipmentID { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryID { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal DailyRate { get; set; }
    public int StockQuantity { get; set; }
    public string Status { get; set; } = "Available";
    public DateTime DateAdded { get; set; }
    public string? ImageURL { get; set; }
    public string? SerialPrefix { get; set; }
    public int TotalStock { get; set; }
    public int BookedCount { get; set; }
    public int AvailableCount { get; set; }
    public DateTime? RangeStart { get; set; }
    public DateTime? RangeEnd { get; set; }
}

public class EquipmentCreateDto
{
    [Required, StringLength(200)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Category")]
    public int CategoryID { get; set; }

    [StringLength(2000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Required, Range(0, 100000)]
    [Display(Name = "Daily rate")]
    public decimal DailyRate { get; set; }

    [Range(0, 100000)]
    [Display(Name = "Initial stock quantity")]
    public int StockQuantity { get; set; }

    [Display(Name = "Status")]
    public string Status { get; set; } = "Available";

    [StringLength(500)]
    [Display(Name = "Image")]
    public string? ImageURL { get; set; }

    [Display(Name = "Upload image")]
    public IFormFile? ImageFile { get; set; }

    [StringLength(8)]
    [RegularExpression(@"^[A-Z0-9\-]*$", ErrorMessage = "Use uppercase letters, digits, and dashes only (e.g. TBL-, CHR-).")]
    [Display(Name = "Serial prefix")]
    public string? SerialPrefix { get; set; }
}

public class EquipmentUpdateDto
{
    public int EquipmentID { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Category")]
    public int CategoryID { get; set; }

    [StringLength(2000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Required, Range(0, 100000)]
    [Display(Name = "Daily rate")]
    public decimal DailyRate { get; set; }

    [Range(0, 100000)]
    [Display(Name = "Stock quantity")]
    public int StockQuantity { get; set; }

    [Required]
    [Display(Name = "Status")]
    public string Status { get; set; } = "Available";

    [StringLength(500)]
    [Display(Name = "Image")]
    public string? ImageURL { get; set; }

    [Display(Name = "Upload new image")]
    public IFormFile? ImageFile { get; set; }

    [StringLength(8)]
    [RegularExpression(@"^[A-Z0-9\-]*$", ErrorMessage = "Use uppercase letters, digits, and dashes only (e.g. TBL-, CHR-).")]
    [Display(Name = "Serial prefix")]
    public string? SerialPrefix { get; set; }
}

public class CategoryDto
{
    public int CategoryID { get; set; }
    [Required, StringLength(100)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Description")]
    public string? Description { get; set; }
}
