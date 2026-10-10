using delivery_schedule_management.Application.Common.Exceptions;
using delivery_schedule_management.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace delivery_schedule_management.Api.Infrastructure;

/// <summary>
/// Converts well-known application/domain exceptions into RFC 9110-compliant <see cref="ProblemDetails"/>
/// responses, mapping <see cref="ValidationException"/> → 400, <see cref="NotFoundException"/> → 404,
/// <see cref="UnauthorizedAccessException"/> → 401, <see cref="ForbiddenAccessException"/> → 403,
/// <see cref="DeliveryWindowLockedException"/> → 409 (48h business rule, CLAUDE.md §1/§5), and
/// <see cref="InvalidDeliveryDayTransitionException"/> → 422 (invalid state transition, CLAUDE.md §5).
/// Unrecognised exceptions are not handled and fall through to the default middleware.
/// </summary>
public class ProblemDetailsExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, problemDetails) = exception switch
        {
            ValidationException ve => (StatusCodes.Status400BadRequest, (ProblemDetails)new ValidationProblemDetails(ve.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            }),
            NotFoundException ne => (StatusCodes.Status404NotFound, new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                Title = "The specified resource was not found.",
                Detail = ne.Message
            }),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2"
            }),
            ForbiddenAccessException => (StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4"
            }),
            DeliveryWindowLockedException dw => (StatusCodes.Status409Conflict, new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                Title = "The delivery day is within the 48-hour lock window.",
                Detail = dw.Message
            }),
            InvalidDeliveryDayTransitionException it => (StatusCodes.Status422UnprocessableEntity, new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.21",
                Title = "The requested operation is not valid for the delivery day's current status.",
                Detail = it.Message
            }),
            ArgumentOutOfRangeException aoor => (StatusCodes.Status422UnprocessableEntity, new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.21",
                Title = "The provided value is out of the allowed range.",
                Detail = aoor.Message
            }),
            ArgumentException ae => (StatusCodes.Status422UnprocessableEntity, new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.21",
                Title = "The provided argument is invalid.",
                Detail = ae.Message
            }),
            _ => (-1, null)
        };

        if (problemDetails is null) return false;

        httpContext.Response.StatusCode = statusCode;
        // Serialize by runtime type: with the static type ProblemDetails, ValidationProblemDetails.Errors would be dropped.
        await httpContext.Response.WriteAsJsonAsync((object)problemDetails, problemDetails.GetType(), cancellationToken);
        return true;
    }
}
