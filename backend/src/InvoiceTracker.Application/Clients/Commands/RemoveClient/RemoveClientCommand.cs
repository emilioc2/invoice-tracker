using MediatR;

namespace InvoiceTracker.Application.Clients.Commands.RemoveClient;

/// <summary>
/// Soft delete: transitions the client to <see cref="InvoiceTracker.Domain.Enums.ClientStatus.Removed"/>
/// via <see cref="InvoiceTracker.Domain.Entities.Client.TransitionTo"/> rather than removing the
/// row. Never a hard delete.
/// </summary>
public record RemoveClientCommand(Guid Id) : IRequest;
