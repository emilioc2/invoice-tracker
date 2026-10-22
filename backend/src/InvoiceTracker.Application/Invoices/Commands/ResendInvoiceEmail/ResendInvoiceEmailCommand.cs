using InvoiceTracker.Application.Invoices.Common;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Commands.ResendInvoiceEmail;

/// <summary>
/// Re-delivers the same PDF+details email as Send, for any invoice past Draft — see
/// architecture.md's "Invoice send/resend email delivery" section. Never touches the invoice's
/// status or the Draft-only edit lock; records a fresh Notification (new SourceEventId) each call.
/// </summary>
public record ResendInvoiceEmailCommand(Guid Id) : IRequest<ResendInvoiceEmailResult>;

public record ResendInvoiceEmailResult(EmailDeliveryResult EmailDelivery);
