using InvoiceTracker.Application.Users.Commands.CreateStaffUser;
using InvoiceTracker.Application.Users.Commands.DeactivateUser;
using InvoiceTracker.Application.Users.Commands.ReactivateUser;
using InvoiceTracker.Application.Users.Common;
using InvoiceTracker.Application.Users.Queries.ListUsers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceTracker.Api.Controllers;

/// <summary>
/// Team management (requirements.md's "Multi-user onboarding" + "Permissions"). Every route is
/// scoped to the caller's own business — handlers resolve BusinessId server-side, never from the
/// request. Unlike every controller before this one, role (not just "is there a valid JWT")
/// matters here: create/deactivate are Owner-only (<see cref="AuthorizeAttribute.Roles"/>), while
/// list is open to any authenticated user in the business.
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListUsersQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Owner")]
    public async Task<ActionResult<UserDto>> Create(CreateStaffUserRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateStaffUserCommand(request.Email), cancellationToken);
        return CreatedAtAction(nameof(List), result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeactivateUserCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ReactivateUserCommand(id), cancellationToken);
        return NoContent();
    }
}

public record CreateStaffUserRequest(string Email);
