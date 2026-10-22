using FluentAssertions;
using InvoiceTracker.Application.Auth.Commands.Login;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;
using NSubstitute;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// Unit tests for <see cref="LoginCommandHandler"/>'s branching logic per requirements.md's
/// "Multi-business membership": zero active memberships fails identically to a bad password,
/// exactly one active membership issues a normal token (unchanged shape), and more than one active
/// membership returns a preauth token + membership list instead of a usable access token. Mocks
/// <see cref="IIdentityService"/>/<see cref="IJwtTokenGenerator"/> and uses EF Core's in-memory
/// provider for the membership/business lookups, per architecture.md's test-plan guidance.
/// </summary>
public class LoginCommandHandlerTests
{
    private static LoginCommandHandler MakeHandler(
        TestApplicationDbContext db, IIdentityService identityService, IJwtTokenGenerator jwtTokenGenerator) =>
        new(db, identityService, jwtTokenGenerator);

    [Fact]
    public async Task InvalidCredentials_ThrowsAuthenticationFailed()
    {
        using var db = TestApplicationDbContext.Create();
        var identityService = Substitute.For<IIdentityService>();
        identityService.ValidateCredentialsAsync("nobody@example.com", "whatever").Returns((AuthenticatedIdentityDto?)null);
        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();

        var handler = MakeHandler(db, identityService, jwtTokenGenerator);

        var act = () => handler.Handle(new LoginCommand("nobody@example.com", "whatever"), CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationFailedException>();
    }

    [Fact]
    public async Task CredentialsValid_ButZeroActiveMemberships_ThrowsAuthenticationFailed_SameAsBadPassword()
    {
        using var db = TestApplicationDbContext.Create();
        var userId = Guid.NewGuid();
        var identityService = Substitute.For<IIdentityService>();
        identityService.ValidateCredentialsAsync("ghost@example.com", "Password1")
            .Returns(new AuthenticatedIdentityDto(userId, "ghost@example.com", false));
        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();

        var handler = MakeHandler(db, identityService, jwtTokenGenerator);

        var act = () => handler.Handle(new LoginCommand("ghost@example.com", "Password1"), CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationFailedException>();
        jwtTokenGenerator.DidNotReceive().GenerateToken(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>());
        jwtTokenGenerator.DidNotReceive().GeneratePreAuthToken(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ExactlyOneActiveMembership_ReturnsNormalToken_UnchangedShape()
    {
        using var db = TestApplicationDbContext.Create();
        var userId = Guid.NewGuid();
        var business = new Business { Id = Guid.NewGuid(), Name = "Acme", Email = "acme@example.com" };
        db.Businesses.Add(business);
        db.UserBusinessMemberships.Add(new UserBusinessMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BusinessId = business.Id,
            Role = UserRole.Owner
        });
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        identityService.ValidateCredentialsAsync("owner@example.com", "Password1")
            .Returns(new AuthenticatedIdentityDto(userId, "owner@example.com", false));

        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        var expiresAt = DateTime.UtcNow.AddMinutes(60);
        jwtTokenGenerator.GenerateToken(userId, business.Id, "Owner", "owner@example.com")
            .Returns(new AuthToken("signed-token", expiresAt));

        var handler = MakeHandler(db, identityService, jwtTokenGenerator);

        var result = await handler.Handle(new LoginCommand("owner@example.com", "Password1"), CancellationToken.None);

        result.RequiresBusinessSelection.Should().BeFalse();
        result.AccessToken.Should().Be("signed-token");
        result.PreAuthToken.Should().BeNull();
        result.Memberships.Should().BeNull();
        result.BusinessId.Should().Be(business.Id);
        result.Role.Should().Be("Owner");
        result.MustChangePassword.Should().BeFalse();
        jwtTokenGenerator.DidNotReceive().GeneratePreAuthToken(Arg.Any<Guid>());
    }

    [Fact]
    public async Task MoreThanOneActiveMembership_ReturnsPreAuthTokenAndMembershipList_NotAUsableAccessToken()
    {
        using var db = TestApplicationDbContext.Create();
        var userId = Guid.NewGuid();
        var businessA = new Business { Id = Guid.NewGuid(), Name = "Business A", Email = "a@example.com" };
        var businessB = new Business { Id = Guid.NewGuid(), Name = "Business B", Email = "b@example.com" };
        db.Businesses.AddRange(businessA, businessB);
        db.UserBusinessMemberships.AddRange(
            new UserBusinessMembership { Id = Guid.NewGuid(), UserId = userId, BusinessId = businessA.Id, Role = UserRole.Owner },
            new UserBusinessMembership { Id = Guid.NewGuid(), UserId = userId, BusinessId = businessB.Id, Role = UserRole.Staff });
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        identityService.ValidateCredentialsAsync("multi@example.com", "Password1")
            .Returns(new AuthenticatedIdentityDto(userId, "multi@example.com", false));

        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        jwtTokenGenerator.GeneratePreAuthToken(userId).Returns(new PreAuthToken("preauth-token", DateTime.UtcNow.AddMinutes(5)));

        var handler = MakeHandler(db, identityService, jwtTokenGenerator);

        var result = await handler.Handle(new LoginCommand("multi@example.com", "Password1"), CancellationToken.None);

        result.RequiresBusinessSelection.Should().BeTrue();
        result.AccessToken.Should().BeNull();
        result.PreAuthToken.Should().Be("preauth-token");
        result.Memberships.Should().HaveCount(2);
        result.Memberships.Should().Contain(m => m.BusinessId == businessA.Id && m.BusinessName == "Business A" && m.Role == "Owner");
        result.Memberships.Should().Contain(m => m.BusinessId == businessB.Id && m.BusinessName == "Business B" && m.Role == "Staff");
        jwtTokenGenerator.DidNotReceive().GenerateToken(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task DeactivatedMembership_IsExcludedFromTheActiveCount_AndFromTheMembershipsList()
    {
        using var db = TestApplicationDbContext.Create();
        var userId = Guid.NewGuid();
        var businessA = new Business { Id = Guid.NewGuid(), Name = "Business A", Email = "a@example.com" };
        var businessB = new Business { Id = Guid.NewGuid(), Name = "Business B", Email = "b@example.com" };
        db.Businesses.AddRange(businessA, businessB);

        var deactivatedMembership = new UserBusinessMembership { Id = Guid.NewGuid(), UserId = userId, BusinessId = businessA.Id, Role = UserRole.Staff };
        deactivatedMembership.TransitionTo(UserStatus.Deactivated);
        db.UserBusinessMemberships.Add(deactivatedMembership);
        db.UserBusinessMemberships.Add(new UserBusinessMembership { Id = Guid.NewGuid(), UserId = userId, BusinessId = businessB.Id, Role = UserRole.Owner });
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        identityService.ValidateCredentialsAsync("partial@example.com", "Password1")
            .Returns(new AuthenticatedIdentityDto(userId, "partial@example.com", false));

        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        jwtTokenGenerator.GenerateToken(userId, businessB.Id, "Owner", "partial@example.com")
            .Returns(new AuthToken("signed-token", DateTime.UtcNow.AddMinutes(60)));

        var handler = MakeHandler(db, identityService, jwtTokenGenerator);

        // Only one *active* membership remains (B) — the Deactivated one in A must not count
        // toward the "more than one" branch, and must never appear anywhere in the result.
        var result = await handler.Handle(new LoginCommand("partial@example.com", "Password1"), CancellationToken.None);

        result.RequiresBusinessSelection.Should().BeFalse();
        result.BusinessId.Should().Be(businessB.Id);
    }
}
