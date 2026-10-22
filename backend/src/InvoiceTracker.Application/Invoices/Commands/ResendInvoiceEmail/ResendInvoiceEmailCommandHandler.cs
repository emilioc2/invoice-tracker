using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Persistence;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InvoiceTracker.Application.Invoices.Commands.ResendInvoiceEmail;

/// <summary>
/// Sequence (see architecture.md's "Invoice send/resend email delivery"):
/// 1. Load the invoice (BusinessId-scoped). Reject Draft invoices with 400 — nothing to resend yet.
/// 2. Load the Client and render the PDF via IInvoicePdfService.
/// 3. Dispatch the invoice-copy email via the same shared InvoiceEmailDispatcher Send uses (fresh
///    Notification row, fresh SourceEventId each call) — no status transition, no interaction with
///    the Draft-only edit lock, since resending never touches invoice content.
/// </summary>
public class ResendInvoiceEmailCommandHandler : IRequestHandler<ResendInvoiceEmailCommand, ResendInvoiceEmailResult>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IInvoicePdfService _invoicePdfService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<ResendInvoiceEmailCommandHandler> _logger;

    public ResendInvoiceEmailCommandHandler(
        IApplicationDbContext dbContext,
        ICurrentUserService currentUserService,
        IInvoicePdfService invoicePdfService,
        IEmailSender emailSender,
        ILogger<ResendInvoiceEmailCommandHandler> logger)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _invoicePdfService = invoicePdfService;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<ResendInvoiceEmailResult> Handle(ResendInvoiceEmailCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var invoice = await _dbContext.Invoices
            .GetForBusinessOrThrowAsync(request.Id, businessId, "Invoice not found.", cancellationToken);

        if (invoice.Status == InvoiceStatus.Draft)
        {
            throw new BadRequestException("Cannot resend a Draft invoice — send it first.");
        }

        var client = await _dbContext.Clients
            .GetForBusinessOrThrowAsync(invoice.ClientId, businessId, "Client not found.", cancellationToken);

        var pdf = await _invoicePdfService.GenerateAsync(invoice.Id, businessId, cancellationToken);

        var emailDelivery = await InvoiceEmailDispatcher.DispatchAsync(
            _dbContext, _emailSender, _logger, invoice, client, pdf, cancellationToken);

        return new ResendInvoiceEmailResult(emailDelivery);
    }
}
