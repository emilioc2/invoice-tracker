namespace InvoiceTracker.Application.Users.Common;

public record UserDto(Guid Id, string Email, string Role, bool IsActive);
