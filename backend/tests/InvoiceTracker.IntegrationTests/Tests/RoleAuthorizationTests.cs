using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Users.Common;
using InvoiceTracker.IntegrationTests.Infrastructure;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// Role-based authorization on the user-management endpoints (requirements.md's Permissions
/// section): Staff cannot create users, Owner can, and an Owner can never deactivate their own
/// account (the "exactly one Owner per business" safeguard).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class RoleAuthorizationTests : IntegrationTestBase
{
    public RoleAuthorizationTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    private async Task<(HttpClient OwnerClient, AuthResultDto OwnerAuth, UserDto StaffUser)> RegisterBusinessWithStaffAsync(string prefix)
    {
        var (ownerClient, ownerAuth) = await RegisterBusinessAsync(prefix);

        var createStaffResponse = await ownerClient.PostAsJsonAsync("/api/users", new { Email = UniqueEmail($"{prefix}-staff") });
        createStaffResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var staffUser = (await createStaffResponse.Content.ReadFromJsonAsync<UserDto>())!;

        return (ownerClient, ownerAuth, staffUser);
    }

    [Fact]
    public async Task Owner_CanCreateStaffUser()
    {
        var (ownerClient, _) = await RegisterBusinessAsync("role-owner-create");

        var response = await ownerClient.PostAsJsonAsync("/api/users", new { Email = UniqueEmail("new-staff") });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var staffUser = await response.Content.ReadFromJsonAsync<UserDto>();
        staffUser!.Role.Should().Be("Staff");
        staffUser.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Staff_CannotCreateUsers_Returns403()
    {
        var (ownerClient, _, staffUser) = await RegisterBusinessWithStaffAsync("role-staff-forbidden");

        // We never captured the Staff user's temp password (it's only emailed), so log in as Staff
        // the same way the real app would have to: reset the password via the forgot-password flow,
        // using the fake IEmailSender to recover the token (never a real Formspree call).
        var staffClient = CreateClient();
        await staffClient.PostAsJsonAsync("/api/auth/forgot-password", new { Email = staffUser.Email });
        var sentEmail = Factory.EmailSender.FindLatestTo(staffUser.Email);
        sentEmail.Should().NotBeNull();
        var token = System.Text.RegularExpressions.Regex.Match(sentEmail!.Body, "password: (?<token>.+)$").Groups["token"].Value;

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

        var createAttempt = await staffClient.PostAsJsonAsync("/api/users", new { Email = UniqueEmail("staff-cant-create") });

        createAttempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_CannotDeactivateOwnAccount_Returns400()
    {
        var (ownerClient, ownerAuth) = await RegisterBusinessAsync("role-owner-self-deactivate");

        var response = await ownerClient.DeleteAsync($"/api/users/{ownerAuth.UserId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Owner_CanDeactivateStaffUser()
    {
        var (ownerClient, _, staffUser) = await RegisterBusinessWithStaffAsync("role-owner-deactivate-staff");

        var response = await ownerClient.DeleteAsync($"/api/users/{staffUser.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await ownerClient.GetAsync("/api/users");
        var users = await listResponse.Content.ReadFromJsonAsync<List<UserDto>>();
        users!.Single(u => u.Id == staffUser.Id).IsActive.Should().BeFalse();
    }
}
