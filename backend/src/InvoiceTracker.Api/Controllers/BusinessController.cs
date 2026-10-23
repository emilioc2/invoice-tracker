using InvoiceTracker.Application.BusinessProfile.Commands.UpdatePaymentDetails;
using InvoiceTracker.Application.BusinessProfile.Common;
using InvoiceTracker.Application.BusinessProfile.Queries.GetBusinessProfile;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceTracker.Api.Controllers;

/// <summary>
/// The caller's own business profile/settings (requirements.md's "Business payment details").
/// Every route here requires a JWT (the app-wide fallback policy from Program.cs) and is scoped to
/// the caller's own business — handlers resolve BusinessId server-side, never from the request.
/// Unlike UsersController, no route here is role-gated (no <c>[Authorize(Roles = ...)]</c>): both
/// are reachable by any authenticated user in the business (Owner or Staff), mirroring the
/// existing <c>GET /api/users</c> any-authenticated-user precedent. A singular, non-plural route
/// (<c>/api/business</c>, not <c>/api/businesses</c>) since a token is always scoped to exactly
/// one business — see architecture.md's REST API routes table.
/// </summary>
[ApiController]
[Route("api/business")]
public class BusinessController : ControllerBase
{
    private readonly IMediator _mediator;

    public BusinessController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<BusinessProfileDto>> GetProfile(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBusinessProfileQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpPut("payment-details")]
    public async Task<ActionResult<BusinessProfileDto>> UpdatePaymentDetails(
        UpdatePaymentDetailsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdatePaymentDetailsCommand(request.PaymentDetails), cancellationToken);
        return Ok(result);
    }
}

public record UpdatePaymentDetailsRequest(string? PaymentDetails);
