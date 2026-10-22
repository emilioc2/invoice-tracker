using InvoiceTracker.Application.Invoices.Common;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Commands.SendInvoice;

/// <summary>
/// Draft → Unpaid. Sets IssueDate = now server-side; immutable from this point on. The status
/// transition always succeeds/fails purely on its own merits; the invoice-email delivery attempt
/// that follows it is a best-effort side effect reported separately via
/// <see cref="SendInvoiceResult.EmailDelivery"/> — see architecture.md's "Invoice send/resend email
/// delivery" section.
/// </summary>
public record SendInvoiceCommand(Guid Id) : IRequest<SendInvoiceResult>;

public record SendInvoiceResult(InvoiceDto Invoice, EmailDeliveryResult EmailDelivery);
