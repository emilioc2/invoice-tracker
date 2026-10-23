using FluentAssertions;
using InvoiceTracker.Application.BusinessProfile.Queries.GetBusinessProfile;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.BusinessProfile;

/// <summary>
/// Unit tests for <see cref="GetBusinessProfileQueryHandler"/> per architecture.md's test plan:
/// resolves the target business via <c>ICurrentUserService.BusinessId</c>, never a
/// client-supplied id, and returns exactly the four documented fields.
/// </summary>
public class GetBusinessProfileQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsCallersOwnBusinessProfile_IncludingPaymentDetails()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness("Acme Co", "acme@example.com");
        business.PaymentDetails = "Bank: Acme Bank, Acct: 123456";

        db.Businesses.Add(business);
        await db.SaveChangesAsync();

        var handler = new GetBusinessProfileQueryHandler(db, new TestCurrentUserService(business.Id));

        var result = await handler.Handle(new GetBusinessProfileQuery(), CancellationToken.None);

        result.Id.Should().Be(business.Id);
        result.Name.Should().Be("Acme Co");
        result.Email.Should().Be("acme@example.com");
        result.PaymentDetails.Should().Be("Bank: Acme Bank, Acct: 123456");
    }

    [Fact]
    public async Task Handle_ReturnsNullPaymentDetails_WhenNeverSet()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();

        db.Businesses.Add(business);
        await db.SaveChangesAsync();

        var handler = new GetBusinessProfileQueryHandler(db, new TestCurrentUserService(business.Id));

        var result = await handler.Handle(new GetBusinessProfileQuery(), CancellationToken.None);

        result.PaymentDetails.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NeverReturnsAnotherBusinesssProfile()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness("Acme Co", "acme@example.com");
        var otherBusiness = InvoiceSeeding.MakeBusiness("Other Co", "other@example.com");
        otherBusiness.PaymentDetails = "Other Co's secret bank details";

        db.Businesses.AddRange(business, otherBusiness);
        await db.SaveChangesAsync();

        // The caller's JWT is scoped to `business`, not `otherBusiness` — the handler must never
        // leak another business's row, even though both exist in the same database.
        var handler = new GetBusinessProfileQueryHandler(db, new TestCurrentUserService(business.Id));

        var result = await handler.Handle(new GetBusinessProfileQuery(), CancellationToken.None);

        result.Id.Should().Be(business.Id);
        result.PaymentDetails.Should().BeNull();
    }
}
