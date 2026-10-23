using FluentAssertions;
using InvoiceTracker.Application.Auth.Commands.ForgotPassword;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// Covers the bug fix mirroring <c>CreateStaffUserCommandHandler</c>'s equivalent email-send
/// hardening: per the handler's own documented contract ("Always succeeds from the caller's point
/// of view ... so the response never leaks account existence"), a thrown exception from
/// <see cref="IEmailSender"/> must be caught and logged, never rethrown — otherwise a delivery
/// failure (e.g. a live provider rejecting the recipient) would surface as a bare 500 and also
/// leak account existence (the opposite of what this handler exists to prevent).
/// </summary>
public class ForgotPasswordCommandHandlerTests
{
    private const string FrontendBaseUrl = "https://app.example.com";

    private static ForgotPasswordCommandHandler CreateHandler(
        IIdentityService identityService, IEmailSender emailSender) =>
        new(
            identityService,
            emailSender,
            Options.Create(new FrontendSettings { BaseUrl = FrontendBaseUrl }),
            NullLogger<ForgotPasswordCommandHandler>.Instance);

    private static IIdentityService MakeIdentityServiceWithMatchingAccount(string email, string token = "reset-token")
    {
        var identityService = Substitute.For<IIdentityService>();
        identityService.GeneratePasswordResetTokenAsync(email).Returns(token);
        return identityService;
    }

    [Fact]
    public async Task EmailSenderSucceeds_ReturnsUnitValue()
    {
        const string email = "owner@example.com";
        var identityService = MakeIdentityServiceWithMatchingAccount(email);
        var emailSender = Substitute.For<IEmailSender>();

        var handler = CreateHandler(identityService, emailSender);
        var result = await handler.Handle(new ForgotPasswordCommand(email), CancellationToken.None);

        result.Should().Be(Unit.Value);
        await emailSender.Received(1).SendAsync(
            email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmailSenderThrows_StillReturnsUnitValue_DoesNotPropagate()
    {
        const string email = "owner@example.com";
        var identityService = MakeIdentityServiceWithMatchingAccount(email);
        var emailSender = Substitute.For<IEmailSender>();
        emailSender
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Resend rejected recipient domain"));

        var handler = CreateHandler(identityService, emailSender);

        var act = async () => await handler.Handle(new ForgotPasswordCommand(email), CancellationToken.None);

        await act.Should().NotThrowAsync();
        var result = await handler.Handle(new ForgotPasswordCommand(email), CancellationToken.None);
        result.Should().Be(Unit.Value);
    }

    [Fact]
    public async Task EmailDoesNotMatchAnAccount_NoEmailSentAtAll_StillReturnsUnitValue()
    {
        const string email = "nobody@example.com";
        var identityService = Substitute.For<IIdentityService>();
        identityService.GeneratePasswordResetTokenAsync(email).Returns((string?)null);
        var emailSender = Substitute.For<IEmailSender>();

        var handler = CreateHandler(identityService, emailSender);
        var result = await handler.Handle(new ForgotPasswordCommand(email), CancellationToken.None);

        result.Should().Be(Unit.Value);
        await emailSender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>());
    }
}
