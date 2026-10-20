namespace InvoiceTracker.Application.Common.Exceptions;

/// <summary>
/// Thrown when a requested record doesn't exist, or exists but belongs to a different business
/// than the caller's (resolved server-side via <c>ICurrentUserService.BusinessId</c>). Both cases
/// map to the same 404 — per this project's multi-tenancy rule, a cross-tenant request must not
/// be able to distinguish "doesn't exist" from "exists but isn't yours" by getting a 403 instead.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }
}
