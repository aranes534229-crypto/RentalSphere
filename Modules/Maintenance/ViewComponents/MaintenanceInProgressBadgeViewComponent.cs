using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Services;
using RentalSphere.Modules.Maintenance.Services;

namespace RentalSphere.Modules.Maintenance.ViewComponents;

/// <summary>
/// Renders a small badge with the count of InProgress maintenance tickets.
/// Returns empty content for non-staff or when the count is 0. Used in the
/// global sidebar so staff see the open repair queue on every page.
/// </summary>
public class MaintenanceInProgressBadgeViewComponent : ViewComponent
{
    private readonly IMaintenanceService _service;
    private readonly ICurrentUser _current;

    public MaintenanceInProgressBadgeViewComponent(IMaintenanceService service, ICurrentUser current)
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

        var count = await _service.CountInProgressAsync();
        return View(count);
    }
}
