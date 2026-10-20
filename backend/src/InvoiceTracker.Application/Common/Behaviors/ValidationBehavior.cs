using FluentValidation;
using MediatR;

namespace InvoiceTracker.Application.Common.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs every registered FluentValidation validator for
/// <typeparamref name="TRequest"/> before the matching handler executes. Handlers never check
/// "is this input valid" themselves — they only ever see validated input, per this project's
/// validation convention.
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);

            var validationResults = await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

            var failures = validationResults
                .SelectMany(result => result.Errors)
                .Where(failure => failure is not null)
                .ToList();

            if (failures.Count != 0)
            {
                throw new Exceptions.ValidationException(failures);
            }
        }

        return await next();
    }
}
