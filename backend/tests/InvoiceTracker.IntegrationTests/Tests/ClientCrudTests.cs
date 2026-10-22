using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.IntegrationTests.Infrastructure;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>Client CRUD, including the archive-not-delete behavior (requirements.md's soft-delete rule).</summary>
[Collection(IntegrationTestCollection.Name)]
public class ClientCrudTests : IntegrationTestBase
{
    public ClientCrudTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    private static object NewClientRequest(string? name = null) => new
    {
        Name = name ?? "Jane Doe",
        Email = $"jane-{Guid.NewGuid():N}@example.com",
        Phone = "5551234",
        Address = "123 Main St"
    };

    [Fact]
    public async Task Create_Then_GetById_ReturnsSameClient()
    {
        var (client, _) = await RegisterBusinessAsync("client-crud");

        var createResponse = await client.PostAsJsonAsync("/api/clients", NewClientRequest("Acme Corp"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ClientDto>();
        created!.Name.Should().Be("Acme Corp");

        var getResponse = await client.GetAsync($"/api/clients/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ClientDto>();
        fetched!.Id.Should().Be(created.Id);
        fetched.Email.Should().Be(created.Email);
    }

    [Fact]
    public async Task Update_ChangesPersist()
    {
        var (client, _) = await RegisterBusinessAsync("client-update");

        var createResponse = await client.PostAsJsonAsync("/api/clients", NewClientRequest("Old Name"));
        var created = (await createResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        var updateResponse = await client.PutAsJsonAsync($"/api/clients/{created.Id}", new
        {
            Name = "New Name",
            Email = created.Email,
            Phone = "5559999",
            Address = "456 Updated Ave"
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await client.GetAsync($"/api/clients/{created.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<ClientDto>();
        fetched!.Name.Should().Be("New Name");
        fetched.Phone.Should().Be("5559999");
    }

    [Fact]
    public async Task Archive_HidesFromDefaultList_ButRecordStillExists()
    {
        var (client, _) = await RegisterBusinessAsync("client-archive");

        var createResponse = await client.PostAsJsonAsync("/api/clients", NewClientRequest("To Be Archived"));
        var created = (await createResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        var archiveResponse = await client.DeleteAsync($"/api/clients/{created.Id}");
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Hidden from the default list...
        var listResponse = await client.GetAsync("/api/clients?pageSize=100");
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<ClientDto>>();
        list!.Items.Should().NotContain(c => c.Id == created.Id);

        // ...but not actually deleted: direct-by-id lookup still finds it, now with ArchivedAt set.
        var getResponse = await client.GetAsync($"/api/clients/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ClientDto>();
        fetched!.ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Archive_IsIdempotent()
    {
        var (client, _) = await RegisterBusinessAsync("client-archive-twice");

        var createResponse = await client.PostAsJsonAsync("/api/clients", NewClientRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        var firstArchive = await client.DeleteAsync($"/api/clients/{created.Id}");
        var secondArchive = await client.DeleteAsync($"/api/clients/{created.Id}");

        firstArchive.StatusCode.Should().Be(HttpStatusCode.NoContent);
        secondArchive.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
