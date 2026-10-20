namespace InvoiceTracker.Application.Common.Exceptions;

/// <summary>
/// Thrown when a request is well-formed and targets a record the caller owns, but violates a
/// business rule that isn't a FluentValidation input-shape concern — e.g. editing a non-Draft
/// invoice, or an invalid invoice status transition. Maps to a clean 400 in the Api layer's
/// exception-handling middleware.
/// </summary>
public class BadRequestException : Exception
{
    public BadRequestException(string message)
        : base(message)
    {
    }
}
