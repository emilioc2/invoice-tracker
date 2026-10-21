using InvoiceTracker.Api.BackgroundServices;
using InvoiceTracker.Api.Middleware;
using InvoiceTracker.Application;
using InvoiceTracker.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
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
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Testability marker: top-level-statement Program.cs compiles to an `internal sealed partial
// class Program`, which WebApplicationFactory<Program> (used by InvoiceTracker.IntegrationTests,
// a different assembly) can't reference without this. Standard, Microsoft-documented fix —
// non-behavioral.
public partial class Program { }
