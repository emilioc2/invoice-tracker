using InvoiceTracker.Application.BusinessProfile.Common;
using InvoiceTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.BusinessProfile.Commands.UpdatePaymentDetails;

public class UpdatePaymentDetailsCommandHandler : IRequestHandler<UpdatePaymentDetailsCommand, BusinessProfileDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public UpdatePaymentDetailsCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<BusinessProfileDto> Handle(UpdatePaymentDetailsCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var business = await _dbContext.Businesses
            .SingleAsync(b => b.Id == businessId, cancellationToken);

        // Writes the submitted value as-is, including null — clearing the business's default
        // back to nothing is a legitimate, deliberate action here, not treated as "no change".
        business.PaymentDetails = request.PaymentDetails;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return business.ToProfileDto();
    }
}
