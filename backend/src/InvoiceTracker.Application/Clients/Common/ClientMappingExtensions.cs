using InvoiceTracker.Domain.Entities;

namespace InvoiceTracker.Application.Clients.Common;

public static class ClientMappingExtensions
{
    public static ClientDto ToDto(this Client client) =>
        new(client.Id, client.Name, client.Email, client.Phone, client.Address,
            client.CreatedAtDate, client.LastModifiedDate, client.ArchivedAt);
}
