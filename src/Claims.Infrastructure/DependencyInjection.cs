using Claims.Application.Abstractions;
using Claims.Infrastructure.Messaging;
using Claims.Infrastructure.Persistence;
using Claims.Infrastructure.Security;
using Claims.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Claims.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.Configure<KafkaOptions>(config.GetSection("Kafka"));
        services.Configure<StorageOptions>(config.GetSection("Storage"));

        var provider = config.GetValue<string>("Database:Provider") ?? "SqlServer";
        var connection = config.GetConnectionString("Default")
                         ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<ClaimsDbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                options.UseSqlite(connection, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
            else
                options.UseSqlServer(connection, o =>
                {
                    o.EnableRetryOnFailure();
                    o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                });
        });
        services.AddScoped<IClaimsDbContext>(sp => sp.GetRequiredService<ClaimsDbContext>());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        var kafkaEnabled = config.GetValue<bool>("Kafka:Enabled");
        if (kafkaEnabled)
        {
            services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
            services.AddHostedService<ClaimEventConsumerService>();
        }
        else
        {
            services.AddSingleton<IEventPublisher, LoggingEventPublisher>();
        }
        services.AddHostedService<OutboxPublisherService>();

        return services;
    }
}
