using FluentAssertions;
using InvoiceTracker.Application.Auth.Commands.ChangePassword;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.UnitTests.TestSupport;
using NSubstitute;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// Unit tests for <see cref="ChangePasswordCommandHandler"/> per architecture.md's test plan:
/// resolves the caller via <see cref="ICurrentUserService"/> (never a client-supplied id), sets
/// the new password without a current-password check, and delegates <c>MustChangePassword</c>
/// clearing to <see cref="IIdentityService.SetPasswordAsync"/> itself (Infrastructure) rather than
/// doing it in this handler — see <see cref="IIdentityService"/>'s own doc comment — so with
/// <see cref="IIdentityService"/> mocked here, this handler's own responsibility is reduced to
/// "delegate to IIdentityService.SetPasswordAsync and translate its result"; the flag-clearing
/// behavior itself is exercised end-to-end in <c>AuthTests</c> (integration tests).
/// </summary>
public class ChangePasswordCommandHandlerTests
{
    [Fact]
    public async Task IdentityServiceRejectsNewPassword_ThrowsBadRequest()
    {
        var businessId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var identityService = Substitute.For<IIdentityService>();
        identityService
            .SetPasswordAsync(userId, "bad")
            .Returns(IdentityResultDto.Failure(new[] { "Passwords must be at least 8 characters." }));

        var handler = new ChangePasswordCommandHandler(identityService, new TestCurrentUserService(businessId, userId));

        var act = () => handler.Handle(new ChangePasswordCommand("bad"), CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task ValidNewPassword_Succeeds_AndDelegatesToIdentityService_WithNoCurrentPasswordCheck()
    {
        var businessId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var identityService = Substitute.For<IIdentityService>();
        identityService
            .SetPasswordAsync(userId, "NewPassword1")
            .Returns(IdentityResultDto.Success());

        var handler = new ChangePasswordCommandHandler(identityService, new TestCurrentUserService(businessId, userId));

        await handler.Handle(new ChangePasswordCommand("NewPassword1"), CancellationToken.None);

        await identityService.Received(1).SetPasswordAsync(userId, "NewPassword1");
    }
}
