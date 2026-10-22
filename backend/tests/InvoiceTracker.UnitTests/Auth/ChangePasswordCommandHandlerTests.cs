using FluentAssertions;
using InvoiceTracker.Application.Auth.Commands.ChangePassword;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;
using NSubstitute;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// Unit tests for <see cref="ChangePasswordCommandHandler"/> per architecture.md's test plan:
/// a wrong current password is rejected with no state change, a correct current password +
/// valid new password succeeds and clears <c>User.MustChangePassword</c> to false. Exercised
/// against EF Core's in-memory provider via <see cref="TestApplicationDbContext"/> (for the
/// Domain User row) with a mocked <see cref="IIdentityService"/> (no real Identity/UserManager).
/// </summary>
public class ChangePasswordCommandHandlerTests
{
    private static User MakeUser(Guid businessId, bool mustChangePassword) => new()
    {
        Id = Guid.NewGuid(),
        BusinessId = businessId,
        Email = "staff@example.com",
        Role = UserRole.Staff,
        IsActive = true,
        MustChangePassword = mustChangePassword
    };

    [Fact]
    public async Task WrongCurrentPassword_ThrowsBadRequest_AndMustChangePasswordIsUnchanged()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var user = MakeUser(business.Id, mustChangePassword: true);

        db.Businesses.Add(business);
        db.BusinessUsers.Add(user);
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        identityService
            .ChangePasswordAsync(user.Id, "WrongPassword1", "NewPassword1")
            .Returns(IdentityResultDto.Failure(new[] { "Current password is incorrect." }));

        var handler = new ChangePasswordCommandHandler(db, identityService, new TestCurrentUserService(business.Id, user.Id));

        var act = () => handler.Handle(new ChangePasswordCommand("WrongPassword1", "NewPassword1"), CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>();

        var reloaded = db.BusinessUsers.Single(u => u.Id == user.Id);
        reloaded.MustChangePassword.Should().BeTrue("a rejected change must leave state untouched");
    }

    [Fact]
    public async Task CorrectCurrentPassword_Succeeds_AndClearsMustChangePassword()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var user = MakeUser(business.Id, mustChangePassword: true);

        db.Businesses.Add(business);
        db.BusinessUsers.Add(user);
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        identityService
            .ChangePasswordAsync(user.Id, "TempPassword1", "NewPassword1")
            .Returns(IdentityResultDto.Success());

        var handler = new ChangePasswordCommandHandler(db, identityService, new TestCurrentUserService(business.Id, user.Id));

        await handler.Handle(new ChangePasswordCommand("TempPassword1", "NewPassword1"), CancellationToken.None);

        var reloaded = db.BusinessUsers.Single(u => u.Id == user.Id);
        reloaded.MustChangePassword.Should().BeFalse();

        await identityService.Received(1).ChangePasswordAsync(user.Id, "TempPassword1", "NewPassword1");
    }

    [Fact]
    public async Task CorrectCurrentPassword_ForUserWithoutForcedChangeFlag_StillSucceeds()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var user = MakeUser(business.Id, mustChangePassword: false);

        db.Businesses.Add(business);
        db.BusinessUsers.Add(user);
        await db.SaveChangesAsync();

        var identityService = Substitute.For<IIdentityService>();
        identityService
            .ChangePasswordAsync(user.Id, "CurrentPassword1", "NewPassword1")
            .Returns(IdentityResultDto.Success());

        var handler = new ChangePasswordCommandHandler(db, identityService, new TestCurrentUserService(business.Id, user.Id));

        await handler.Handle(new ChangePasswordCommand("CurrentPassword1", "NewPassword1"), CancellationToken.None);

        var reloaded = db.BusinessUsers.Single(u => u.Id == user.Id);
        reloaded.MustChangePassword.Should().BeFalse();
    }
}
