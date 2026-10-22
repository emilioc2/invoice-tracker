using InvoiceTracker.Application.Clients.Commands.CreateClient;
using InvoiceTracker.Application.Clients.Commands.RemoveClient;
using InvoiceTracker.Application.Clients.Commands.UpdateClient;
using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Clients.Queries.GetClientById;
using InvoiceTracker.Application.Clients.Queries.ListClients;
using InvoiceTracker.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceTracker.Api.Controllers;

/// <summary>
/// Every route here requires a JWT (the app-wide fallback policy from Program.cs) and is scoped
/// to the caller's own business — handlers resolve BusinessId server-side, never from the
/// request. Stays thin: each action only builds a command/query and calls `_mediator.Send(...)`.
/// </summary>
[ApiController]
[Route("api/clients")]
public class ClientsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ClientsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<ClientDto>>> List(
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ListClientsQuery(search, pageNumber, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetClientByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ClientDto>> Create(CreateClientRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateClientCommand(request.Name, request.Email, request.Phone, request.Address);
        var result = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClientDto>> Update(Guid id, UpdateClientRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateClientCommand(id, request.Name, request.Email, request.Phone, request.Address);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RemoveClientCommand(id), cancellationToken);
        return NoContent();
    }
}

public record CreateClientRequest(string Name, string Email, string? Phone, string? Address);

public record UpdateClientRequest(string Name, string Email, string? Phone, string? Address);
