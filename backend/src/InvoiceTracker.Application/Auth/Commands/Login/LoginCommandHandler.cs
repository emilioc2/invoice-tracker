using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResultDto>
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    private readonly IApplicationDbContext _dbContext;
    private readonly IIdentityService _identityService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public LoginCommandHandler(
        IApplicationDbContext dbContext,
        IIdentityService identityService,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _identityService = identityService;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResultDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var userId = await _identityService.ValidateCredentialsAsync(request.Email, request.Password);
        if (userId is null)
        {
            throw new AuthenticationFailedException(InvalidCredentialsMessage);
        }

        var user = await _dbContext.BusinessUsers
            .FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);

        // Credentials matched the Identity store but there's no correlated (active) Domain User
        // row — treat identically to a bad password rather than leaking which part failed.
        if (user is null || !user.IsActive)
        {
            throw new AuthenticationFailedException(InvalidCredentialsMessage);
        }

        var token = _jwtTokenGenerator.GenerateToken(user.Id, user.BusinessId, user.Role.ToString(), user.Email);

        return new AuthResultDto(
            token.AccessToken, token.ExpiresAtUtc, user.Id, user.BusinessId, user.Email, user.Role.ToString(), user.MustChangePassword);
    }
}
