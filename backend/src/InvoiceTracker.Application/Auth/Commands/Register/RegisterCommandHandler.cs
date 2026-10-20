using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.Register;

/// <summary>
/// Creates three correlated rows — Business, Domain <see cref="User"/> (Role = Owner), and the
/// Identity credential-store row (same Id as the Domain User) — as one atomic operation. The
/// Domain rows are written via EF Core directly; the credential-store row is written through
/// <see cref="IIdentityService"/> (backed by UserManager, which shares the same registered
/// DbContext instance). Wrapping the whole sequence in an explicit transaction means a failure
/// partway through — e.g. a duplicate email rejected by Identity — rolls back the Business/User
/// rows too, rather than leaving an orphaned Business with no usable Owner account.
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
        await using var transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

        var business = new Business
        {
            Id = Guid.NewGuid(),
            Name = request.BusinessName,
            Email = request.BusinessEmail
        };
        _dbContext.Businesses.Add(business);

        var ownerId = Guid.NewGuid();
        var owner = new User
        {
            Id = ownerId,
            BusinessId = business.Id,
            Email = request.OwnerEmail,
            Role = UserRole.Owner,
            IsActive = true
        };
        _dbContext.BusinessUsers.Add(owner);

        // Business + Domain User committed to this transaction (not yet to the database).
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Same Id as `owner` above — correlates the credential-store row to the Domain User row.
        var identityResult = await _identityService.CreateUserAsync(ownerId, request.OwnerEmail, request.Password);
        if (!identityResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new IdentityOperationException(identityResult.Errors);
        }

        await transaction.CommitAsync(cancellationToken);

        var token = _jwtTokenGenerator.GenerateToken(ownerId, business.Id, owner.Role.ToString(), owner.Email);

        return new AuthResultDto(token.AccessToken, token.ExpiresAtUtc, ownerId, business.Id, owner.Email, owner.Role.ToString());
    }
}
