using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Users.Common;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvoiceTracker.Application.Users.Commands.CreateStaffUser;

/// <summary>
/// Same transactional two-row pattern as <c>RegisterCommandHandler</c> (Auth/Commands/Register):
/// a <see cref="UserBusinessMembership"/> row (Role = Staff, BusinessId from the caller's own JWT)
/// and the Identity credential-store row (same Id as the membership's <c>UserId</c>) are written
/// as one atomic operation, so a failure in either (e.g. Identity rejecting a duplicate email)
/// rolls both back rather than leaving an orphaned membership row with no way to log in. The
/// business already exists by this point (unlike Register, which creates it) — only the
/// Owner-only "add a teammate" half applies here.
///
/// Per requirements.md's "Multi-business membership": if the invited email already has an identity
/// (elsewhere, or even in this same business already), a new Staff membership is attached to that
/// *existing* identity instead — no new identity/temp password, and a different notification email.
/// If the invited email has no identity yet, behaves exactly as before.
/// </summary>
public class CreateStaffUserCommandHandler : IRequestHandler<CreateStaffUserCommand, UserDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IIdentityService _identityService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<CreateStaffUserCommandHandler> _logger;

    public CreateStaffUserCommandHandler(
        IApplicationDbContext dbContext,
        IIdentityService identityService,
        ICurrentUserService currentUserService,
        IEmailSender emailSender,
        ILogger<CreateStaffUserCommandHandler> logger)
    {
        _dbContext = dbContext;
        _identityService = identityService;
        _currentUserService = currentUserService;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<UserDto> Handle(CreateStaffUserCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var existingUserId = await _identityService.FindUserIdByEmailAsync(request.Email);

        return existingUserId is not null
            ? await AttachMembershipToExistingIdentityAsync(existingUserId.Value, businessId, request.Email, cancellationToken)
            : await CreateNewIdentityWithMembershipAsync(businessId, request.Email, cancellationToken);
    }

    /// <summary>
    /// The invited email already has an identity (in this business or another) — attach a new
    /// Staff membership to it. No new identity/temp password is created, and
    /// <c>ApplicationUser.MustChangePassword</c> is left exactly as that identity's current value
    /// already is (not touched here at all).
    /// </summary>
    private async Task<UserDto> AttachMembershipToExistingIdentityAsync(
        Guid existingUserId, Guid businessId, string email, CancellationToken cancellationToken)
    {
        var alreadyMember = await _dbContext.UserBusinessMemberships
            .AnyAsync(m => m.UserId == existingUserId && m.BusinessId == businessId, cancellationToken);
        if (alreadyMember)
        {
            throw new ConflictException("This email already has a membership in this business.");
        }

        var membership = new UserBusinessMembership
        {
            Id = Guid.NewGuid(),
            UserId = existingUserId,
            BusinessId = businessId,
            Role = UserRole.Staff
            // Status defaults to UserStatus.Active via the entity's own default initializer.
        };
        _dbContext.UserBusinessMemberships.Add(membership);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var business = await _dbContext.Businesses.FirstAsync(b => b.Id == businessId, cancellationToken);

        // Membership is already committed above — a send failure here (e.g. the sandbox email
        // provider rejecting a recipient it isn't allowed to deliver to) must not surface as a
        // request failure, since there's nothing left to roll back against. Log and move on.
        try
        {
            await _emailSender.SendAsync(
                email,
                $"You've been added to {business.Name}",
                $"You've been added to {business.Name} on Eternity Invoicing. Log in with your existing password to get started.",
                Array.Empty<EmailAttachment>(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send 'added to business' notification email to {Email} for business {BusinessId}.", email, businessId);
        }

        return membership.ToDto(email);
    }

    /// <summary>The invited email has no identity yet — behaves exactly as before this step.</summary>
    private async Task<UserDto> CreateNewIdentityWithMembershipAsync(Guid businessId, string email, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

        // Fresh Guid shared by the membership's own Id and its UserId — same "one new identity,
        // one new membership, same shared id" shape as RegisterCommandHandler.
        var staffId = Guid.NewGuid();
        var staffMembership = new UserBusinessMembership
        {
            Id = staffId,
            UserId = staffId,
            BusinessId = businessId,
            Role = UserRole.Staff
            // Status defaults to UserStatus.Active via the entity's own default initializer.
        };
        _dbContext.UserBusinessMemberships.Add(staffMembership);

        // Membership row committed to this transaction (not yet to the database).
        await _dbContext.SaveChangesAsync(cancellationToken);

        var tempPassword = TempPasswordGenerator.Generate();

        // Same Id as `staffMembership.UserId` above — correlates the credential-store row to the
        // membership. mustChangePassword: true — must be cleared via ChangePasswordCommandHandler
        // before this Staff user gets normal access — see architecture.md's "Forced password
        // change for temp-password logins".
        var identityResult = await _identityService.CreateUserAsync(staffId, email, tempPassword, mustChangePassword: true);
        if (!identityResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new IdentityOperationException(identityResult.Errors);
        }

        await transaction.CommitAsync(cancellationToken);

        // Email send happens after commit (not inside the transaction) — there's nothing to roll
        // back it against, and a send failure here shouldn't undo the already-committed account.
        // Caught (not rethrown) for the same reason: the account was already created successfully,
        // so a delivery failure (e.g. the sandbox email provider rejecting a recipient it isn't
        // allowed to deliver to) must not turn into a 500 for a request that actually succeeded.
        try
        {
            await _emailSender.SendAsync(
                email,
                "Your Eternity Invoicing account",
                $"An account has been created for you. Temporary password: {tempPassword}. " +
                "Please log in and change your password as soon as possible.",
                Array.Empty<EmailAttachment>(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send new-account email to {Email} for business {BusinessId}.", email, businessId);
        }

        // email is already on hand (it's what we just created the identity with) — no need to
        // round-trip through IIdentityService just to read it back.
        return staffMembership.ToDto(email);
    }
}
