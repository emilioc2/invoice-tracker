using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Invoices.Commands.ArchiveInvoice;
using InvoiceTracker.Application.Invoices.Commands.CancelInvoice;
using InvoiceTracker.Application.Invoices.Commands.CreateInvoice;
using InvoiceTracker.Application.Invoices.Commands.MarkInvoicePaid;
using InvoiceTracker.Application.Invoices.Commands.ResendInvoiceEmail;
using InvoiceTracker.Application.Invoices.Commands.SendInvoice;
using InvoiceTracker.Application.Invoices.Commands.UpdateInvoice;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Application.Invoices.Queries.GetInvoiceById;
using InvoiceTracker.Application.Invoices.Queries.GetInvoicePdf;
using InvoiceTracker.Application.Invoices.Queries.ListInvoices;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceTracker.Api.Controllers;

/// <summary>
/// Every route here requires a JWT and is scoped to the caller's own business — see
/// ClientsController's summary for the same note. Kafka-notification-related routes are a later
/// build-order step, not implemented here.
/// </summary>
[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public InvoicesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<InvoiceDto>>> List(
        [FromQuery] InvoiceStatus? status,
        [FromQuery] Guid? clientId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new ListInvoicesQuery(status, clientId, dateFrom, dateTo, pageNumber, pageSize);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetInvoiceByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> GetPdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetInvoicePdfQuery(id), cancellationToken);
        return File(result.Content, "application/pdf", result.FileName);
    }

    [HttpPost]
    public async Task<ActionResult<InvoiceDto>> Create(CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateInvoiceCommand(request.ClientId, request.DueDate, request.TaxRate, request.Notes, request.LineItems);
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InvoiceDto>> Update(Guid id, UpdateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateInvoiceCommand(id, request.ClientId, request.DueDate, request.TaxRate, request.Notes, request.LineItems);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/send")]
    public async Task<ActionResult<SendInvoiceResult>> Send(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SendInvoiceCommand(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/resend-email")]
    public async Task<ActionResult<ResendInvoiceEmailResult>> ResendEmail(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ResendInvoiceEmailCommand(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/mark-paid")]
    public async Task<ActionResult<InvoiceDto>> MarkPaid(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new MarkInvoicePaidCommand(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<InvoiceDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelInvoiceCommand(id), cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ArchiveInvoiceCommand(id), cancellationToken);
        return NoContent();
    }
}

public record CreateInvoiceRequest(Guid ClientId, DateTime DueDate, decimal TaxRate, string? Notes, List<InvoiceLineItemRequest> LineItems);

public record UpdateInvoiceRequest(Guid ClientId, DateTime DueDate, decimal TaxRate, string? Notes, List<InvoiceLineItemRequest> LineItems);
