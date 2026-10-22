using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.IntegrationTests.Infrastructure;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// Full-HTTP coverage of the four Auth routes (architecture.md's integration-test checklist):
/// register creates Business+Owner, login succeeds/fails correctly, and the forgot/reset-password
/// round trip works end-to-end against a fake <see cref="InvoiceTracker.IntegrationTests.Infrastructure.TestEmailSender"/>
/// (never a real Formspree call).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AuthTests : IntegrationTestBase
{
    public AuthTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Register_CreatesBusinessAndOwner_ReturnsUsableToken()
    {
        var client = CreateClient();
        var ownerEmail = UniqueEmail("owner");

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            BusinessName = "Acme Consulting",
            BusinessEmail = UniqueEmail("biz"),
            OwnerEmail = ownerEmail,
            Password = "P@ssw0rd123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var auth = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        auth.Should().NotBeNull();
        auth!.AccessToken.Should().NotBeNullOrWhiteSpace();
        auth.Email.Should().Be(ownerEmail);
        auth.Role.Should().Be("Owner");
        auth.BusinessId.Should().NotBeEmpty();
        auth.UserId.Should().NotBeEmpty();

        // The returned token is actually usable against a real protected route.
        Authorize(client, auth.AccessToken);
        var dashboardResponse = await client.GetAsync("/api/dashboard/summary");
        dashboardResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_DuplicateOwnerEmail_Fails()
    {
        var client = CreateClient();
        var ownerEmail = UniqueEmail("dup-owner");

        var firstResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            BusinessName = "First Business",
            BusinessEmail = UniqueEmail("biz"),
            OwnerEmail = ownerEmail,
            Password = "P@ssw0rd123!"
        });
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            BusinessName = "Second Business",
            BusinessEmail = UniqueEmail("biz"),
            OwnerEmail = ownerEmail,
            Password = "P@ssw0rd123!"
        });

        secondResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_Succeeds()
    {
        var password = "P@ssw0rd123!";
        var (_, auth) = await RegisterBusinessAsync("login-ok", password);

        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = auth.Email,
            Password = password
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginResult = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        loginResult!.Email.Should().Be(auth.Email);
        loginResult.BusinessId.Should().Be(auth.BusinessId);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var (_, auth) = await RegisterBusinessAsync("login-bad", "P@ssw0rd123!");

        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = auth.Email,
            Password = "TotallyWrongPassword123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = UniqueEmail("nobody"),
            Password = "WhateverPassword123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ForgotPassword_ThenResetPassword_RoundTripsAndAllowsLoginWithNewPassword()
    {
        var originalPassword = "P@ssw0rd123!";
        var (_, auth) = await RegisterBusinessAsync("reset-flow", originalPassword);

        var client = CreateClient();

        var forgotResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = auth.Email });
        forgotResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Never a real Formspree call — the token comes back out of the fake IEmailSender's
        // captured message body, the same one the real app would have emailed to the user.
        var sentEmail = Factory.EmailSender.FindLatestTo(auth.Email);
        sentEmail.Should().NotBeNull("ForgotPassword should have sent a reset token via IEmailSender");

        // Matches ForgotPasswordCommandHandler's email body:
        // "Use the following token to reset your Eternity Invoicing password: {token}".
        var tokenMatch = Regex.Match(sentEmail!.Body, "password: (?<token>.+)$");
        tokenMatch.Success.Should().BeTrue("the fake email body should contain the reset token");
        var resetToken = tokenMatch.Groups["token"].Value;

        var newPassword = "NewP@ssw0rd456!";
        var resetResponse = await client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            Email = auth.Email,
            Token = resetToken,
            NewPassword = newPassword
        });
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Old password no longer works, new one does.
        var oldLoginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Email = auth.Email, Password = originalPassword });
        oldLoginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newLoginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Email = auth.Email, Password = newPassword });
        newLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_StillReturnsGenericOk()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = UniqueEmail("unknown") });

        // Deliberately generic — never confirms/denies whether the email matched an account.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_NewOwner_MustChangePasswordIsFalse()
    {
        var (_, auth) = await RegisterBusinessAsync("mcp-register");

        auth.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task Login_AfterCreateStaffUser_MustChangePasswordIsTrue_UntilChanged()
    {
        var (ownerClient, _) = await RegisterBusinessAsync("mcp-staff");
        var staffEmail = UniqueEmail("mcp-staff-member");

        var createResponse = await ownerClient.PostAsJsonAsync("/api/users", new { Email = staffEmail });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var tempPassword = ExtractTempPassword(staffEmail);

        var staffClient = CreateClient();
        var loginResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffEmail, Password = tempPassword });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var staffAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        staffAuth!.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_WithoutAToken_ReturnsUnauthorized()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            CurrentPassword = "Whatever1",
            NewPassword = "NewPassword1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_ReturnsBadRequest_AndMustChangePasswordStaysTrue()
    {
        var (ownerClient, _) = await RegisterBusinessAsync("mcp-wrong-current");
        var staffEmail = UniqueEmail("mcp-wrong-current-staff");
        var createResponse = await ownerClient.PostAsJsonAsync("/api/users", new { Email = staffEmail });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var tempPassword = ExtractTempPassword(staffEmail);

        var staffClient = CreateClient();
        var loginResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffEmail, Password = tempPassword });
        var staffAuth = (await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        Authorize(staffClient, staffAuth.AccessToken);

        var changeResponse = await staffClient.PostAsJsonAsync("/api/auth/change-password", new
        {
            CurrentPassword = "TotallyWrongPassword1",
            NewPassword = "BrandNewPassword1"
        });

        changeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var reLoginResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffEmail, Password = tempPassword });
        reLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reLoginAuth = await reLoginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        reLoginAuth!.MustChangePassword.Should().BeTrue("a rejected change must leave the flag untouched");
    }

    [Fact]
    public async Task StaffUser_LogsInWithTempPassword_ChangesPassword_ThenMustChangePasswordIsFalse()
    {
        var (ownerClient, _) = await RegisterBusinessAsync("mcp-e2e");
        var staffEmail = UniqueEmail("mcp-e2e-staff");
        var createResponse = await ownerClient.PostAsJsonAsync("/api/users", new { Email = staffEmail });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var tempPassword = ExtractTempPassword(staffEmail);

        var staffClient = CreateClient();
        var loginResponse = await staffClient.PostAsJsonAsync("/api/auth/login", new { Email = staffEmail, Password = tempPassword });
        var staffAuth = (await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        staffAuth.MustChangePassword.Should().BeTrue();
        Authorize(staffClient, staffAuth.AccessToken);

        const string newPassword = "BrandNewPassword1";
        var changeResponse = await staffClient.PostAsJsonAsync("/api/auth/change-password", new
        {
            CurrentPassword = tempPassword,
            NewPassword = newPassword
        });
        changeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var finalLoginClient = CreateClient();
        var finalLoginResponse = await finalLoginClient.PostAsJsonAsync("/api/auth/login", new { Email = staffEmail, Password = newPassword });
        finalLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalAuth = await finalLoginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        finalAuth!.MustChangePassword.Should().BeFalse();

        // The temp password no longer works, either.
        var oldPasswordLogin = await CreateClient().PostAsJsonAsync("/api/auth/login", new { Email = staffEmail, Password = tempPassword });
        oldPasswordLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Pulls the auto-generated temp password out of the fake <see cref="TestEmailSender"/>'s
    /// captured message for <paramref name="staffEmail"/> — matches
    /// <c>CreateStaffUserCommandHandler</c>'s email body: "... Temporary password: {tempPassword}.
    /// Please log in ...".
    /// </summary>
    private string ExtractTempPassword(string staffEmail)
    {
        var sentEmail = Factory.EmailSender.FindLatestTo(staffEmail);
        sentEmail.Should().NotBeNull("CreateStaffUser should have emailed a temp password via IEmailSender");

        var match = Regex.Match(sentEmail!.Body, @"Temporary password: (?<password>[^.\s]+)\.");
        match.Success.Should().BeTrue("the fake email body should contain the temp password");
        return match.Groups["password"].Value;
    }
}
