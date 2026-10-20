using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Notifications.Common;
using InvoiceTracker.Application.Notifications.Queries.ListNotifications;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceTracker.Api.Controllers;

/// <summary>
/// Every route here requires a JWT and is scoped to the caller's own business — see
/// ClientsController's summary for the same note.
/// </summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<NotificationDto>>> List(
        [FromQuery] Guid? invoiceId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new ListNotificationsQuery(invoiceId, pageNumber, pageSize);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result);
    }
}
