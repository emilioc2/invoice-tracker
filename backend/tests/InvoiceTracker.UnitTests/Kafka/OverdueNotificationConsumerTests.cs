using System.Text.Json;
using FluentAssertions;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Events;
using InvoiceTracker.Infrastructure.Messaging;
using InvoiceTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InvoiceTracker.UnitTests.Kafka;

/// <summary>
/// Unit tests for <see cref="OverdueNotificationConsumer"/> per architecture.md's test plan — the
/// three cases that prove the crash-safe idempotency design actually holds:
/// 1. No Notification row exists -> inserts Pending, sends, updates to Sent.
/// 2. Row exists as Sent -> skips, never calls IEmailSender (ordinary at-least-once redelivery).
/// 3. Row exists as Pending -> retries the send using the existing row (this is what proves a
///    crash between "recorded" and "sent" can only ever leave one row behind to retry, never a
///    second Sent row for the same SourceEventId).
///
/// Exercised against a mocked <see cref="IEmailSender"/> (no real Resend/HTTP call) and EF
/// Core's in-memory provider (no real Kafka broker, no real Postgres) via the private
/// <c>ProcessMessageAsync</c> method directly — this consumer's own Kafka consume loop is
/// infrastructure plumbing around that method, not business logic to re-test here.
/// </summary>
public class OverdueNotificationConsumerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static OverdueNotificationConsumer CreateConsumer(IServiceScopeFactory scopeFactory) =>
        new(scopeFactory, Options.Create(new KafkaSettings()), NullLogger<OverdueNotificationConsumer>.Instance);

    /// <summary>
    /// Captures the bool <c>ProcessMessageAsync</c> returns to its caller (the consume loop in
    /// <c>ConsumeLoop</c>) — that return value is the *only* thing the loop uses to decide whether
    /// to call <c>consumer.Commit(result)</c>: <c>true</c> commits (advancing the offset so Kafka
    /// never redelivers it), <c>false</c> leaves the offset uncommitted so Kafka redelivers the
    /// message. The loop itself isn't unit-testable here (it builds its own real
    /// <c>Confluent.Kafka</c> <c>IConsumer</c> internally, with no injected/mockable seam, and this
    /// project's test plan excludes a real Kafka broker from the suite) — so asserting on this
    /// return value for every code path is the correct substitute: it directly tests the specific
    /// decision this consumer's crash-safety fix relies on.
    /// </summary>
    private static Task<bool> ProcessMessageAsyncWithResult(OverdueNotificationConsumer consumer, string payload) =>
        PrivateMethodInvoker.InvokeAsync<bool>(consumer, "ProcessMessageAsync", payload, CancellationToken.None);

    private static InvoiceOverdueEvent MakeEvent(Guid eventId) => new(
        eventId,
        "InvoiceOverdue",
        DateTime.UtcNow,
        Guid.NewGuid(),
        Guid.NewGuid(),
        "INV-0001",
        Guid.NewGuid(),
        "Acme Co",
        "billing@acme.example",
        DateTime.UtcNow.AddDays(-1),
        1500.00m);

    /// <summary>
    /// Builds an <see cref="IInvoicePdfService"/> substitute whose <c>GenerateAsync</c> always
    /// succeeds with a canned PDF — the default for every test except the one specifically
    /// covering PDF generation failure.
    /// </summary>
    private static IInvoicePdfService MakeWorkingPdfService(string invoiceNumber = "INV-0001")
    {
        var pdfService = Substitute.For<IInvoicePdfService>();
        pdfService
            .GenerateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new InvoicePdfGenerationResult(new byte[] { 1, 2, 3 }, invoiceNumber));
        return pdfService;
    }

    [Fact]
    public async Task NoExistingNotification_InsertsPendingRow_GeneratesPdf_SendsEmail_ThenUpdatesToSent()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var pdfService = MakeWorkingPdfService();

        IReadOnlyList<EmailAttachment>? capturedAttachments = null;
        string? capturedSubject = null;
        string? capturedBody = null;
        emailSender
            .When(x => x.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                capturedSubject = call.ArgAt<string>(1);
                capturedBody = call.ArgAt<string>(2);
                capturedAttachments = call.ArgAt<IReadOnlyList<EmailAttachment>>(3);
            });

        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(services =>
        {
            services.AddSingleton(emailSender);
            services.AddSingleton(pdfService);
        });

        var eventId = Guid.NewGuid();
        var @event = MakeEvent(eventId);
        var payload = JsonSerializer.Serialize(@event, JsonOptions);

        var consumer = CreateConsumer(scopeFactory);
        var processedSuccessfully = await ProcessMessageAsyncWithResult(consumer, payload);

        // The PDF is generated using the event's own invoiceId/businessId — no separate lookup.
        await pdfService.Received(1).GenerateAsync(@event.InvoiceId, @event.BusinessId, Arg.Any<CancellationToken>());

        await emailSender.Received(1).SendAsync(
            @event.ClientEmail, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>());

        // Subject/body match the new letter-format overdue reminder template, not the old one-line
        // message.
        capturedSubject.Should().Be($"Invoice {@event.InvoiceNumber} is overdue");
        capturedBody.Should().Contain($"Dear {@event.ClientName},");
        capturedBody.Should().Contain($"This is a reminder that invoice {@event.InvoiceNumber} is now overdue.");
        capturedBody.Should().Contain($"Amount due: {CurrencyFormatter.Format(@event.TotalAmount)}");
        capturedBody.Should().Contain($"Due date: {@event.DueDate:yyyy-MM-dd}");
        capturedBody.Should().Contain("Please arrange payment as soon as possible. A copy of the invoice is attached for your reference.");
        capturedBody.Should().Contain("Thank you.");

        // The PDF generated above is what gets passed through as the email's attachment.
        capturedAttachments.Should().ContainSingle();
        capturedAttachments![0].FileName.Should().Be("INV-0001.pdf");
        capturedAttachments[0].Content.Should().Equal(new byte[] { 1, 2, 3 });
        capturedAttachments[0].ContentType.Should().Be("application/pdf");

        // This is the value the consume loop uses to decide whether to commit the Kafka offset:
        // true here means it would commit, which is correct once the row is Sent.
        processedSuccessfully.Should().BeTrue();

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var notifications = await db.Notifications.Where(n => n.SourceEventId == eventId).ToListAsync();

        notifications.Should().ContainSingle();
        var notification = notifications[0];
        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.SentAt.Should().NotBeNull();
        notification.InvoiceId.Should().Be(@event.InvoiceId);
        notification.Channel.Should().Be("Email");
    }

    [Fact]
    public async Task ExistingSentNotification_IsSkipped_AndEmailSenderIsNeverCalled()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var pdfService = MakeWorkingPdfService();
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(services =>
        {
            services.AddSingleton(emailSender);
            services.AddSingleton(pdfService);
        });

        var eventId = Guid.NewGuid();
        var @event = MakeEvent(eventId);
        var payload = JsonSerializer.Serialize(@event, JsonOptions);

        var existingNotificationId = Guid.NewGuid();
        var originalSentAt = DateTime.UtcNow.AddHours(-1);
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.Notifications.Add(new Notification
            {
                Id = existingNotificationId,
                InvoiceId = @event.InvoiceId,
                Status = NotificationStatus.Sent,
                SentAt = originalSentAt,
                Channel = "Email",
                Message = "original message",
                SourceEventId = eventId
            });
            await db.SaveChangesAsync();
        }

        var consumer = CreateConsumer(scopeFactory);
        var processedSuccessfully = await ProcessMessageAsyncWithResult(consumer, payload);

        await emailSender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>());
        await pdfService.DidNotReceive().GenerateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        // The redelivery-skip path must still signal "safe to commit" — otherwise Kafka would
        // redeliver this message forever even though it was already fully processed.
        processedSuccessfully.Should().BeTrue();

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var notifications = await verifyDb.Notifications.Where(n => n.SourceEventId == eventId).ToListAsync();

        notifications.Should().ContainSingle();
        notifications[0].Id.Should().Be(existingNotificationId);
        notifications[0].Status.Should().Be(NotificationStatus.Sent);
        notifications[0].SentAt.Should().Be(originalSentAt);
    }

    [Fact]
    public async Task ExistingPendingNotification_RetriesSend_UsingTheExistingRow_NoDuplicateInserted()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var pdfService = MakeWorkingPdfService();
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(services =>
        {
            services.AddSingleton(emailSender);
            services.AddSingleton(pdfService);
        });

        var eventId = Guid.NewGuid();
        var @event = MakeEvent(eventId);
        var payload = JsonSerializer.Serialize(@event, JsonOptions);

        var existingNotificationId = Guid.NewGuid();
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.Notifications.Add(new Notification
            {
                Id = existingNotificationId,
                InvoiceId = @event.InvoiceId,
                Status = NotificationStatus.Pending,
                SentAt = null,
                Channel = "Email",
                Message = "a previous attempt crashed before this could be marked Sent",
                SourceEventId = eventId
            });
            await db.SaveChangesAsync();
        }

        var consumer = CreateConsumer(scopeFactory);
        var processedSuccessfully = await ProcessMessageAsyncWithResult(consumer, payload);

        // Proves crash-safety: the retry reuses the existing Pending row rather than inserting a
        // second one, so a crash between "recorded" and "sent" can never produce two Sent rows for
        // the same SourceEventId.
        await emailSender.Received(1).SendAsync(
            @event.ClientEmail, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>());

        processedSuccessfully.Should().BeTrue();

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var notifications = await verifyDb.Notifications.Where(n => n.SourceEventId == eventId).ToListAsync();

        notifications.Should().ContainSingle();
        notifications[0].Id.Should().Be(existingNotificationId);
        notifications[0].Status.Should().Be(NotificationStatus.Sent);
        notifications[0].SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task EmailSendThrows_MarksNotificationFailed_NotSent()
    {
        var emailSender = Substitute.For<IEmailSender>();
        emailSender
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Resend endpoint returned 404"));
        var pdfService = MakeWorkingPdfService();

        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(services =>
        {
            services.AddSingleton(emailSender);
            services.AddSingleton(pdfService);
        });

        var eventId = Guid.NewGuid();
        var @event = MakeEvent(eventId);
        var payload = JsonSerializer.Serialize(@event, JsonOptions);

        var consumer = CreateConsumer(scopeFactory);
        var processedSuccessfully = await ProcessMessageAsyncWithResult(consumer, payload);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var notification = await db.Notifications.SingleAsync(n => n.SourceEventId == eventId);

        notification.Status.Should().Be(NotificationStatus.Failed);
        notification.SentAt.Should().BeNull();

        // This is the specific assertion that proves the fix: a Failed send must signal "do not
        // commit" so the consume loop leaves the Kafka offset uncommitted and the message gets
        // redelivered. Before the fix (EnableAutoCommit = true, no return-value-gated Commit call)
        // this distinction didn't exist and the offset would have advanced regardless of outcome.
        processedSuccessfully.Should().BeFalse();
    }

    [Fact]
    public async Task PdfGenerationThrows_MarksNotificationFailed_NotLeftPending()
    {
        // Per architecture.md's updated OverdueNotificationConsumer section: if PDF generation
        // itself throws (e.g. the invoice can no longer be loaded), that must land the Notification
        // row as Failed rather than leaving it stuck Pending indefinitely — the same crash-safe
        // outcome as a send failure, just triggered by the earlier step.
        var emailSender = Substitute.For<IEmailSender>();
        var pdfService = Substitute.For<IInvoicePdfService>();
        pdfService
            .GenerateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotFoundException("Invoice not found."));

        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(services =>
        {
            services.AddSingleton(emailSender);
            services.AddSingleton(pdfService);
        });

        var eventId = Guid.NewGuid();
        var @event = MakeEvent(eventId);
        var payload = JsonSerializer.Serialize(@event, JsonOptions);

        var consumer = CreateConsumer(scopeFactory);
        var processedSuccessfully = await ProcessMessageAsyncWithResult(consumer, payload);

        // PDF generation failing must never attempt the send at all.
        await emailSender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>());

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var notification = await db.Notifications.SingleAsync(n => n.SourceEventId == eventId);

        notification.Status.Should().Be(NotificationStatus.Failed);
        notification.SentAt.Should().BeNull();

        // Same reasoning as the send-failure case: a Failed row means the offset must be left
        // uncommitted so Kafka redelivery remains the retry path.
        processedSuccessfully.Should().BeFalse();
    }

    [Fact]
    public async Task UnparseablePayload_IsSkipped_AndSignalsSafeToCommit()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(services => services.AddSingleton(emailSender));

        var consumer = CreateConsumer(scopeFactory);

        // Valid JSON (the "null" literal) that deserializes to a null InvoiceOverdueEvent -> this
        // is the specific "unparseable payload" branch ProcessMessageAsync handles itself (genuinely
        // malformed/non-JSON input instead throws and is handled by the consume loop's own outer
        // catch block, which is a separate code path not covered by this test).
        var processedSuccessfully = await ProcessMessageAsyncWithResult(consumer, "null");

        await emailSender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EmailAttachment>>(), Arg.Any<CancellationToken>());

        // A malformed message can never be turned into a valid Notification row no matter how many
        // times it's redelivered, so it must be treated as safe to commit (skip-and-move-on) rather
        // than retried forever.
        processedSuccessfully.Should().BeTrue();

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        (await db.Notifications.AnyAsync()).Should().BeFalse();
    }
}
