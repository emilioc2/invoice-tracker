using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Users.Common;
using InvoiceTracker.IntegrationTests.Infrastructure;
using InvoiceTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        staffUser.Status.Should().Be("Active");
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
        users!.Single(u => u.Id == staffUser.Id).Status.Should().Be("Deactivated");
    }

    [Fact]
    public async Task Owner_CanReactivateDeactivatedStaffUser_AndTheyCanLoginAgain()
    {
        var (ownerClient, _, staffUser) = await RegisterBusinessWithStaffAsync("role-owner-reactivate-staff");

        var deactivateResponse = await ownerClient.DeleteAsync($"/api/users/{staffUser.Id}");
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reactivateResponse = await ownerClient.PostAsync($"/api/users/{staffUser.Id}/reactivate", null);
        reactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await ownerClient.GetAsync("/api/users");
        var users = await listResponse.Content.ReadFromJsonAsync<List<UserDto>>();
        users!.Single(u => u.Id == staffUser.Id).Status.Should().Be("Active");

        // ...and the reactivated Staff user can actually log back in (reset password via the
        // forgot-password flow, same recovery mechanism RoleAuthorizationTests already uses
        // elsewhere, since we never captured the original temp password).
        var staffClient = CreateClient();
        await staffClient.PostAsJsonAsync("/api/auth/forgot-password", new { Email = staffUser.Email });
        var sentEmail = Factory.EmailSender.FindLatestTo(staffUser.Email);
        sentEmail.Should().NotBeNull();
        var token = System.Text.RegularExpressions.Regex.Match(sentEmail!.Body, "password: (?<token>.+)$").Groups["token"].Value;

        const string staffPassword = "ReactivatedP@ss123!";
        var resetResponse = await staffClient.PostAsJsonAsync("/api/auth/reset-password", new
        {
            Email = staffUser.Email,
            Token = token,
            NewPassword = staffPassword
        });
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffUser.Email, Password = staffPassword });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        staffAuth!.Role.Should().Be("Staff");
    }

    [Fact]
    public async Task Staff_CannotReactivateUsers_Returns403()
    {
        var (ownerClient, _, staffUser) = await RegisterBusinessWithStaffAsync("role-staff-reactivate-forbidden");

        var deactivateResponse = await ownerClient.DeleteAsync($"/api/users/{staffUser.Id}");
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Reactivate the Staff user so they can log back in and attempt the forbidden call
        // themselves (rather than asserting against some other Staff account).
        var secondReactivate = await ownerClient.PostAsync($"/api/users/{staffUser.Id}/reactivate", null);
        secondReactivate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var staffClient = CreateClient();
        await staffClient.PostAsJsonAsync("/api/auth/forgot-password", new { Email = staffUser.Email });
        var sentEmail = Factory.EmailSender.FindLatestTo(staffUser.Email);
        var token = System.Text.RegularExpressions.Regex.Match(sentEmail!.Body, "password: (?<token>.+)$").Groups["token"].Value;

        const string staffPassword = "StaffReactivate@123!";
        await staffClient.PostAsJsonAsync("/api/auth/reset-password", new
        {
            Email = staffUser.Email,
            Token = token,
            NewPassword = staffPassword
        });

        var loginResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffUser.Email, Password = staffPassword });
        var staffAuth = (await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        Authorize(staffClient, staffAuth.AccessToken);

        // Deactivate once more so there's something to (attempt to) reactivate as Staff.
        await ownerClient.DeleteAsync($"/api/users/{staffUser.Id}");

        var reactivateAttempt = await staffClient.PostAsync($"/api/users/{staffUser.Id}/reactivate", null);

        reactivateAttempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reactivating_AnAlreadyActiveUser_IsIdempotent()
    {
        var (ownerClient, _, staffUser) = await RegisterBusinessWithStaffAsync("role-reactivate-idempotent");

        // Staff user is already Active (never deactivated) — reactivating is a no-op success.
        var reactivateResponse = await ownerClient.PostAsync($"/api/users/{staffUser.Id}/reactivate", null);

        reactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await ownerClient.GetAsync("/api/users");
        var users = await listResponse.Content.ReadFromJsonAsync<List<UserDto>>();
        users!.Single(u => u.Id == staffUser.Id).Status.Should().Be("Active");
    }

    /// <summary>
    /// Full register → login → deactivate → reactivate → login-again round trip, specifically
    /// proving the one behavior none of the other tests in this file actually exercise end-to-end:
    /// requirements.md's "a deactivated membership ... blocks logins ... immediately" claim. The
    /// other tests here assert the membership's <c>Status</c> flips in the users list, but never
    /// that a deactivated Staff user's own login attempt is actually rejected (401) before being
    /// reactivated — this is the QA gate for this restructure step's "zero behavior change for the
    /// single-membership case" goal, since the restructure moved this exact check (membership
    /// Status lookup in <c>LoginCommandHandler</c>) onto the new <c>UserBusinessMembership</c> shape.
    /// </summary>
    [Fact]
    public async Task DeactivatedStaffUser_CannotLogin_ButCanAgainOnceReactivated()
    {
        var (ownerClient, _, staffUser) = await RegisterBusinessWithStaffAsync("role-deactivated-login-blocked");

        // Recover a usable password for the Staff user (never captured the temp password directly
        // — it's only emailed) via the forgot-password flow, same recovery mechanism used
        // elsewhere in this test class.
        var staffClient = CreateClient();
        await staffClient.PostAsJsonAsync("/api/auth/forgot-password", new { Email = staffUser.Email });
        var sentEmail = Factory.EmailSender.FindLatestTo(staffUser.Email);
        sentEmail.Should().NotBeNull();
        var token = System.Text.RegularExpressions.Regex.Match(sentEmail!.Body, "password: (?<token>.+)$").Groups["token"].Value;

        const string staffPassword = "DeactivateLoginBlocked1!";
        var resetResponse = await staffClient.PostAsJsonAsync("/api/auth/reset-password", new
        {
            Email = staffUser.Email,
            Token = token,
            NewPassword = staffPassword
        });
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Baseline: login succeeds while still Active.
        var loginBeforeResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffUser.Email, Password = staffPassword });
        loginBeforeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deactivateResponse = await ownerClient.DeleteAsync($"/api/users/{staffUser.Id}");
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Same credentials, now rejected identically to a bad password — no new information leaked.
        var loginWhileDeactivatedResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffUser.Email, Password = staffPassword });
        loginWhileDeactivatedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var reactivateResponse = await ownerClient.PostAsync($"/api/users/{staffUser.Id}/reactivate", null);
        reactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Same credentials, no new password/reset needed — login works again.
        var loginAfterReactivateResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffUser.Email, Password = staffPassword });
        loginAfterReactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalAuth = await loginAfterReactivateResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        finalAuth!.Role.Should().Be("Staff");
    }

    /// <summary>
    /// ReactivateUserCommandHandler's "target is an Owner" guard should be unreachable through the
    /// real HTTP API — DeactivateUserCommandHandler already refuses to deactivate any Owner (both
    /// self and otherwise), so no code path should ever produce a Deactivated Owner row to
    /// reactivate in the first place. Per the QA task's instruction to "construct that scenario if
    /// you can," this test does so anyway by writing directly to the Owner's row via raw SQL,
    /// bypassing <c>UserBusinessMembership.TransitionTo</c>'s own guard (which would never allow this) — proving the
    /// defensive check in <c>ReactivateUserCommandHandler</c> genuinely fires rather than relying on
    /// it being merely "probably fine."
    /// </summary>
    [Fact]
    public async Task ReactivatingAnOwner_Returns400_EvenThoughThisShouldBeUnreachableInNormalUse()
    {
        var (ownerClient, ownerAuth) = await RegisterBusinessAsync("role-reactivate-owner-guard");

        using (var scope = Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // ownerAuth.UserId is the identity id, which RegisterCommandHandler also uses as the
            // membership's own Id (Id == UserId for every row created today, single-membership
            // world) — so this still targets the right row by primary key.
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"UserBusinessMemberships\" SET \"Status\" = 'Deactivated' WHERE \"Id\" = {ownerAuth.UserId}");
        }

        var reactivateResponse = await ownerClient.PostAsync($"/api/users/{ownerAuth.UserId}/reactivate", null);

        reactivateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
