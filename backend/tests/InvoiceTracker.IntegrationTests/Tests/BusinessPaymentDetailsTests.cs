using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.BusinessProfile.Common;
using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Application.Users.Common;
using InvoiceTracker.IntegrationTests.Infrastructure;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// <c>GET /api/business</c> / <c>PUT /api/business/payment-details</c>, and the way
/// <c>Business.PaymentDetails</c> flows onto invoice creation/editing/PDF/email, per
/// architecture.md's "Business payment details" section and its test plan additions.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class BusinessPaymentDetailsTests : IntegrationTestBase
{
    public BusinessPaymentDetailsTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    /// <summary>
    /// Creates a Staff user and logs them in via the forgot-password recovery dance — identical to
    /// the pattern already used by RoleAuthorizationTests, since a Staff user's real temp password
    /// is only ever emailed, never returned by any API response.
    /// </summary>
    private async Task<HttpClient> CreateAndLoginStaffAsync(HttpClient ownerClient, string prefix)
    {
        var createStaffResponse = await ownerClient.PostAsJsonAsync("/api/users", new { Email = UniqueEmail($"{prefix}-staff") });
        createStaffResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var staffUser = (await createStaffResponse.Content.ReadFromJsonAsync<UserDto>())!;

        var staffClient = CreateClient();
        await staffClient.PostAsJsonAsync("/api/auth/forgot-password", new { Email = staffUser.Email });
        var sentEmail = Factory.EmailSender.FindLatestTo(staffUser.Email);
        sentEmail.Should().NotBeNull();
        var token = Uri.UnescapeDataString(Regex
            .Match(sentEmail!.Body, @"reset-password\?email=[^&]+&token=(?<token>[^\s]+)").Groups["token"].Value);

        const string staffPassword = "StaffP@ssw0rd123!";
        var resetResponse = await staffClient.PostAsJsonAsync("/api/auth/reset-password", new
        {
            Email = staffUser.Email,
            Token = token,
            NewPassword = staffPassword
        });
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffUser.Email, Password = staffPassword });
        var staffAuth = (await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        staffAuth.Role.Should().Be("Staff");
        Authorize(staffClient, staffAuth.AccessToken);

        return staffClient;
    }

    [Fact]
    public async Task Get_ReturnsCallersOwnBusinessProfile_WithNullPaymentDetails_WhenNeverSet()
    {
        var (client, auth) = await RegisterBusinessAsync("business-profile-get");

        var response = await client.GetAsync("/api/business");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<BusinessProfileDto>();
        profile!.Id.Should().Be(auth.BusinessId!.Value);
        profile.Name.Should().NotBeNullOrWhiteSpace();
        profile.Email.Should().NotBeNullOrWhiteSpace();
        profile.PaymentDetails.Should().BeNull();
    }

    [Fact]
    public async Task Put_UpdatesPaymentDetails_AndSubsequentGetReflectsIt()
    {
        var (client, _) = await RegisterBusinessAsync("business-profile-put");

        var putResponse = await client.PutAsJsonAsync(
            "/api/business/payment-details", new { PaymentDetails = "Bank: Acme Bank, Acct: 123456" });
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await client.GetAsync("/api/business");
        var profile = await getResponse.Content.ReadFromJsonAsync<BusinessProfileDto>();

        profile!.PaymentDetails.Should().Be("Bank: Acme Bank, Acct: 123456");
    }

    [Fact]
    public async Task Put_CanClearPaymentDetailsBackToNull()
    {
        var (client, _) = await RegisterBusinessAsync("business-profile-clear");
        await client.PutAsJsonAsync("/api/business/payment-details", new { PaymentDetails = "Some default" });

        var putResponse = await client.PutAsJsonAsync("/api/business/payment-details", new { PaymentDetails = (string?)null });
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await client.GetAsync("/api/business");
        var profile = await getResponse.Content.ReadFromJsonAsync<BusinessProfileDto>();

        profile!.PaymentDetails.Should().BeNull();
    }

    [Fact]
    public async Task BothOwnerAndStaff_CanGetAndUpdatePaymentDetails_NeitherGets403()
    {
        var (ownerClient, _) = await RegisterBusinessAsync("business-profile-roles");
        var staffClient = await CreateAndLoginStaffAsync(ownerClient, "business-profile-roles");

        var ownerGetResponse = await ownerClient.GetAsync("/api/business");
        ownerGetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var staffGetResponse = await staffClient.GetAsync("/api/business");
        staffGetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var staffPutResponse = await staffClient.PutAsJsonAsync(
            "/api/business/payment-details", new { PaymentDetails = "Set by Staff" });
        staffPutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var ownerPutResponse = await ownerClient.PutAsJsonAsync(
            "/api/business/payment-details", new { PaymentDetails = "Set by Owner" });
        ownerPutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var finalGetResponse = await ownerClient.GetAsync("/api/business");
        var finalProfile = await finalGetResponse.Content.ReadFromJsonAsync<BusinessProfileDto>();
        finalProfile!.PaymentDetails.Should().Be("Set by Owner");
    }

    [Fact]
    public async Task BusinessB_NeverSeesBusinessAsPaymentDetails_ViaItsOwnGet()
    {
        var (clientA, _) = await RegisterBusinessAsync("business-profile-tenant-a");
        var (clientB, authB) = await RegisterBusinessAsync("business-profile-tenant-b");

        await clientA.PutAsJsonAsync("/api/business/payment-details", new { PaymentDetails = "Business A's secret bank details" });

        // Business B's JWT never carries Business A's id anywhere, so there is no cross-tenant
        // request shape to even attempt here — B's own GET must simply return its own (empty)
        // profile, never A's, per the handler always resolving BusinessId server-side.
        var response = await clientB.GetAsync("/api/business");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<BusinessProfileDto>();
        profile!.Id.Should().Be(authB.BusinessId!.Value);
        profile.PaymentDetails.Should().BeNull();
    }

    private async Task<(HttpClient Client, Guid ClientId)> CreateBusinessWithClientAsync(string prefix)
    {
        var (client, _) = await RegisterBusinessAsync(prefix);

        var createClientResponse = await client.PostAsJsonAsync("/api/clients", new
        {
            Name = "Payment Details Test Client",
            Email = UniqueEmail($"{prefix}-client"),
            Phone = "5551234",
            Address = "1 Payment Way"
        });
        var clientDto = (await createClientResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        return (client, clientDto.Id);
    }

    private static object InvoiceCreateBody(Guid clientId, string? paymentDetails = null) => new
    {
        ClientId = clientId,
        DueDate = DateTime.UtcNow.AddDays(30),
        TaxRate = 0m,
        Notes = (string?)null,
        LineItems = new[] { new { Description = "Work", Quantity = 1m, UnitPrice = 250m } },
        PaymentDetails = paymentDetails
    };

    [Fact]
    public async Task CreateInvoice_SnapshotsCurrentBusinessDefault_WhenNoOverrideSupplied()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("invoice-snapshot");
        await client.PutAsJsonAsync("/api/business/payment-details", new { PaymentDetails = "Business default at creation time" });

        var createResponse = await client.PostAsJsonAsync("/api/invoices", InvoiceCreateBody(clientId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = await createResponse.Content.ReadFromJsonAsync<InvoiceDto>();

        invoice!.PaymentDetails.Should().Be("Business default at creation time");
    }

    [Fact]
    public async Task CreateInvoice_ExplicitOverride_TakesPrecedenceOverBusinessDefault()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("invoice-override");
        await client.PutAsJsonAsync("/api/business/payment-details", new { PaymentDetails = "Business default" });

        var createResponse = await client.PostAsJsonAsync(
            "/api/invoices", InvoiceCreateBody(clientId, "Explicit override for this invoice only"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = await createResponse.Content.ReadFromJsonAsync<InvoiceDto>();

        invoice!.PaymentDetails.Should().Be("Explicit override for this invoice only");
    }

    [Fact]
    public async Task CreateInvoice_LaterChangeToBusinessDefault_DoesNotRetroactivelyChangeIt()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("invoice-no-retroactive");
        await client.PutAsJsonAsync("/api/business/payment-details", new { PaymentDetails = "Original default" });

        var createResponse = await client.PostAsJsonAsync("/api/invoices", InvoiceCreateBody(clientId));
        var invoice = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;
        invoice.PaymentDetails.Should().Be("Original default");

        await client.PutAsJsonAsync("/api/business/payment-details", new { PaymentDetails = "New default" });

        var getResponse = await client.GetAsync($"/api/invoices/{invoice.Id}");
        var reloaded = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();

        reloaded!.PaymentDetails.Should().Be("Original default");
    }

    [Fact]
    public async Task UpdateInvoice_CanChangePaymentDetails_WhileStillDraft()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("invoice-update-draft");
        var createResponse = await client.PostAsJsonAsync("/api/invoices", InvoiceCreateBody(clientId, "Original"));
        var invoice = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var updateResponse = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", new
        {
            ClientId = clientId,
            DueDate = invoice.DueDate,
            TaxRate = 0m,
            Notes = (string?)null,
            LineItems = new[] { new { Description = "Work", Quantity = 1m, UnitPrice = 250m } },
            PaymentDetails = "Updated while Draft"
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        updated!.PaymentDetails.Should().Be("Updated while Draft");
    }

    [Fact]
    public async Task UpdateInvoice_RejectsPaymentDetailsChange_OnceNoLongerDraft()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("invoice-update-locked");
        var createResponse = await client.PostAsJsonAsync("/api/invoices", InvoiceCreateBody(clientId, "Original"));
        var invoice = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var sendResponse = await client.PostAsync($"/api/invoices/{invoice.Id}/send", null);
        sendResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateResponse = await client.PutAsJsonAsync($"/api/invoices/{invoice.Id}", new
        {
            ClientId = clientId,
            DueDate = invoice.DueDate,
            TaxRate = 0m,
            Notes = (string?)null,
            LineItems = new[] { new { Description = "Work", Quantity = 1m, UnitPrice = 250m } },
            PaymentDetails = "Attempted change after send"
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var getResponse = await client.GetAsync($"/api/invoices/{invoice.Id}");
        var reloaded = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        reloaded!.PaymentDetails.Should().Be("Original");
    }

    [Fact]
    public async Task GetPdf_StillReturnsValidPdf_WhenInvoiceHasPaymentDetails()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("invoice-pdf-payment-details");
        var createResponse = await client.PostAsJsonAsync(
            "/api/invoices", InvoiceCreateBody(clientId, "Bank: Acme Bank, Acct: 123456"));
        var invoice = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var pdfResponse = await client.GetAsync($"/api/invoices/{invoice.Id}/pdf");

        pdfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task SendInvoice_EmailBody_IncludesPaymentDetailsLine_WhenInvoiceHasPaymentDetails()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("invoice-email-payment-details");
        var createResponse = await client.PostAsJsonAsync(
            "/api/invoices", InvoiceCreateBody(clientId, "Bank: Acme Bank, Acct: 123456"));
        var invoice = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var sendResponse = await client.PostAsync($"/api/invoices/{invoice.Id}/send", null);
        sendResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getClientResponse = await client.GetAsync($"/api/clients/{clientId}");
        var clientDto = (await getClientResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        var sentEmail = Factory.EmailSender.FindLatestTo(clientDto.Email);
        sentEmail.Should().NotBeNull();
        sentEmail!.Body.Should().Contain("Payment details: Bank: Acme Bank, Acct: 123456");
    }

    [Fact]
    public async Task SendInvoice_EmailBody_OmitsPaymentDetailsLine_WhenInvoiceHasNone()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("invoice-email-no-payment-details");
        var createResponse = await client.PostAsJsonAsync("/api/invoices", InvoiceCreateBody(clientId));
        var invoice = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;
        invoice.PaymentDetails.Should().BeNull();

        var sendResponse = await client.PostAsync($"/api/invoices/{invoice.Id}/send", null);
        sendResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getClientResponse = await client.GetAsync($"/api/clients/{clientId}");
        var clientDto = (await getClientResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        var sentEmail = Factory.EmailSender.FindLatestTo(clientDto.Email);
        sentEmail.Should().NotBeNull();
        sentEmail!.Body.Should().NotContain("Payment details");
    }
}
