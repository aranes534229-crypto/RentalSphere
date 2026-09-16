using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Services;
using RentalSphere.Modules.ReservationManagement.Services;

namespace RentalSphere.Modules.ReservationManagement.ViewComponents;

/// <summary>
/// Renders a small badge with the count of open-in-flight reservations
/// (Pending for staff; Pending + Confirmed for the current customer). Returns
/// empty content for anonymous or no rows. Used in the global sidebar so
/// staff see how many reservations need confirmation at a glance and
/// customers see how many of their reservations are still being processed.
/// </summary>
public class ReservationPendingBadgeViewComponent : ViewComponent
{
    private readonly IReservationService _service;
    private readonly ICurrentUser _current;

    public ReservationPendingBadgeViewComponent(IReservationService service, ICurrentUser current)
    {
        _service = service;
        _current = current;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (string.IsNullOrEmpty(_current.UserId)) return Content(string.Empty);

        var count = await _service.CountPendingForCurrentUserAsync();
        return View(count);
    }
}

