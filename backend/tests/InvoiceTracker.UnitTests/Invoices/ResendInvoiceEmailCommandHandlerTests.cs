using FluentAssertions;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Invoices.Commands.ResendInvoiceEmail;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Unit tests for <see cref="ResendInvoiceEmailCommandHandler"/> per architecture.md's test plan:
/// rejects a Draft invoice with no side effects; succeeds for every other status without mutating
/// invoice status; records a new Notification with a fresh SourceEventId each call.
/// </summary>
public class ResendInvoiceEmailCommandHandlerTests
{
    private static IInvoicePdfService MakePdfService(string invoiceNumber = "INV-0001")
    {
        var pdfService = Substitute.For<IInvoicePdfService>();
        pdfService
            .GenerateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new InvoicePdfGenerationResult(new byte[] { 1, 2, 3 }, invoiceNumber));
        return pdfService;
    }

    private static ResendInvoiceEmailCommandHandler CreateHandler(
        TestApplicationDbContext db, Guid businessId, IEmailSender emailSender, IInvoicePdfService? pdfService = null) =>
        new(
            db,
            new TestCurrentUserService(businessId),
            pdfService ?? MakePdfService(),
            emailSender,
            NullLogger<ResendInvoiceEmailCommandHandler>.Instance);

    [Fact]
    public async Task DraftInvoice_IsRejected_WithNoSideEffects()
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
        var pdfService = MakePdfService();
        var handler = CreateHandler(db, business.Id, emailSender, pdfService);

        var act = () => handler.Handle(new ResendInvoiceEmailCommand(invoice.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>();

        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.Status.Should().Be(InvoiceStatus.Draft);

        await pdfService.DidNotReceive().GenerateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await emailSender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>());
        (await db.Notifications.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(InvoiceStatus.Unpaid)]
    [InlineData(InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task NonDraftInvoice_ResendSucceeds_WithoutMutatingStatus(InvoiceStatus startingStatus)
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

        var result = await handler.Handle(new ResendInvoiceEmailCommand(invoice.Id), CancellationToken.None);

        result.EmailDelivery.Status.Should().Be(nameof(NotificationStatus.Sent));

        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.Status.Should().Be(startingStatus);

        var notification = db.Notifications.Single(n => n.Id == result.EmailDelivery.NotificationId);
        notification.NotificationType.Should().Be(NotificationType.InvoiceSent);
        notification.Status.Should().Be(NotificationStatus.Sent);
    }

    [Fact]
    public async Task EachCall_RecordsANewNotification_WithAFreshSourceEventId()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IEmailSender>();
        var handler = CreateHandler(db, business.Id, emailSender);

        var firstResult = await handler.Handle(new ResendInvoiceEmailCommand(invoice.Id), CancellationToken.None);
        var secondResult = await handler.Handle(new ResendInvoiceEmailCommand(invoice.Id), CancellationToken.None);

        firstResult.EmailDelivery.NotificationId.Should().NotBe(secondResult.EmailDelivery.NotificationId);

        var notifications = db.Notifications.Where(n => n.InvoiceId == invoice.Id).ToList();
        notifications.Should().HaveCount(2);
        notifications.Select(n => n.SourceEventId).Distinct().Should().HaveCount(2);
        notifications.Should().OnlyContain(n => n.NotificationType == NotificationType.InvoiceSent);
    }

    [Fact]
    public async Task EmailSenderThrows_NotificationRecordedFailed_ResendResultStillReturned()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IEmailSender>();
        emailSender
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Formspree endpoint returned 404"));

        var handler = CreateHandler(db, business.Id, emailSender);

        var result = await handler.Handle(new ResendInvoiceEmailCommand(invoice.Id), CancellationToken.None);

        result.EmailDelivery.Status.Should().Be(nameof(NotificationStatus.Failed));
        var notification = db.Notifications.Single(n => n.Id == result.EmailDelivery.NotificationId);
        notification.Status.Should().Be(NotificationStatus.Failed);
    }

    [Fact]
    public async Task InvoiceFromAnotherBusiness_IsTreatedAsNotFound()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var otherBusiness = InvoiceSeeding.MakeBusiness("Other Co", "other@example.com");
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid);

        db.Businesses.AddRange(business, otherBusiness);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var emailSender = Substitute.For<IEmailSender>();
        var handler = CreateHandler(db, otherBusiness.Id, emailSender);

        var act = () => handler.Handle(new ResendInvoiceEmailCommand(invoice.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
