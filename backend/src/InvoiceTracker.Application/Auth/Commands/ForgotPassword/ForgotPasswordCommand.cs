using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.ForgotPassword;

public record ForgotPasswordCommand(string Email) : IRequest<Unit>;
