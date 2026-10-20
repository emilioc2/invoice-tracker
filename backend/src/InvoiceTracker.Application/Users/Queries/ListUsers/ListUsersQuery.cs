using InvoiceTracker.Application.Users.Common;
using MediatR;

namespace InvoiceTracker.Application.Users.Queries.ListUsers;

/// <summary>
/// Lists every user (Owner + Staff, active and inactive) in the caller's business. Any
/// authenticated user may call this — unlike create/deactivate, it's not Owner-only. No
/// pagination: a business's team is expected to stay small, unlike Clients/Invoices.
/// </summary>
public record ListUsersQuery : IRequest<List<UserDto>>;
