using FluentValidation;

namespace InvoiceTracker.Application.BusinessProfile.Commands.UpdatePaymentDetails;

/// <summary>
/// No documented length/format constraint beyond a sane cap — mirrors the same
/// <c>MaximumLength(2000)</c> already applied to <c>Invoice.Notes</c>, matching the column's own
/// <c>HasMaxLength(2000)</c> in <c>BusinessConfiguration</c>. <see cref="UpdatePaymentDetailsCommand.PaymentDetails"/>
/// is nullable, so there is deliberately no <c>NotEmpty</c>/required rule.
/// </summary>
public class UpdatePaymentDetailsCommandValidator : AbstractValidator<UpdatePaymentDetailsCommand>
{
    public UpdatePaymentDetailsCommandValidator()
    {
        RuleFor(x => x.PaymentDetails)
            .MaximumLength(2000);
    }
}
