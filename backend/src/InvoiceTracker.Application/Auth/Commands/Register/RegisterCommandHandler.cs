using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.Register;

/// <summary>
/// Creates three correlated rows — Business, <see cref="UserBusinessMembership"/> (Role = Owner),
/// and the Identity credential-store row (same Id as the membership's <c>UserId</c>) — as one
/// atomic operation. The Business/membership rows are written via EF Core directly; the
/// credential-store row is written through <see cref="IIdentityService"/> (backed by UserManager,
/// which shares the same registered DbContext instance). Wrapping the whole sequence in an
/// explicit transaction means a failure partway through — e.g. a duplicate email rejected by
/// Identity (now only a narrow race-condition backstop: the common case is already caught up front
/// by the explicit <see cref="IIdentityService.FindUserIdByEmailAsync"/> conflict check below) —
/// rolls back the Business/membership rows too, rather than leaving an orphaned Business with no
/// usable Owner account.
/// </summary>
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResultDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IIdentityService _identityService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public RegisterCommandHandler(
        IApplicationDbContext dbContext,
        IIdentityService identityService,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _identityService = identityService;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResultDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        // Multi-business membership (requirements.md): registering with an email that already has
        // an identity is rejected outright — the caller must log in and use "Add a business"
        // instead, rather than this silently reusing or colliding with the existing identity.
        // Checked before opening the transaction below since there's nothing to roll back yet.
        var existingUserId = await _identityService.FindUserIdByEmailAsync(request.OwnerEmail);
        if (existingUserId is not null)
        {
            throw new ConflictException(
                "An account with this email already exists. Please log in and use \"Add a business\" instead.");
        }

        await using var transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

        var business = new Business
        {
            Id = Guid.NewGuid(),
            Name = request.BusinessName,
            Email = request.BusinessEmail
        };
        _dbContext.Businesses.Add(business);

        // Fresh Guid shared by the membership's own Id and its UserId (the identity this
        // membership correlates to) — same "one new identity, one new membership, same shared id"
        // shape as before the restructure, just across two distinct columns now instead of one
        // shared primary key.
        var ownerId = Guid.NewGuid();
        var owner = new UserBusinessMembership
        {
            Id = ownerId,
            UserId = ownerId,
            BusinessId = business.Id,
            Role = UserRole.Owner
            // Status defaults to UserStatus.Active via the entity's own default initializer.
        };
        _dbContext.UserBusinessMemberships.Add(owner);

        // Business + membership committed to this transaction (not yet to the database).
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Same Id as `owner.UserId` above — correlates the credential-store row to the membership.
        // A newly-registered Owner chooses their own password at registration — never a temp
        // password — so mustChangePassword is left at its default (false) here.
        var identityResult = await _identityService.CreateUserAsync(ownerId, request.OwnerEmail, request.Password);
        if (!identityResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new IdentityOperationException(identityResult.Errors);
        }

        await transaction.CommitAsync(cancellationToken);

        var token = _jwtTokenGenerator.GenerateToken(ownerId, business.Id, owner.Role.ToString(), request.OwnerEmail);

        return new AuthResultDto(
            token.AccessToken, token.ExpiresAtUtc, ownerId, business.Id, request.OwnerEmail, owner.Role.ToString(), MustChangePassword: false);
    }
}
