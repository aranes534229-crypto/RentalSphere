using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Services;
using RentalSphere.Modules.RentalTransaction.Services;

namespace RentalSphere.Modules.RentalTransaction.ViewComponents;

/// <summary>
/// Renders a small badge with the count of overdue rental transactions
/// (Status == Active and ExpectedReturnDate already past). Scope-aware:
/// staff see the global count, the signed-in customer sees only their own.
/// Returns empty content for anonymous or when the count is 0.
/// </summary>
public class RentalTransactionOverdueBadgeViewComponent : ViewComponent
{
    private readonly IRentalTransactionService _service;
    private readonly ICurrentUser _current;

    public RentalTransactionOverdueBadgeViewComponent(IRentalTransactionService service, ICurrentUser current)
    {
        _service = service;
        _current = current;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (string.IsNullOrEmpty(_current.UserId)) return Content(string.Empty);

        var count = await _service.CountOverdueForCurrentUserAsync();
        return View(count);
    }
}

