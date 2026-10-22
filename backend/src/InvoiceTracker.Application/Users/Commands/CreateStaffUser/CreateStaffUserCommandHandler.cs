using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Users.Common;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using MediatR;

namespace InvoiceTracker.Application.Users.Commands.CreateStaffUser;

/// <summary>
/// Same transactional two-row pattern as <c>RegisterCommandHandler</c> (Auth/Commands/Register):
/// a Domain <see cref="User"/> row (Role = Staff, BusinessId from the caller's own JWT) and the
/// Identity credential-store row (same Id) are written as one atomic operation, so a failure in
/// either (e.g. Identity rejecting a duplicate email) rolls both back rather than leaving an
/// orphaned Domain User row with no way to log in. The business already exists by this point
/// (unlike Register, which creates it) — only the Owner-only "add a teammate" half applies here.
/// </summary>
public class CreateStaffUserCommandHandler : IRequestHandler<CreateStaffUserCommand, UserDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IIdentityService _identityService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IEmailSender _emailSender;

    public CreateStaffUserCommandHandler(
        IApplicationDbContext dbContext,
        IIdentityService identityService,
        ICurrentUserService currentUserService,
        IEmailSender emailSender)
    {
        _dbContext = dbContext;
        _identityService = identityService;
        _currentUserService = currentUserService;
        _emailSender = emailSender;
    }

    public async Task<UserDto> Handle(CreateStaffUserCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        await using var transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

        var staffId = Guid.NewGuid();
        var staffUser = new User
        {
            Id = staffId,
            BusinessId = businessId,
            Email = request.Email,
            Role = UserRole.Staff,
            IsActive = true
        };
        _dbContext.BusinessUsers.Add(staffUser);

        // Domain User row committed to this transaction (not yet to the database).
        await _dbContext.SaveChangesAsync(cancellationToken);

        var tempPassword = TempPasswordGenerator.Generate();

        // Same Id as `staffUser` above — correlates the credential-store row to the Domain User row.
        var identityResult = await _identityService.CreateUserAsync(staffId, request.Email, tempPassword);
        if (!identityResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new IdentityOperationException(identityResult.Errors);
        }

        await transaction.CommitAsync(cancellationToken);

        // Email send happens after commit (not inside the transaction) — there's nothing to roll
        // back it against, and a send failure here shouldn't undo the already-committed account.
        await _emailSender.SendAsync(
            staffUser.Email,
            "Your Eternity Invoicing account",
            $"An account has been created for you. Temporary password: {tempPassword}. " +
            "Please log in and change your password as soon as possible.",
            Array.Empty<EmailAttachment>(),
            cancellationToken);

        return staffUser.ToDto();
    }
}
