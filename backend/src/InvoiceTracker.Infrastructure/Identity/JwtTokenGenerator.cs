using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using InvoiceTracker.Application.Common.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace InvoiceTracker.Infrastructure.Identity;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    // Claim carried only by a preauth token (see IJwtTokenGenerator's doc comment) — checked both
    // here (ValidatePreAuthToken) and in DependencyInjection.AddJwtBearer's OnTokenValidated event,
    // which rejects any bearer token carrying it from every other endpoint.
    private const string PreAuthClaimType = "preauth";
    private const string PreAuthClaimValue = "true";
    private const int PreAuthTokenExpiryMinutes = 5;

    private readonly JwtSettings _settings;

    public JwtTokenGenerator(IOptions<JwtSettings> options)
    {
        _settings = options.Value;
    }

    public AuthToken GenerateToken(Guid userId, Guid businessId, string role, string email)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            // Custom claim — resolved server-side by ICurrentUserService, never trusted from the client.
            new Claim("businessId", businessId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return new AuthToken(accessToken, expiresAtUtc);
    }

    public PreAuthToken GeneratePreAuthToken(Guid userId)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(PreAuthTokenExpiryMinutes);

        // Deliberately no businessId/role claims — a preauth token doesn't identify a business
        // membership yet, only the identity waiting to pick one.
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(PreAuthClaimType, PreAuthClaimValue),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return new PreAuthToken(accessToken, expiresAtUtc);
    }

    public Guid? ValidatePreAuthToken(string token)
    {
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(token, validationParameters, out _);

            var isPreAuthToken = principal.FindFirstValue(PreAuthClaimType) == PreAuthClaimValue;
            if (!isPreAuthToken)
            {
                // Signature/expiry were fine, but this is a normal access token (or something else
                // entirely) — never a valid select-business input.
                return null;
            }

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out var userId) ? userId : null;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // ValidateToken throws ArgumentException (not a SecurityTokenException) for a
            // structurally malformed token string (e.g. not even JWT-shaped) — still just an
            // invalid preauth token from this method's point of view.
            return null;
        }
    }
}
