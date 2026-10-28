using System.Threading.RateLimiting;
using InvoiceTracker.Api.BackgroundServices;
using InvoiceTracker.Api.Middleware;
using InvoiceTracker.Api.Serialization;
using InvoiceTracker.Api.Startup;
using InvoiceTracker.Application;
using InvoiceTracker.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    // Normalizes every JSON-body-bound DateTime (e.g. CreateInvoiceRequest.DueDate) to Kind = Utc on
    // read, so a bare date-only string (what an HTML <input type="date"> sends) doesn't crash
    // Npgsql's deliberate rejection of Kind = Unspecified values against timestamptz columns. See
    // UtcDateTimeJsonConverter's doc comment. Note: this only covers JSON-body binding — [FromQuery]
    // DateTime params (e.g. InvoicesController.List's dateFrom/dateTo) go through ASP.NET Core's own
    // DateTime.TryParse-based model binder, not this converter, and are normalized separately at the
    // point they reach EF Core (see ListInvoicesQueryHandler).
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Lets Swagger UI's "Authorize" button attach a bearer token for the routes that need one
    // (every route except AuthController's four, which are [AllowAnonymous]).
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer {your JWT}'"
    });
    // Microsoft.OpenApi v2 (pulled in transitively by Swashbuckle.AspNetCore 10.x) replaced the old
    // OpenApiSecurityScheme-with-a-Reference-property pattern with a dedicated reference type
    // (OpenApiSecuritySchemeReference) and changed AddSecurityRequirement to a document-aware
    // factory delegate, since OpenApiSecurityRequirement's dictionary key now needs the hosting
    // OpenApiDocument to resolve the "Bearer" scheme registered above via AddSecurityDefinition.
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// Lets the Vite dev server (and, later, whatever origin the deployed frontend lands on) call this
// API from a browser — browsers block cross-origin fetches without CORS headers regardless of
// whether the API itself would accept the request. Origins come from config (Cors:AllowedOrigins),
// not a hardcoded string, so dev/prod can differ without a code change. No AllowCredentials(): the
// frontend sends a Bearer token in a header, not a cookie, so credentialed CORS isn't needed, and
// skipping it avoids the classic (and here, pointless) AllowCredentials + broad-origin misconfiguration.
const string FrontendCorsPolicy = "FrontendCorsPolicy";
var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins(corsAllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// The other half of the Kafka pipeline (KafkaEventPublisher, OverdueNotificationConsumer) is
// registered inside AddInfrastructure, since those are Infrastructure-layer adapters. These two
// live in the Api project itself (see architecture.md's folder layout) because they only depend
// on IApplicationDbContext/IEventPublisher, not on Kafka directly.
builder.Services.AddHostedService<OverdueInvoiceScannerService>();
builder.Services.AddHostedService<OutboxRelayService>();

// Every endpoint requires an authenticated caller by default, from here on — controllers/routes
// that shouldn't (just AuthController's four routes, for now) opt out with [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Brute-force protection for the auth endpoints most worth throttling (login, register,
// forgot/reset/change-password — see AuthController). Deliberately scoped to just those actions via
// [EnableRateLimiting(...)] on each one, not applied API-wide. Fixed-window, partitioned per client
// IP, so one caller hammering /api/auth/login can't exhaust another caller's quota. Limit/window are
// configurable (RateLimiting:Auth:PermitLimit/WindowSeconds) so appsettings.Development.json — which
// the integration test suite also runs under, see
// IntegrationTestWebApplicationFactory.UseEnvironment("Development") — can set a much higher limit
// than production without any test needing to know about rate limiting at all. The policy factory
// below deliberately resolves IConfiguration from httpContext.RequestServices at request time
// instead of capturing a value read from builder.Configuration up front: WebApplicationFactory-based
// tests (see RateLimitingTests) append their own config override via WithWebHostBuilder, which is
// only merged into the final IConfiguration at the point the host is built — reading eagerly here
// would silently bake in the pre-override value instead.
builder.Services.AddRateLimiter(options =>
{
    // Default is 503; a plain, un-queued 429 is the clean, expected rejection response here.
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimiterPolicyNames.Auth, httpContext =>
    {
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var permitLimit = configuration.GetValue<int?>("RateLimiting:Auth:PermitLimit") ?? 10;
        var windowSeconds = configuration.GetValue<int?>("RateLimiting:Auth:WindowSeconds") ?? 60;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

// Guards against an unset/placeholder JWT signing secret ever being used outside Development — see
// JwtSecretGuard's own doc comment. Runs after configuration is built (app.Configuration reflects
// every provider, including env var overrides) but before the app starts serving requests.
JwtSecretGuard.EnsureConfigured(app.Environment.EnvironmentName, app.Configuration["Jwt:Secret"]);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

// Must come before UseHttpsRedirection, matching ASP.NET Core's documented HSTS ordering — HSTS tells
// the browser to upgrade future requests to HTTPS itself, so it only makes sense outside Development
// (where the dev cert isn't something a real browser trusts) and should be set before the redirect
// middleware runs.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();

app.Run();

// Testability marker: top-level-statement Program.cs compiles to an `internal sealed partial
// class Program`, which WebApplicationFactory<Program> (used by InvoiceTracker.IntegrationTests,
// a different assembly) can't reference without this. Standard, Microsoft-documented fix —
// non-behavioral.
public partial class Program { }
