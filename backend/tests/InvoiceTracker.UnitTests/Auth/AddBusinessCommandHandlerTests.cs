using FluentAssertions;
using InvoiceTracker.Application.Auth.Commands.AddBusiness;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// Unit tests for <see cref="AddBusinessCommandHandler"/>: the one new, genuinely pure piece of
/// logic here (beyond what the integration tests already cover end-to-end) is that the created
/// membership's own Id is distinct from the caller's UserId — the "shared id" shape only applies to
/// a brand-new identity's first membership (Register/CreateStaffUser), never here, since this path
/// always attaches a second-or-later membership to an identity that already exists — plus the
/// not-found guard for a caller identity that can't be resolved.
/// </summary>
public class AddBusinessCommandHandlerTests
{
    [Fact]
    public async Task CallerIdentityCannotBeResolved_ThrowsNotFound_AndCreatesNothing()
    {
        using var db = TestApplicationDbContext.Create();
        var callerUserId = Guid.NewGuid();

        var identityService = Substitute.For<IIdentityService>();
        identityService.FindIdentityByIdAsync(callerUserId).Returns((AuthenticatedIdentityDto?)null);
        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        var currentUserService = new TestCurrentUserService(Guid.NewGuid(), callerUserId);

        var handler = new AddBusinessCommandHandler(db, identityService, currentUserService, jwtTokenGenerator);

        var act = () => handler.Handle(new AddBusinessCommand("New Co", "new@example.com"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        (await db.Businesses.CountAsync()).Should().Be(0);
        (await db.UserBusinessMemberships.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ValidCaller_CreatesANewBusinessAndOwnerMembership_WithItsOwnDistinctId_AndReturnsATokenScopedToIt()
    {
        using var db = TestApplicationDbContext.Create();
        var callerUserId = Guid.NewGuid();

        var identityService = Substitute.For<IIdentityService>();
        identityService.FindIdentityByIdAsync(callerUserId).Returns(new AuthenticatedIdentityDto(callerUserId, "owner@example.com", false));

        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        AuthToken? capturedToken = null;
        jwtTokenGenerator
            .GenerateToken(callerUserId, Arg.Any<Guid>(), "Owner", "owner@example.com")
            .Returns(ci =>
            {
                capturedToken = new AuthToken("signed-token", DateTime.UtcNow.AddMinutes(60));
                return capturedToken;
            });

        var currentUserService = new TestCurrentUserService(Guid.NewGuid(), callerUserId);

        var handler = new AddBusinessCommandHandler(db, identityService, currentUserService, jwtTokenGenerator);

        var result = await handler.Handle(new AddBusinessCommand("New Co", "new@example.com"), CancellationToken.None);

        var business = await db.Businesses.SingleAsync();
        business.Name.Should().Be("New Co");
        business.Email.Should().Be("new@example.com");

        var membership = await db.UserBusinessMemberships.SingleAsync();
        membership.UserId.Should().Be(callerUserId);
        membership.BusinessId.Should().Be(business.Id);
        membership.Role.Should().Be(UserRole.Owner);
        membership.Status.Should().Be(UserStatus.Active);
        // Distinct from UserId — unlike Register/CreateStaffUser's brand-new-identity "shared id"
        // shape, this membership's own Id is a fresh Guid since the identity already exists.
        membership.Id.Should().NotBe(membership.UserId);

        result.AccessToken.Should().Be("signed-token");
        result.BusinessId.Should().Be(business.Id);
        result.Role.Should().Be("Owner");
    }
}
