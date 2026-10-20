namespace InvoiceTracker.Application.Common.Exceptions;

/// <summary>
/// Thrown when an underlying ASP.NET Core Identity operation (create user, reset password, ...)
/// fails — e.g. a duplicate email on register, or an invalid/expired token on reset-password.
/// Maps to a 400 in the Api layer's exception-handling middleware.
/// </summary>
public class IdentityOperationException : Exception
{
    public IdentityOperationException(IEnumerable<string> errors)
        : base(string.Join("; ", errors))
    {
        Errors = errors.ToArray();
    }

    public string[] Errors { get; }
}
