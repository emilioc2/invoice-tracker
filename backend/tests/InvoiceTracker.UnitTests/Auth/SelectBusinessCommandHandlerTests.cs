using FluentAssertions;
using InvoiceTracker.Application.Auth.Commands.SelectBusiness;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;
using NSubstitute;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// Unit tests for <see cref="SelectBusinessCommandHandler"/>'s own responsibility: validating the
/// preauth token (via mocked <see cref="IJwtTokenGenerator.ValidatePreAuthToken"/>) and re-checking,
/// server-side, that the identity it names actually holds an active membership in the requested
/// business — never trusting the request's BusinessId outright.
/// </summary>
public class SelectBusinessCommandHandlerTests
{
    private static SelectBusinessCommandHandler MakeHandler(
        TestApplicationDbContext db, IIdentityService identityService, IJwtTokenGenerator jwtTokenGenerator) =>
        new(db, identityService, jwtTokenGenerator);

    [Fact]
    public async Task InvalidOrExpiredPreAuthToken_ThrowsAuthenticationFailed()
    {
        using var db = TestApplicationDbContext.Create();
        var identityService = Substitute.For<IIdentityService>();
        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        jwtTokenGenerator.ValidatePreAuthToken("bad-token").Returns((Guid?)null);

        var handler = MakeHandler(db, identityService, jwtTokenGenerator);

        var act = () => handler.Handle(new SelectBusinessCommand("bad-token", Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationFailedException>();
    }

    [Fact]
    public async Task ValidToken_ButIdentityHasNoActiveMembershipInTheRequestedBusiness_ThrowsNotFound()
    {
        using var db = TestApplicationDbContext.Create();
        var userId = Guid.NewGuid();
        var ownedBusiness = new Business { Id = Guid.NewGuid(), Name = "Owned", Email = "owned@example.com" };
        var foreignBusinessId = Guid.NewGuid();
        db.Businesses.Add(ownedBusiness);
        db.UserBusinessMemberships.Add(new UserBusinessMembership { Id = Guid.NewGuid(), UserId = userId, BusinessId = ownedBusiness.Id, Role = UserRole.Owner });
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        jwtTokenGenerator.ValidatePreAuthToken("good-token").Returns(userId);

        var handler = MakeHandler(db, identityService, jwtTokenGenerator);

        // Structurally valid token (resolves to a real userId) but targeting a business this
        // identity has no membership in at all.
        var act = () => handler.Handle(new SelectBusinessCommand("good-token", foreignBusinessId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ValidToken_ButMembershipIsDeactivated_ThrowsNotFound()
    {
        using var db = TestApplicationDbContext.Create();
        var userId = Guid.NewGuid();
        var business = new Business { Id = Guid.NewGuid(), Name = "Biz", Email = "biz@example.com" };
        db.Businesses.Add(business);
        var membership = new UserBusinessMembership { Id = Guid.NewGuid(), UserId = userId, BusinessId = business.Id, Role = UserRole.Staff };
        membership.TransitionTo(UserStatus.Deactivated);
        db.UserBusinessMemberships.Add(membership);
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        jwtTokenGenerator.ValidatePreAuthToken("good-token").Returns(userId);

        var handler = MakeHandler(db, identityService, jwtTokenGenerator);

        var act = () => handler.Handle(new SelectBusinessCommand("good-token", business.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ValidToken_AndMatchingActiveMembership_ReturnsATokenScopedToIt()
    {
        using var db = TestApplicationDbContext.Create();
        var userId = Guid.NewGuid();
        var business = new Business { Id = Guid.NewGuid(), Name = "Biz", Email = "biz@example.com" };
        db.Businesses.Add(business);
        db.UserBusinessMemberships.Add(new UserBusinessMembership { Id = Guid.NewGuid(), UserId = userId, BusinessId = business.Id, Role = UserRole.Owner });
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        identityService.FindIdentityByIdAsync(userId).Returns(new AuthenticatedIdentityDto(userId, "owner@example.com", false));

        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        jwtTokenGenerator.ValidatePreAuthToken("good-token").Returns(userId);
        jwtTokenGenerator.GenerateToken(userId, business.Id, "Owner", "owner@example.com")
            .Returns(new AuthToken("signed-token", DateTime.UtcNow.AddMinutes(60)));

        var handler = MakeHandler(db, identityService, jwtTokenGenerator);

        var result = await handler.Handle(new SelectBusinessCommand("good-token", business.Id), CancellationToken.None);

        result.AccessToken.Should().Be("signed-token");
        result.BusinessId.Should().Be(business.Id);
        result.Role.Should().Be("Owner");
        result.RequiresBusinessSelection.Should().BeFalse();
    }
}
