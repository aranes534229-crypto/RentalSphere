using Microsoft.AspNetCore.Mvc;
using RentalSphere.Common.Services;
using RentalSphere.Modules.CustomerCrm.Services;

namespace RentalSphere.Modules.CustomerCrm.ViewComponents;

/// <summary>
/// Renders a small badge with the total unread CRM items count for the
/// current user. Returns empty content for Customer role or no items.
/// Used in the global sidebar so staff see their unread count on every page.
/// </summary>
public class CrmUnreadBadgeViewComponent : ViewComponent
{
    private readonly ICustomerCrmService _service;
    private readonly ICurrentUser _current;

    public CrmUnreadBadgeViewComponent(ICustomerCrmService service, ICurrentUser current)
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

        var count = await _service.CountUnreadCrmItemsForUserAsync(_current.UserId);
        ViewBag.Count = count;
        return View(count);
    }
}
