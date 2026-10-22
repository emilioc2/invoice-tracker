namespace InvoiceTracker.Application.Clients.Common;

public record ClientDto(
    Guid Id,
    string Name,
    string Email,
    string Phone,
    string Address,
    DateTime CreatedAtDate,
    DateTime LastModifiedDate,
    string Status,
    DateTime StatusChangedAt);
