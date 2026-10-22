using System.Reflection;
using FluentValidation;
using InvoiceTracker.Application.Common.Behaviors;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceTracker.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // Application-layer service (not Infrastructure): only orchestrates IApplicationDbContext
        // and IPdfGenerator, both already visible from here. See IInvoicePdfService's doc comment.
        services.AddScoped<IInvoicePdfService, InvoicePdfService>();

        return services;
    }
}
