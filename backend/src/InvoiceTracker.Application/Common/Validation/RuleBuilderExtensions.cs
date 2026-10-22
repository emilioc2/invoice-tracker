using System.Linq.Expressions;
using FluentValidation;

namespace InvoiceTracker.Application.Common.Validation;

/// <summary>
/// Shared FluentValidation rule sets used by more than one validator. Per
/// architecture.md's "Shared rules across validators" note, a rule used by multiple
/// validators belongs here once instead of being copy-pasted into each validator, so the
/// policy can't silently diverge between entry points.
/// </summary>
public static class RuleBuilderExtensions
{
    /// <summary>
    /// Password-strength rules shared by <c>RegisterCommandValidator</c> and
    /// <c>ResetPasswordCommandValidator</c>: at least 8 characters, with at least one
    /// uppercase letter, one lowercase letter, and one digit. Does not include
    /// <c>NotEmpty()</c> — callers add that themselves alongside this rule set.
    /// </summary>
    public static IRuleBuilderOptions<T, string> PasswordRules<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.");
    }

    /// <summary>
    /// Phone format rules shared by <c>CreateClientCommandValidator</c> and
    /// <c>UpdateClientCommandValidator</c>: at most 15 characters, digits only (no <c>+</c>,
    /// spaces, dashes, parentheses, or letters). Phone is optional on <c>Client</c>, so a
    /// <c>null</c>/empty value passes -- callers add <c>NotEmpty()</c> themselves if they ever
    /// need to require it.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> PhoneRules<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(15)
            .Matches("^[0-9]*$").WithMessage("Phone must contain digits only.");
    }

    /// <summary>
    /// Pagination bounds shared by every list query validator: <c>PageNumber</c> must be at
    /// least 1, and <c>PageSize</c> must be between 1 and 100 (inclusive).
    /// </summary>
    public static void PaginationRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int>> pageNumberSelector,
        Expression<Func<T, int>> pageSizeSelector)
    {
        validator.RuleFor(pageNumberSelector).GreaterThanOrEqualTo(1);
        validator.RuleFor(pageSizeSelector).InclusiveBetween(1, 100);
    }
}
