using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Services;
using RentalSphere.Modules.DamagePenalty.Services;

namespace RentalSphere.Modules.DamagePenalty.ViewComponents;

/// <summary>
/// Renders a small badge with the count of Pending DamagePenalty records
/// (damage or late-fee assessments waiting to be finalized). Returns empty
/// content for non-staff or when the count is 0. Used in the global sidebar
/// so staff see the unassessed penalty queue on every page.
/// </summary>
public class DamagePenaltyPendingBadgeViewComponent : ViewComponent
{
    private readonly IDamagePenaltyService _service;
    private readonly ICurrentUser _current;

    public DamagePenaltyPendingBadgeViewComponent(IDamagePenaltyService service, ICurrentUser current)
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

        var count = await _service.CountPendingAsync();
        return View(count);
    }
}
