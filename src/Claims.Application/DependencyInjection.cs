using Claims.Application.Services;
using Claims.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Claims.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<ClaimService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<LookupService>();
        services.AddScoped<AutoAssignmentService>();
        services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
        return services;
    }
}
