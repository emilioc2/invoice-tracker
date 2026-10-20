using InvoiceTracker.Application.Auth.Common;
using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.Register;

/// <summary>
/// First registration: creates the Business + its Owner user (see requirements.md's
/// "Multi-user onboarding"). <see cref="BusinessEmail"/> is the business's own contact email
/// (stored on Business), which may differ from <see cref="OwnerEmail"/> (the Owner's login
/// identity, stored on both the Domain User row and the Identity credential store).
/// </summary>
public record RegisterCommand(
    string BusinessName,
    string BusinessEmail,
    string OwnerEmail,
    string Password) : IRequest<AuthResultDto>;
