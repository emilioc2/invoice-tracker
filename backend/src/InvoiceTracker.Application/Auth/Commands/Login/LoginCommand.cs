using InvoiceTracker.Application.Auth.Common;
using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.Login;

public record LoginCommand(string Email, string Password) : IRequest<AuthResultDto>;
