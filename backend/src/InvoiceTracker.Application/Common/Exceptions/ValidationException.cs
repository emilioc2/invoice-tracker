using FluentValidation.Results;

namespace InvoiceTracker.Application.Common.Exceptions;

/// <summary>
/// Thrown by <see cref="InvoiceTracker.Application.Common.Behaviors.ValidationBehavior{TRequest,TResponse}"/>
/// when a command/query's FluentValidation validator(s) fail. Kept distinct from FluentValidation's
/// own exception type so the Api layer's exception-handling middleware only needs to know about
/// this project's own exception types.
/// </summary>
public class ValidationException : Exception
{
    public ValidationException()
        : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }

    public IDictionary<string, string[]> Errors { get; }
}
