namespace InvoiceTracker.Application.Common.Exceptions;

/// <summary>
/// Thrown when a request is well-formed but conflicts with an already-existing resource that isn't
/// a simple "not found"/"bad input" case — e.g. registering with an email that already has an
/// identity (see requirements.md's "Multi-business membership": the caller should log in and use
/// "Add a business" instead). Maps to a clean 409 in the Api layer's exception-handling middleware.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }
}
