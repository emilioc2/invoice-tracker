using FluentAssertions;
using InvoiceTracker.Application.Auth.Commands.SwitchBusiness;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;
using NSubstitute;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// Unit tests for <see cref="SwitchBusinessCommandHandler"/>: the caller's identity is resolved
/// server-side from <see cref="ICurrentUserService"/>, and the requested BusinessId is only ever
/// used to look up a membership for that identity — never trusted outright.
/// </summary>
public class SwitchBusinessCommandHandlerTests
{
    private static SwitchBusinessCommandHandler MakeHandler(
        TestApplicationDbContext db,
        IIdentityService identityService,
        ICurrentUserService currentUserService,
        IJwtTokenGenerator jwtTokenGenerator) =>
        new(db, identityService, currentUserService, jwtTokenGenerator);

    [Fact]
    public async Task NoActiveMembershipInTheRequestedBusiness_ThrowsNotFound()
    {
        using var db = TestApplicationDbContext.Create();
        var callerUserId = Guid.NewGuid();
        var ownedBusiness = new Business { Id = Guid.NewGuid(), Name = "Owned", Email = "owned@example.com" };
        db.Businesses.Add(ownedBusiness);
        db.UserBusinessMemberships.Add(new UserBusinessMembership { Id = Guid.NewGuid(), UserId = callerUserId, BusinessId = ownedBusiness.Id, Role = UserRole.Owner });
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        var currentUserService = new TestCurrentUserService(ownedBusiness.Id, callerUserId);

        var handler = MakeHandler(db, identityService, currentUserService, jwtTokenGenerator);

        var act = () => handler.Handle(new SwitchBusinessCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeactivatedMembershipInTheRequestedBusiness_ThrowsNotFound()
    {
        using var db = TestApplicationDbContext.Create();
        var callerUserId = Guid.NewGuid();
        var business = new Business { Id = Guid.NewGuid(), Name = "Biz", Email = "biz@example.com" };
        db.Businesses.Add(business);
        var membership = new UserBusinessMembership { Id = Guid.NewGuid(), UserId = callerUserId, BusinessId = business.Id, Role = UserRole.Staff };
        membership.TransitionTo(UserStatus.Deactivated);
        db.UserBusinessMemberships.Add(membership);
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        var currentUserService = new TestCurrentUserService(business.Id, callerUserId);

        var handler = MakeHandler(db, identityService, currentUserService, jwtTokenGenerator);

        var act = () => handler.Handle(new SwitchBusinessCommand(business.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ActiveMembershipInTheRequestedBusiness_ReturnsATokenScopedToItsRole()
    {
        using var db = TestApplicationDbContext.Create();
        var callerUserId = Guid.NewGuid();
        var currentBusiness = new Business { Id = Guid.NewGuid(), Name = "Current", Email = "current@example.com" };
        var targetBusiness = new Business { Id = Guid.NewGuid(), Name = "Target", Email = "target@example.com" };
        db.Businesses.AddRange(currentBusiness, targetBusiness);
        db.UserBusinessMemberships.AddRange(
            new UserBusinessMembership { Id = Guid.NewGuid(), UserId = callerUserId, BusinessId = currentBusiness.Id, Role = UserRole.Owner },
            new UserBusinessMembership { Id = Guid.NewGuid(), UserId = callerUserId, BusinessId = targetBusiness.Id, Role = UserRole.Staff });
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        identityService.FindIdentityByIdAsync(callerUserId).Returns(new AuthenticatedIdentityDto(callerUserId, "person@example.com", false));

        var jwtTokenGenerator = Substitute.For<IJwtTokenGenerator>();
        jwtTokenGenerator.GenerateToken(callerUserId, targetBusiness.Id, "Staff", "person@example.com")
            .Returns(new AuthToken("signed-token", DateTime.UtcNow.AddMinutes(60)));

        var currentUserService = new TestCurrentUserService(currentBusiness.Id, callerUserId);

        var handler = MakeHandler(db, identityService, currentUserService, jwtTokenGenerator);

        var result = await handler.Handle(new SwitchBusinessCommand(targetBusiness.Id), CancellationToken.None);

        result.AccessToken.Should().Be("signed-token");
        result.BusinessId.Should().Be(targetBusiness.Id);
        result.Role.Should().Be("Staff");
    }
}
