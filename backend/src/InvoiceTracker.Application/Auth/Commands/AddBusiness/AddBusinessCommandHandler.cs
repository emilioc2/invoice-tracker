using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.AddBusiness;

/// <summary>
/// Same transactional two-row pattern as <c>RegisterCommandHandler</c> (Business + Owner
/// membership, one atomic operation) — but no Identity row is created here. The membership's own
/// Id is a fresh Guid distinct from UserId (unlike Register/CreateStaffUser's "shared id" shape for
/// a brand-new identity's first membership) — this is precisely the case where the same identity
/// now holds more than one membership row, each with its own Id, sharing one UserId.
/// </summary>
public class AddBusinessCommandHandler : IRequestHandler<AddBusinessCommand, AuthResultDto>
{
    private const string CallerIdentityNotFoundMessage = "Caller identity could not be resolved.";

    private readonly IApplicationDbContext _dbContext;
    private readonly IIdentityService _identityService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AddBusinessCommandHandler(
        IApplicationDbContext dbContext,
        IIdentityService identityService,
        ICurrentUserService currentUserService,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _identityService = identityService;
        _currentUserService = currentUserService;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResultDto> Handle(AddBusinessCommand request, CancellationToken cancellationToken)
    {
        var callerUserId = _currentUserService.UserId!.Value;

        // Resolved up front — if this somehow can't be found for an already-authenticated caller,
        // nothing below (Business/membership creation) should happen either.
        var identity = await _identityService.FindIdentityByIdAsync(callerUserId);
        if (identity is null)
        {
            throw new NotFoundException(CallerIdentityNotFoundMessage);
        }

        await using var transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

        var business = new Business
        {
            Id = Guid.NewGuid(),
            Name = request.BusinessName,
            Email = request.BusinessEmail
        };
        _dbContext.Businesses.Add(business);

        var membership = new UserBusinessMembership
        {
            Id = Guid.NewGuid(),
            UserId = callerUserId,
            BusinessId = business.Id,
            Role = UserRole.Owner
            // Status defaults to UserStatus.Active via the entity's own default initializer.
        };
        _dbContext.UserBusinessMemberships.Add(membership);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Behaves like switch-business into the new business from here — the returned token is
        // already scoped to it, no separate switch call required.
        var token = _jwtTokenGenerator.GenerateToken(callerUserId, business.Id, membership.Role.ToString(), identity.Email);

        return AuthResultDto.ForToken(
            token, callerUserId, business.Id, identity.Email, membership.Role.ToString(), identity.MustChangePassword);
    }
}
