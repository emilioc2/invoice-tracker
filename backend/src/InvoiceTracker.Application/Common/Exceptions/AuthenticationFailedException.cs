namespace InvoiceTracker.Application.Common.Exceptions;

/// <summary>
/// Thrown when login credentials are invalid. Maps to a clean 401 (never a 500) in the Api
/// layer's exception-handling middleware.
/// </summary>
public class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string message)
        : base(message)
    {
    }
}
