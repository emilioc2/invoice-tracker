using System.Text;
using InvoiceTracker.Application.Common.Interfaces;
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
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;

namespace InvoiceTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
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
            });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        services.Configure<FormspreeSettings>(configuration.GetSection("Formspree"));
        services.AddHttpClient<IEmailSender, FormspreeEmailSender>();

        services.Configure<KafkaSettings>(configuration.GetSection("Kafka"));
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        services.AddHostedService<OverdueNotificationConsumer>();

        services.AddScoped<IPdfGenerator, QuestPdfInvoiceGenerator>();

        return services;
    }
}
