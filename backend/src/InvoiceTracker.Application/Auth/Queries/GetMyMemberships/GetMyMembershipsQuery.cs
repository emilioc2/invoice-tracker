using InvoiceTracker.Application.Auth.Common;
using MediatR;

namespace InvoiceTracker.Application.Auth.Queries.GetMyMemberships;

/// <summary>
/// Lets an already-authenticated caller list their own active <see cref="Domain.Entities.UserBusinessMembership"/>
/// rows on demand — e.g. the frontend's "Switch business" affordance doing a real lazy fetch on
/// open, rather than relying only on whatever <c>memberships</c> list a prior multi-membership
/// login/select-business response happened to return earlier in this session. Reuses
/// <see cref="MembershipOptionDto"/>, the same shape <c>LoginCommandHandler</c> already returns for
/// its own multi-membership case, rather than inventing a new DTO.
/// </summary>
public record GetMyMembershipsQuery : IRequest<List<MembershipOptionDto>>;
