using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Users.Common;
using InvoiceTracker.IntegrationTests.Infrastructure;
using InvoiceTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// Full-HTTP coverage of requirements.md's "Multi-business membership" section and
/// architecture.md's matching test-plan bullets: multi-business login (select-business),
/// switch-business, register-with-an-already-registered-email, "Add a business", Staff-invite to
/// an existing identity, and that deactivating one membership doesn't affect a different business's
/// membership for the same identity.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class MultiBusinessMembershipTests : IntegrationTestBase
{
    private const string Password = "P@ssw0rd123!";

    public MultiBusinessMembershipTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    private static HttpClient WithToken(HttpClient client, string? accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    [Fact]
    public async Task Login_WithExactlyOneActiveMembership_BehavesExactlyAsBefore()
    {
        var (_, auth) = await RegisterBusinessAsync("mb-single-login", Password);

        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { Email = auth.Email, Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        result!.RequiresBusinessSelection.Should().BeFalse();
        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.PreAuthToken.Should().BeNull();
        result.Memberships.Should().BeNull();
        result.BusinessId.Should().Be(auth.BusinessId);
        result.Role.Should().Be("Owner");
    }

    [Fact]
    public async Task Login_WithTwoActiveMemberships_RequiresBusinessSelection_WithBothListed()
    {
        var (ownerClientA, authA) = await RegisterBusinessAsync("mb-multi-login-a", Password);

        var addBusinessResponse = await ownerClientA.PostAsJsonAsync("/api/auth/businesses", new
        {
            BusinessName = $"MB Multi Login B {Guid.NewGuid():N}",
            BusinessEmail = UniqueEmail("mb-multi-login-b")
        });
        addBusinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var addBusinessAuth = (await addBusinessResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;

        var client = CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Email = authA.Email, Password });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        loginResult!.RequiresBusinessSelection.Should().BeTrue();
        loginResult.AccessToken.Should().BeNull();
        loginResult.PreAuthToken.Should().NotBeNullOrWhiteSpace();
        loginResult.Memberships.Should().HaveCount(2);
        loginResult.Memberships!.Select(m => m.BusinessId).Should()
            .BeEquivalentTo(new[] { authA.BusinessId!.Value, addBusinessAuth.BusinessId!.Value });
        loginResult.Memberships!.Should().ContainSingle(m => m.BusinessId == authA.BusinessId && m.Role == "Owner");
        loginResult.Memberships!.Should().ContainSingle(m => m.BusinessId == addBusinessAuth.BusinessId && m.Role == "Owner");
    }

    [Fact]
    public async Task SelectBusiness_WithValidPreAuthToken_AndABusinessTheIdentityBelongsTo_Succeeds()
    {
        var (ownerClientA, authA) = await RegisterBusinessAsync("mb-select-ok-a", Password);
        var addBusinessResponse = await ownerClientA.PostAsJsonAsync("/api/auth/businesses", new
        {
            BusinessName = $"MB Select Ok B {Guid.NewGuid():N}",
            BusinessEmail = UniqueEmail("mb-select-ok-b")
        });
        var addBusinessAuth = (await addBusinessResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;

        var client = CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Email = authA.Email, Password });
        var loginResult = (await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;

        var selectResponse = await client.PostAsJsonAsync("/api/auth/select-business", new
        {
            PreAuthToken = loginResult.PreAuthToken,
            BusinessId = authA.BusinessId
        });

        selectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var selectResult = await selectResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        selectResult!.AccessToken.Should().NotBeNullOrWhiteSpace();
        selectResult.BusinessId.Should().Be(authA.BusinessId);
        selectResult.Role.Should().Be("Owner");
        selectResult.RequiresBusinessSelection.Should().BeFalse();

        // The returned token is genuinely usable against a real protected route.
        WithToken(client, selectResult.AccessToken);
        var dashboardResponse = await client.GetAsync("/api/dashboard/summary");
        dashboardResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SelectBusiness_WithABusinessTheIdentityDoesNotBelongTo_IsRejected_EvenWithAStructurallyValidPreAuthToken()
    {
        var (ownerClientA, authA) = await RegisterBusinessAsync("mb-select-reject-a", Password);
        await ownerClientA.PostAsJsonAsync("/api/auth/businesses", new
        {
            BusinessName = $"MB Select Reject B {Guid.NewGuid():N}",
            BusinessEmail = UniqueEmail("mb-select-reject-b")
        });

        var client = CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Email = authA.Email, Password });
        var loginResult = (await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;

        // A different business entirely — the identity has no membership in it at all, but the
        // preauth token itself (signature/expiry/claim) is perfectly valid.
        var (_, foreignAuth) = await RegisterBusinessAsync("mb-select-reject-foreign", Password);

        var selectResponse = await client.PostAsJsonAsync("/api/auth/select-business", new
        {
            PreAuthToken = loginResult.PreAuthToken,
            BusinessId = foreignAuth.BusinessId
        });

        selectResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SwitchBusiness_ForAMembershipTheCallerActuallyHolds_ReturnsATokenScopedToIt()
    {
        var (ownerClientA, authA) = await RegisterBusinessAsync("mb-switch-ok-a", Password);
        var addBusinessResponse = await ownerClientA.PostAsJsonAsync("/api/auth/businesses", new
        {
            BusinessName = $"MB Switch Ok B {Guid.NewGuid():N}",
            BusinessEmail = UniqueEmail("mb-switch-ok-b")
        });
        var addBusinessAuth = (await addBusinessResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;

        // ownerClientA's own token is already scoped to Business B (AddBusiness behaves like
        // switch-business into the new business) — switch it back to Business A explicitly.
        var switchResponse = await ownerClientA.PostAsJsonAsync("/api/auth/switch-business", new { BusinessId = authA.BusinessId });

        switchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var switchResult = await switchResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        switchResult!.BusinessId.Should().Be(authA.BusinessId);
        switchResult.Role.Should().Be("Owner");
        switchResult.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SwitchBusiness_ForABusinessTheCallerHasNoActiveMembershipIn_Returns404()
    {
        var (ownerClientA, _) = await RegisterBusinessAsync("mb-switch-404-a", Password);
        var (_, foreignAuth) = await RegisterBusinessAsync("mb-switch-404-foreign", Password);

        var switchResponse = await ownerClientA.PostAsJsonAsync("/api/auth/switch-business", new { BusinessId = foreignAuth.BusinessId });

        switchResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AJwtIssuedForBusinessA_IsRejected_WhenUsedToCallAResourceScopedToBusinessB()
    {
        var (ownerClientA, authA) = await RegisterBusinessAsync("mb-cross-scope-a", Password);

        // Create a resource under Business A, using the token still scoped to A.
        var createClientResponseA = await ownerClientA.PostAsJsonAsync("/api/clients", new
        {
            Name = "Business A's Client",
            Email = UniqueEmail("mb-cross-scope-a-client"),
            Phone = "5551111",
            Address = "1 Business A St"
        });
        createClientResponseA.StatusCode.Should().Be(HttpStatusCode.Created);
        var clientA = (await createClientResponseA.Content.ReadFromJsonAsync<ClientDto>())!;
        var tokenA = ownerClientA.DefaultRequestHeaders.Authorization!.Parameter;

        // Add a second business for the same identity, and create a resource under it while
        // scoped to B (AddBusiness's own returned token).
        var addBusinessResponse = await ownerClientA.PostAsJsonAsync("/api/auth/businesses", new
        {
            BusinessName = $"MB Cross Scope B {Guid.NewGuid():N}",
            BusinessEmail = UniqueEmail("mb-cross-scope-b")
        });
        var addBusinessAuth = (await addBusinessResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        var clientB = WithToken(CreateClient(), addBusinessAuth.AccessToken);

        var createClientResponseB = await clientB.PostAsJsonAsync("/api/clients", new
        {
            Name = "Business B's Client",
            Email = UniqueEmail("mb-cross-scope-b-client"),
            Phone = "5552222",
            Address = "1 Business B St"
        });
        createClientResponseB.StatusCode.Should().Be(HttpStatusCode.Created);
        var clientBDto = (await createClientResponseB.Content.ReadFromJsonAsync<ClientDto>())!;

        // The original Business-A-scoped token can't reach Business B's resource...
        var clientAUsingTokenA = WithToken(CreateClient(), tokenA);
        var crossResponse = await clientAUsingTokenA.GetAsync($"/api/clients/{clientBDto.Id}");
        crossResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // ...and Business B's token can't reach Business A's resource either — proving a switch
        // requires a genuinely new, separately-scoped token rather than one token spanning both.
        var crossResponseReverse = await clientB.GetAsync($"/api/clients/{clientA.Id}");
        crossResponseReverse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Sanity check: each token still works fine against its own business's resource.
        (await clientAUsingTokenA.GetAsync($"/api/clients/{clientA.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientB.GetAsync($"/api/clients/{clientBDto.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_WithAnAlreadyRegisteredEmail_Returns409_AndCreatesNoNewBusinessIdentityOrMembership()
    {
        var (_, authA) = await RegisterBusinessAsync("mb-register-conflict", Password);

        using (var scope = Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var businessCountBefore = await dbContext.Businesses.CountAsync();
            var membershipCountBefore = await dbContext.UserBusinessMemberships.CountAsync();

            var client = CreateClient();
            var response = await client.PostAsJsonAsync("/api/auth/register", new
            {
                BusinessName = "Should Not Be Created",
                BusinessEmail = UniqueEmail("mb-register-conflict-biz2"),
                OwnerEmail = authA.Email,
                Password
            });

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var businessCountAfter = await dbContext.Businesses.CountAsync();
            var membershipCountAfter = await dbContext.UserBusinessMemberships.CountAsync();
            businessCountAfter.Should().Be(businessCountBefore, "no new Business should be created on a 409");
            membershipCountAfter.Should().Be(membershipCountBefore, "no new UserBusinessMembership should be created on a 409");

            // Original membership is still there, untouched, and still logs in fine.
            var stillWorksResponse = await CreateClient().PostAsJsonAsync("/api/auth/login", new { Email = authA.Email, Password });
            stillWorksResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AddBusiness_ForAnExistingIdentity_Succeeds_AndDoesNotDisturbItsExistingMembership()
    {
        var (ownerClientA, authA) = await RegisterBusinessAsync("mb-add-business-a", Password);

        var addBusinessResponse = await ownerClientA.PostAsJsonAsync("/api/auth/businesses", new
        {
            BusinessName = $"MB Add Business B {Guid.NewGuid():N}",
            BusinessEmail = UniqueEmail("mb-add-business-b")
        });

        addBusinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var addBusinessAuth = (await addBusinessResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        addBusinessAuth.BusinessId.Should().NotBe(authA.BusinessId!.Value);
        addBusinessAuth.Role.Should().Be("Owner");
        addBusinessAuth.UserId.Should().Be(authA.UserId);

        // The original membership in Business A is untouched — can still switch back to it and
        // it still shows up as Active/Owner.
        var switchBackResponse = await ownerClientA.PostAsJsonAsync("/api/auth/switch-business", new { BusinessId = authA.BusinessId });
        switchBackResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var switchBackResult = await switchBackResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        switchBackResult!.BusinessId.Should().Be(authA.BusinessId);
        switchBackResult.Role.Should().Be("Owner");
    }

    [Fact]
    public async Task DeactivatingAMembershipInOneBusiness_DoesNotAffectTheSameIdentitysMembership_OrAbilityToLogIn_InADifferentBusiness()
    {
        var (ownerClientA, authA) = await RegisterBusinessAsync("mb-deactivate-isolation-a", Password);

        var staffEmail = UniqueEmail("mb-deactivate-isolation-staff");
        var createStaffResponse = await ownerClientA.PostAsJsonAsync("/api/users", new { Email = staffEmail });
        createStaffResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var staffUserInA = (await createStaffResponse.Content.ReadFromJsonAsync<UserDto>())!;

        var tempPassword = ExtractTempPassword(staffEmail);

        // Staff logs in (single active membership so far — unchanged login shape) and adds a
        // second business for itself, becoming Owner of it too.
        var staffClient = CreateClient();
        var staffLoginResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffEmail, Password = tempPassword });
        staffLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffAuth = (await staffLoginResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        WithToken(staffClient, staffAuth.AccessToken);

        var addBusinessResponse = await staffClient.PostAsJsonAsync("/api/auth/businesses", new
        {
            BusinessName = $"MB Deactivate Isolation B {Guid.NewGuid():N}",
            BusinessEmail = UniqueEmail("mb-deactivate-isolation-b")
        });
        addBusinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var businessBAuth = (await addBusinessResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;

        // Owner of Business A deactivates the Staff membership in A.
        var deactivateResponse = await ownerClientA.DeleteAsync($"/api/users/{staffUserInA.Id}");
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The same identity's login into Business B is unaffected — now back to exactly one
        // active membership (Owner of B), so login behaves normally and succeeds.
        var reLoginResponse = await CreateClient().PostAsJsonAsync("/api/auth/login", new { Email = staffEmail, Password = tempPassword });
        reLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reLoginResult = await reLoginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        reLoginResult!.RequiresBusinessSelection.Should().BeFalse();
        reLoginResult.BusinessId.Should().Be(businessBAuth.BusinessId);
        reLoginResult.Role.Should().Be("Owner");

        // And Business A itself still shows the membership as Deactivated (not deleted).
        var usersInAResponse = await ownerClientA.GetAsync("/api/users");
        var usersInA = await usersInAResponse.Content.ReadFromJsonAsync<List<UserDto>>();
        usersInA!.Single(u => u.Id == staffUserInA.Id).Status.Should().Be("Deactivated");
    }

    [Fact]
    public async Task ADeactivatedMembership_NeverAppearsInAMultiMembershipLoginsMembershipsList()
    {
        var (ownerClientA, authA) = await RegisterBusinessAsync("mb-deactivated-hidden-a", Password);

        var staffEmail = UniqueEmail("mb-deactivated-hidden-staff");
        var createStaffResponse = await ownerClientA.PostAsJsonAsync("/api/users", new { Email = staffEmail });
        var staffUserInA = (await createStaffResponse.Content.ReadFromJsonAsync<UserDto>())!;
        var tempPassword = ExtractTempPassword(staffEmail);

        var staffClient = CreateClient();
        var staffLoginResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffEmail, Password = tempPassword });
        var staffAuth = (await staffLoginResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        WithToken(staffClient, staffAuth.AccessToken);

        // Add two more businesses so the identity ends up with three active memberships total
        // (Staff-A, Owner-B, Owner-C) before A gets deactivated.
        var addBResponse = await staffClient.PostAsJsonAsync("/api/auth/businesses", new
        {
            BusinessName = $"MB Deactivated Hidden B {Guid.NewGuid():N}",
            BusinessEmail = UniqueEmail("mb-deactivated-hidden-b")
        });
        var authB = (await addBResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        WithToken(staffClient, authB.AccessToken);

        var addCResponse = await staffClient.PostAsJsonAsync("/api/auth/businesses", new
        {
            BusinessName = $"MB Deactivated Hidden C {Guid.NewGuid():N}",
            BusinessEmail = UniqueEmail("mb-deactivated-hidden-c")
        });
        var authC = (await addCResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;

        // Now deactivate the Staff membership in Business A — two active memberships remain (B, C).
        var deactivateResponse = await ownerClientA.DeleteAsync($"/api/users/{staffUserInA.Id}");
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var loginResponse = await CreateClient().PostAsJsonAsync("/api/auth/login", new { Email = staffEmail, Password = tempPassword });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();

        loginResult!.RequiresBusinessSelection.Should().BeTrue();
        loginResult.Memberships.Should().HaveCount(2, "the Deactivated Business A membership must not appear");
        loginResult.Memberships!.Select(m => m.BusinessId).Should()
            .BeEquivalentTo(new[] { authB.BusinessId!.Value, authC.BusinessId!.Value });
        loginResult.Memberships!.Should().NotContain(m => m.BusinessId == authA.BusinessId);
    }

    [Fact]
    public async Task PreAuthToken_IsRejected_AgainstGetClients_NotJustDashboard()
    {
        var (ownerClientA, authA) = await RegisterBusinessAsync("mb-preauth-clients-a", Password);
        await ownerClientA.PostAsJsonAsync("/api/auth/businesses", new
        {
            BusinessName = $"MB PreAuth Clients B {Guid.NewGuid():N}",
            BusinessEmail = UniqueEmail("mb-preauth-clients-b")
        });

        var client = CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Email = authA.Email, Password });
        var loginResult = (await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        loginResult.RequiresBusinessSelection.Should().BeTrue();

        WithToken(client, loginResult.PreAuthToken);
        var clientsResponse = await client.GetAsync("/api/clients");

        clientsResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateStaffUser_InvitingAnEmailThatAlreadyHasAnIdentity_AttachesAMembership_WithoutATempPassword_AndWithoutTouchingMustChangePassword()
    {
        // Give the invited identity a MustChangePassword = true starting state via an unrelated
        // earlier temp-password invite (Business A), so this test can prove the second business's
        // invite (Business C) genuinely leaves that flag untouched rather than it coincidentally
        // already being false.
        var (ownerClientA, _) = await RegisterBusinessAsync("mb-attach-existing-a", Password);
        var invitedEmail = UniqueEmail("mb-attach-existing-person");
        var firstInviteResponse = await ownerClientA.PostAsJsonAsync("/api/users", new { Email = invitedEmail });
        firstInviteResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var tempPassword = ExtractTempPassword(invitedEmail);

        // Baseline: this identity currently has MustChangePassword = true.
        var baselineLoginResponse = await CreateClient().PostAsJsonAsync("/api/auth/login", new { Email = invitedEmail, Password = tempPassword });
        baselineLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var baselineAuth = await baselineLoginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        baselineAuth!.MustChangePassword.Should().BeTrue();

        // A second, unrelated business invites the same email.
        var (ownerClientC, authC) = await RegisterBusinessAsync("mb-attach-existing-c", Password);
        var secondInviteResponse = await ownerClientC.PostAsJsonAsync("/api/users", new { Email = invitedEmail });

        secondInviteResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondInviteUser = (await secondInviteResponse.Content.ReadFromJsonAsync<UserDto>())!;
        secondInviteUser.Role.Should().Be("Staff");
        secondInviteUser.Status.Should().Be("Active");

        // No new temp-password email was sent for this second invite — only the original one.
        var allEmailsToInvitee = Factory.EmailSender.SentEmails.Where(m => string.Equals(m.To, invitedEmail, StringComparison.OrdinalIgnoreCase)).ToList();
        allEmailsToInvitee.Should().ContainSingle(m => m.Body.Contains("Temporary password"));
        allEmailsToInvitee.Should().Contain(m => m.Body.Contains("You've been added to") || m.Subject.Contains("added to"));

        // Now two active memberships — login requires selection; select Business C and confirm
        // MustChangePassword is still true there, proving it's identity-level and genuinely
        // untouched by the attach-to-existing-identity path (not coincidentally false).
        var loginResponse = await CreateClient().PostAsJsonAsync("/api/auth/login", new { Email = invitedEmail, Password = tempPassword });
        var loginResult = (await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        loginResult.RequiresBusinessSelection.Should().BeTrue();

        var selectResponse = await CreateClient().PostAsJsonAsync("/api/auth/select-business", new
        {
            PreAuthToken = loginResult.PreAuthToken,
            BusinessId = authC.BusinessId
        });
        selectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var selectResult = await selectResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        selectResult!.MustChangePassword.Should().BeTrue("MustChangePassword lives on the identity, not the membership, and must be untouched by attaching a new membership");
    }

    [Fact]
    public async Task CreateStaffUser_InvitingAnEmailAlreadyAMemberOfTheCallersBusiness_Returns409()
    {
        var (ownerClientA, _) = await RegisterBusinessAsync("mb-attach-conflict-a", Password);
        var staffEmail = UniqueEmail("mb-attach-conflict-staff");

        var firstResponse = await ownerClientA.PostAsJsonAsync("/api/users", new { Email = staffEmail });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondResponse = await ownerClientA.PostAsJsonAsync("/api/users", new { Email = staffEmail });

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Same temp-password extraction helper as <c>AuthTests</c> — pulls the auto-generated temp
    /// password out of the fake <see cref="TestEmailSender"/>'s captured message.
    /// </summary>
    private string ExtractTempPassword(string staffEmail)
    {
        var sentEmail = Factory.EmailSender.FindLatestTo(staffEmail);
        sentEmail.Should().NotBeNull("CreateStaffUser should have emailed a temp password via IEmailSender");

        var match = System.Text.RegularExpressions.Regex.Match(sentEmail!.Body, @"Temporary password: (?<password>[^.\s]+)\.");
        match.Success.Should().BeTrue("the fake email body should contain the temp password");
        return match.Groups["password"].Value;
    }
}
