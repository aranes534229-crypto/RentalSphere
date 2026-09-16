using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Services;
using RentalSphere.Modules.Billing.Services;

namespace RentalSphere.Modules.Billing.ViewComponents;

/// <summary>
/// Renders a small badge with the count of invoices that still have a
/// balance (Unpaid + PartiallyPaid). Returns empty content for non-staff
/// or when the count is 0. Used in the global sidebar so staff see the
/// open-billing queue on every page.
/// </summary>
public class BillingUnpaidBadgeViewComponent : ViewComponent
{
    private readonly IBillingService _service;
    private readonly ICurrentUser _current;

    public BillingUnpaidBadgeViewComponent(IBillingService service, ICurrentUser current)
    {
        _service = service;
        _current = current;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (string.IsNullOrEmpty(_current.UserId) || !(_current.IsAdmin || _current.IsStaff))
        {
            return Content(string.Empty);
        }

        var count = await _service.CountUnpaidOrPartialAsync();
        return View(count);
    }
}
