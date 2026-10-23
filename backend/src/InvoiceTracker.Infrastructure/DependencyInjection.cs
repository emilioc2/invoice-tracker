using System.Security.Claims;
using System.Text;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Infrastructure.Email;
using InvoiceTracker.Infrastructure.Identity;
using InvoiceTracker.Infrastructure.Messaging;
using InvoiceTracker.Infrastructure.Pdf;
using InvoiceTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;

namespace InvoiceTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // QuestPDF's Community license requires this to be set once at startup, or it throws at
        // render time. This project is eligible for the free Community license (small
        // team/portfolio project), so set it unconditionally here.
        QuestPDF.Settings.License = LicenseType.Community;

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                // Dev-friendly password policy; RequiredLength/complexity still mirrored by the
                // matching FluentValidation validators in the Application layer.
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));

        // AddIdentity (above) already registered its own cookie scheme as the default
        // authenticate/challenge scheme. Explicitly setting all three Default*Scheme
        // properties here (not just the AddAuthentication(string) shorthand, which only sets
        // DefaultScheme) is required to actually make JWT Bearer win — otherwise unauthenticated
        // requests get redirected to Identity's /Account/Login instead of a clean 401.
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtSettings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                // Preauth tokens (see IJwtTokenGenerator.GeneratePreAuthToken) are only ever valid
                // as the request-body `preAuthToken` field of POST /api/auth/select-business —
                // JwtTokenGenerator validates that one manually. Here, independently, any bearer
                // token carrying the "preauth" claim is rejected from authenticating against every
                // endpoint that goes through the normal JWT bearer pipeline (every protected route,
                // and select-business too if someone tried presenting it as a header there instead
                // of a body field) — see requirements.md's "Multi-business membership" and
                // architecture.md's Auth routes for why a preauth token must never grant normal access.
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var isPreAuthToken = context.Principal?.FindFirstValue("preauth") == "true";
                        if (isPreAuthToken)
                        {
                            context.Fail("Pre-authentication tokens cannot be used to access protected resources.");
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        // ResendSettings is bound unconditionally — harmless, and needed once a real API key is
        // configured.
        services.Configure<ResendSettings>(configuration.GetSection("Resend"));

        // FrontendSettings (Application/Common/Models) — bound here the same way as every other
        // settings class, even though the type itself lives in the Application layer rather than
        // Infrastructure; ForgotPasswordCommandHandler needs it directly to build a clickable
        // reset-password link.
        services.Configure<FrontendSettings>(configuration.GetSection("Frontend"));

        // ResendEmailSender is used whenever a real Resend API key is configured — in any
        // environment, including Development (a deliberate runtime check rather than a purely
        // environment-based split, since the key lives in user-secrets/env vars rather than
        // appsettings.*.json). NoOpEmailSender is the fallback otherwise, including Development
        // with no key configured — it logs what it would have sent (handy for reading a reset
        // token out of the console) without making a real HTTP call.
        var resendApiKey = configuration["Resend:ApiKey"];
        if (!string.IsNullOrWhiteSpace(resendApiKey))
        {
            services.AddHttpClient<IEmailSender, ResendEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, NoOpEmailSender>();
        }

        services.Configure<KafkaSettings>(configuration.GetSection("Kafka"));
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        services.AddHostedService<OverdueNotificationConsumer>();

        services.AddScoped<IPdfGenerator, QuestPdfInvoiceGenerator>();

        return services;
    }
}
