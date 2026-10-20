using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Queries.ListInvoices;

/// <summary>
/// DateFrom/DateTo filter on DueDate — the one invoice date that's always set (IssueDate is null
/// while Draft), and the field the overdue-related filtering in the UI cares most about.
/// </summary>
public record ListInvoicesQuery(
    InvoiceStatus? Status,
    Guid? ClientId,
    DateTime? DateFrom,
    DateTime? DateTo,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<PaginatedResult<InvoiceDto>>;
