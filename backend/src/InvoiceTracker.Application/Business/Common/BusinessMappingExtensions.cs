using InvoiceTracker.Domain.Entities;

namespace InvoiceTracker.Application.BusinessProfile.Common;

public static class BusinessMappingExtensions
{
    public static BusinessProfileDto ToProfileDto(this Business business) =>
        new(business.Id, business.Name, business.Email, business.PaymentDetails);
}
