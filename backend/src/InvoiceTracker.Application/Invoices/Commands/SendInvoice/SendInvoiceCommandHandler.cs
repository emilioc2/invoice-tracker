using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Persistence;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvoiceTracker.Application.Invoices.Commands.SendInvoice;

/// <summary>
/// Sequence (see architecture.md's "Invoice send/resend email delivery"):
/// 1. Load the invoice (BusinessId-scoped), transition Draft -> Unpaid, set IssueDate/LastModifiedDate,
///    save. This is the transition proper — if it fails (invoice not Draft, or not found/not this
///    business), the whole command fails exactly as it always has, and nothing below ever runs.
/// 2. Once that save has committed, load the Client (for its email address) and render the PDF via
///    IInvoicePdfService.
/// 3. Dispatch the invoice-copy email via the shared InvoiceEmailDispatcher (Pending Notification
///    row written before the send is attempted, per the idempotency rule), recording Sent/Failed.
/// The email step is a best-effort side effect — it never rolls back step 1's already-committed
/// transition, whatever its own outcome.
/// </summary>
public class SendInvoiceCommandHandler : IRequestHandler<SendInvoiceCommand, SendInvoiceResult>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IInvoicePdfService _invoicePdfService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<SendInvoiceCommandHandler> _logger;

    public SendInvoiceCommandHandler(
        IApplicationDbContext dbContext,
        ICurrentUserService currentUserService,
        IInvoicePdfService invoicePdfService,
        IEmailSender emailSender,
        ILogger<SendInvoiceCommandHandler> logger)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _invoicePdfService = invoicePdfService;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<SendInvoiceResult> Handle(SendInvoiceCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var invoice = await _dbContext.Invoices
            .Include(i => i.LineItems)
            .GetForBusinessOrThrowAsync(request.Id, businessId, "Invoice not found.", cancellationToken);

        try
        {
            invoice.TransitionTo(InvoiceStatus.Unpaid);
        }
        catch (InvalidInvoiceStatusTransitionException)
        {
            throw new BadRequestException("Only Draft invoices can be sent.");
        }

        var now = DateTime.UtcNow;
        invoice.IssueDate = now;
        invoice.LastModifiedDate = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // From here on, the transition has already committed — nothing below can or should undo it.
        var client = await _dbContext.Clients
            .GetForBusinessOrThrowAsync(invoice.ClientId, businessId, "Client not found.", cancellationToken);

        var pdf = await _invoicePdfService.GenerateAsync(invoice.Id, businessId, cancellationToken);

        var emailDelivery = await InvoiceEmailDispatcher.DispatchAsync(
            _dbContext, _emailSender, _logger, invoice, client, pdf, cancellationToken);

        return new SendInvoiceResult(invoice.ToDto(), emailDelivery);
    }
}
