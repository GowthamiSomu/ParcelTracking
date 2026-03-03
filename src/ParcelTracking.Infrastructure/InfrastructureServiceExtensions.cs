using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParcelTracking.Infrastructure.Abstractions;
using ParcelTracking.Infrastructure.Idempotency;
using ParcelTracking.Infrastructure.Messaging;
using ParcelTracking.Infrastructure.Persistence;
using ParcelTracking.Infrastructure.Repositories;
using StackExchange.Redis;

namespace ParcelTracking.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // EF Core – SQL Server
        services.AddDbContext<ParcelTrackingDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("SqlServer"),
                sql => sql.EnableRetryOnFailure(maxRetryCount: 5)));

        // Repositories
        services.AddScoped<IParcelRepository, ParcelRepository>();
        services.AddScoped<IScanEventRepository, ScanEventRepository>();

        // Redis idempotency store
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("Redis connection string not configured.")));
        services.AddSingleton<IEventIdempotencyStore, RedisEventIdempotencyStore>();

        // Azure Service Bus anomaly publisher
        var sbConnectionString = configuration.GetConnectionString("ServiceBus")
            ?? throw new InvalidOperationException("ServiceBus connection string not configured.");
        var anomalyQueue = configuration["ServiceBus:AnomalyQueue"] ?? "parcel-anomalies";

        services.AddSingleton(_ =>
        {
            var client = new ServiceBusClient(sbConnectionString);
            return client.CreateSender(anomalyQueue);
        });
        services.AddSingleton<IAnomalyEventPublisher, ServiceBusAnomalyEventPublisher>();

        return services;
    }
}
