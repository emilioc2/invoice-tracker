using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.ResetPassword;

public record ResetPasswordCommand(string Email, string Token, string NewPassword) : IRequest<Unit>;
