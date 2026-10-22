using FluentAssertions;
using InvoiceTracker.Application.Auth.Commands.ChangePassword;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.UnitTests.TestSupport;
using NSubstitute;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// Unit tests for <see cref="ChangePasswordCommandHandler"/> per architecture.md's test plan: a
/// wrong current password is rejected with no state change, a correct current password + valid new
/// password succeeds. <c>MustChangePassword</c> now lives on <c>ApplicationUser</c> (the identity),
/// cleared by <see cref="IIdentityService.ChangePasswordAsync"/> itself (Infrastructure) rather than
/// by this handler — see <see cref="IIdentityService"/>'s own doc comment — so with
/// <see cref="IIdentityService"/> mocked here, this handler's own responsibility is reduced to
/// "delegate to IIdentityService.ChangePasswordAsync and translate its result"; the flag-clearing
/// behavior itself is exercised end-to-end in <c>AuthTests</c> (integration tests).
/// </summary>
public class ChangePasswordCommandHandlerTests
{
    [Fact]
    public async Task WrongCurrentPassword_ThrowsBadRequest()
    {
        var businessId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var identityService = Substitute.For<IIdentityService>();
        identityService
            .ChangePasswordAsync(userId, "WrongPassword1", "NewPassword1")
            .Returns(IdentityResultDto.Failure(new[] { "Current password is incorrect." }));

        var handler = new ChangePasswordCommandHandler(identityService, new TestCurrentUserService(businessId, userId));

        var act = () => handler.Handle(new ChangePasswordCommand("WrongPassword1", "NewPassword1"), CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task CorrectCurrentPassword_Succeeds_AndDelegatesToIdentityService()
    {
        var businessId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var identityService = Substitute.For<IIdentityService>();
        identityService
            .ChangePasswordAsync(userId, "TempPassword1", "NewPassword1")
            .Returns(IdentityResultDto.Success());

        var handler = new ChangePasswordCommandHandler(identityService, new TestCurrentUserService(businessId, userId));

        await handler.Handle(new ChangePasswordCommand("TempPassword1", "NewPassword1"), CancellationToken.None);

        await identityService.Received(1).ChangePasswordAsync(userId, "TempPassword1", "NewPassword1");
    }
}
