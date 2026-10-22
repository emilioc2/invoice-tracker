using System.Text.Json;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Api.BackgroundServices;

/// <summary>
/// Periodic scan that flags newly-overdue invoices and writes the matching outbox entry, in a
/// single database transaction (the transactional outbox pattern — see architecture.md's "Kafka:
/// overdue-notification pipeline"). Runs across ALL businesses: this is a system-level background
/// job, not a per-request handler, so (unlike every MediatR handler in this project) it
/// deliberately does not scope by <c>ICurrentUserService.BusinessId</c> — there is no caller.
///
/// Communicates with <c>OutboxRelayService</c> only via the OutboxMessages table, never by direct
/// method call, so the two could be split into separate processes later with no code change
/// beyond deployment config.
/// </summary>
public class OverdueInvoiceScannerService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OverdueInvoiceScannerService> _logger;
    private readonly TimeSpan _interval;

    public OverdueInvoiceScannerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OverdueInvoiceScannerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var minutes = configuration.GetValue<int?>("Kafka:OverdueScanIntervalMinutes") ?? 15;
        _interval = TimeSpan.FromMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Overdue invoice scan failed; will retry next tick.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var now = DateTime.UtcNow;

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);

        var overdueInvoices = await dbContext.Invoices
            .Where(i => i.Status == InvoiceStatus.Unpaid && i.DueDate < now && i.ArchivedAt == null)
            .ToListAsync(cancellationToken);

        if (overdueInvoices.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var clientIds = overdueInvoices.Select(i => i.ClientId).Distinct().ToList();
        var clientsById = await dbContext.Clients
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        foreach (var invoice in overdueInvoices)
        {
            // Legality is centralized in Invoice.TransitionTo (see architecture.md's "Domain
            // invariants: centralized status transitions") — this guards the only legal starting
            // status for this transition even though the query above already filters to Unpaid.
            invoice.TransitionTo(InvoiceStatus.Overdue);
            invoice.LastModifiedDate = now;

            if (!clientsById.TryGetValue(invoice.ClientId, out var client))
            {
                // Shouldn't happen (ClientId is a required FK), but don't let one bad row sink the
                // whole batch's transaction.
                _logger.LogWarning("Invoice {InvoiceId} references missing Client {ClientId}; skipping its outbox entry.", invoice.Id, invoice.ClientId);
                continue;
            }

            var overdueEvent = new InvoiceOverdueEvent(
                Guid.NewGuid(),
                "InvoiceOverdue",
                now,
                invoice.Id,
                invoice.BusinessId,
                invoice.InvoiceNumber,
                invoice.ClientId,
                client.Name,
                client.Email,
                invoice.DueDate,
                invoice.Total);

            var payload = JsonSerializer.Serialize(overdueEvent, JsonOptions);

            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "InvoiceOverdue",
                Payload = payload,
                OccurredAt = now,
                ProcessedAt = null
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Flagged {Count} invoice(s) as Overdue and queued their outbox events.", overdueInvoices.Count);
    }
}
