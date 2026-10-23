using FluentAssertions;
using InvoiceTracker.Application.BusinessProfile.Commands.UpdatePaymentDetails;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.BusinessProfile;

/// <summary>
/// Unit tests for <see cref="UpdatePaymentDetailsCommandHandler"/> per architecture.md's test
/// plan: resolves the business via <c>ICurrentUserService.BusinessId</c> and writes the submitted
/// value as-is, including setting it back to <c>null</c>, with no role check performed.
/// </summary>
public class UpdatePaymentDetailsCommandHandlerTests
{
    [Fact]
    public async Task Handle_WritesSubmittedValue_ToCallersOwnBusiness()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();

        db.Businesses.Add(business);
        await db.SaveChangesAsync();

        var handler = new UpdatePaymentDetailsCommandHandler(db, new TestCurrentUserService(business.Id));

        var result = await handler.Handle(
            new UpdatePaymentDetailsCommand("Bank: Acme Bank, Acct: 123456"), CancellationToken.None);

        result.PaymentDetails.Should().Be("Bank: Acme Bank, Acct: 123456");

        var reloaded = db.Businesses.Single(b => b.Id == business.Id);
        reloaded.PaymentDetails.Should().Be("Bank: Acme Bank, Acct: 123456");
    }

    [Fact]
    public async Task Handle_CanClearPaymentDetailsBackToNull()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        business.PaymentDetails = "Some existing default";

        db.Businesses.Add(business);
        await db.SaveChangesAsync();

        var handler = new UpdatePaymentDetailsCommandHandler(db, new TestCurrentUserService(business.Id));

        var result = await handler.Handle(new UpdatePaymentDetailsCommand(null), CancellationToken.None);

        result.PaymentDetails.Should().BeNull();

        var reloaded = db.Businesses.Single(b => b.Id == business.Id);
        reloaded.PaymentDetails.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NeverUpdatesAnotherBusinesssPaymentDetails()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness("Acme Co", "acme@example.com");
        var otherBusiness = InvoiceSeeding.MakeBusiness("Other Co", "other@example.com");
        otherBusiness.PaymentDetails = "Other Co's original details";

        db.Businesses.AddRange(business, otherBusiness);
        await db.SaveChangesAsync();

        var handler = new UpdatePaymentDetailsCommandHandler(db, new TestCurrentUserService(business.Id));

        await handler.Handle(new UpdatePaymentDetailsCommand("Acme's new details"), CancellationToken.None);

        var reloadedOther = db.Businesses.Single(b => b.Id == otherBusiness.Id);
        reloadedOther.PaymentDetails.Should().Be("Other Co's original details");
    }
}
