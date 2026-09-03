using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RentalSphere.Common.Exceptions;

namespace RentalSphere.Common.Filters;

/// <summary>
/// Maps domain exceptions to HTTP responses so controllers don't repeat try/catch:
///   - ForbiddenException  -> 403 with a friendly view
///   - NotFoundException   -> 404
///   - anything else       -> rethrown so the developer page sees it
/// </summary>
public class GlobalExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> _logger;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case ForbiddenException fe:
                _logger.LogWarning(fe, "Forbidden: {Message}", fe.Message);
                context.Result = new ViewResult
                {
                    ViewName = "Forbidden",
                    StatusCode = StatusCodes.Status403Forbidden,
                };
                context.ExceptionHandled = true;
                break;

            case NotFoundException nfe:
                _logger.LogInformation(nfe, "Not found: {Message}", nfe.Message);
                context.Result = new ViewResult
                {
                    ViewName = "NotFound",
                    StatusCode = StatusCodes.Status404NotFound,
                };
                context.ExceptionHandled = true;
                break;
        }
    }
}
