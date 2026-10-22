using FluentAssertions;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Invoices.Commands.SendInvoice;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Status-transition unit tests for <see cref="SendInvoiceCommandHandler"/> (Draft -> Unpaid) per
/// architecture.md's test plan: the one valid starting status succeeds, every other status is
/// rejected. Also covers the handler's new email-delivery side effect (per the "Invoice
/// send/resend email delivery" section): the status transition must succeed/persist independent of
/// whether IEmailSender succeeds or throws, and the resulting Notification must always carry
/// NotificationType = InvoiceSent. Exercised against EF Core's in-memory provider via
/// <see cref="TestApplicationDbContext"/>, with a mocked IInvoicePdfService/IEmailSender (no real
/// PDF rendering or email transport).
/// </summary>
public class SendInvoiceCommandHandlerTests
{
    private static IInvoicePdfService MakePdfService()
    {
        var pdfService = Substitute.For<IInvoicePdfService>();
        pdfService
            .GenerateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new InvoicePdfGenerationResult(new byte[] { 1, 2, 3 }, "INV-0001"));
        return pdfService;
    }

    private static SendInvoiceCommandHandler CreateHandler(
        TestApplicationDbContext db, Guid businessId, IEmailSender emailSender, IInvoicePdfService? pdfService = null) =>
        new(
            db,
            new TestCurrentUserService(businessId),
            pdfService ?? MakePdfService(),
            emailSender,
            NullLogger<SendInvoiceCommandHandler>.Instance);

    [Fact]
    public async Task DraftInvoice_IsSentSuccessfully_BecomesUnpaid_AndSetsIssueDate()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Draft);
        invoice.IssueDate.Should().BeNull();

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IEmailSender>();
        var handler = CreateHandler(db, business.Id, emailSender);
        var result = await handler.Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

        result.Invoice.Status.Should().Be(nameof(InvoiceStatus.Unpaid));
        result.Invoice.IssueDate.Should().NotBeNull();
        result.EmailDelivery.Status.Should().Be(nameof(NotificationStatus.Sent));

        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.Status.Should().Be(InvoiceStatus.Unpaid);
        reloaded.IssueDate.Should().NotBeNull();
    }

    [Fact]
    public async Task EmailSenderThrows_StatusTransitionStillSucceeds_NotificationRecordedFailed()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Draft);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IEmailSender>();
        emailSender
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Formspree endpoint returned 404"));

        var handler = CreateHandler(db, business.Id, emailSender);
        var result = await handler.Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

        // The transition itself must succeed/persist regardless of the email outcome.
        result.Invoice.Status.Should().Be(nameof(InvoiceStatus.Unpaid));
        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.Status.Should().Be(InvoiceStatus.Unpaid);
        reloaded.IssueDate.Should().NotBeNull();

        result.EmailDelivery.Status.Should().Be(nameof(NotificationStatus.Failed));

        var notification = db.Notifications.Single(n => n.Id == result.EmailDelivery.NotificationId);
        notification.Status.Should().Be(NotificationStatus.Failed);
        notification.NotificationType.Should().Be(NotificationType.InvoiceSent);
    }

    [Fact]
    public async Task EmailSenderSucceeds_NotificationRecordedSent_WithInvoiceSentType()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Draft);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IEmailSender>();
        var handler = CreateHandler(db, business.Id, emailSender);
        var result = await handler.Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

        var notification = db.Notifications.Single(n => n.Id == result.EmailDelivery.NotificationId);
        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.NotificationType.Should().Be(NotificationType.InvoiceSent);
        notification.SentAt.Should().NotBeNull();

        await emailSender.Received(1).SendAsync(
            client.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(InvoiceStatus.Unpaid)]
    [InlineData(InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task NonDraftInvoice_CannotBeSent_ThrowsBadRequest(InvoiceStatus startingStatus)
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, startingStatus);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IEmailSender>();
        var handler = CreateHandler(db, business.Id, emailSender);

        var act = () => handler.Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>();

        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.Status.Should().Be(startingStatus);

        // No email-related side effects should run if the transition itself was rejected.
        await emailSender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>());
        (await db.Notifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InvoiceFromAnotherBusiness_IsTreatedAsNotFound()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var otherBusiness = InvoiceSeeding.MakeBusiness("Other Co", "other@example.com");
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Draft);

        db.Businesses.AddRange(business, otherBusiness);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IEmailSender>();
        var handler = CreateHandler(db, otherBusiness.Id, emailSender);

        var act = () => handler.Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
