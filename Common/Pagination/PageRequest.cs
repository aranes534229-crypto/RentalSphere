namespace RentalSphere.Common.Pagination;

/// <summary>
/// Standardized query-string shape for Index pages.
/// Defaults: page 1, pageSize 20, max pageSize 200.
/// </summary>
public class PageRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public int Skip => Math.Max(0, (Page - 1) * PageSize);
    public int Take => Math.Clamp(PageSize, 1, 200);
}

public static class PageRequestExtensions
{
    /// <summary>
    /// Clamps page/pageSize so callers always pass safe values downstream.
    /// </summary>
    public static PageRequest Normalize(this PageRequest r)
    {
        if (r.Page < 1) r.Page = 1;
        if (r.PageSize < 1) r.PageSize = 20;
        if (r.PageSize > 200) r.PageSize = 200;
        return r;
    }
}
