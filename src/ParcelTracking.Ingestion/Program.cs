using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using ParcelTracking.Infrastructure;
using ParcelTracking.Infrastructure.Persistence;
using ParcelTracking.Ingestion;
using ParcelTracking.Ingestion.Processing;
using ParcelTracking.Notifications.Abstractions;
using ParcelTracking.Notifications.Services;
using ParcelTracking.Rules.Rules;

var builder = Host.CreateApplicationBuilder(args);

// Infrastructure (EF Core, Redis, Service Bus anomaly publisher)
builder.Services.AddInfrastructure(builder.Configuration);

// Notification service (stub — swap for real provider without touching business logic)
builder.Services.AddSingleton<INotificationService, StubNotificationService>();

// Business rules (stateless — registered as singletons for performance)
builder.Services.AddSingleton<CollectionValidationRule>();
builder.Services.AddSingleton<StatusTransitionRule>();

// Scan event processor (scoped — uses scoped DbContext via IServiceScopeFactory)
builder.Services.AddScoped<ScanEventProcessor>();

// Azure Service Bus session processor (sessions guarantee per-parcel ordering)
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("ServiceBus")
        ?? throw new InvalidOperationException("ServiceBus connection string not configured.");
    var queueName = config["ServiceBus:ScanEventsQueue"] ?? "parcel-scan-events";
    var maxConcurrency = config.GetValue<int>("ServiceBus:MaxConcurrentSessions", 50);

    var client = new ServiceBusClient(connectionString);
    return client.CreateSessionProcessor(queueName, new ServiceBusSessionProcessorOptions
    {
        MaxConcurrentSessions = maxConcurrency,
        MaxConcurrentCallsPerSession = 1, // Strict ordering within each session
        AutoCompleteMessages = false,     // We complete/DLQ manually
        MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
    });
});

// Give the host enough time for Service Bus connect on cold start
builder.Services.Configure<HostOptions>(opts =>
{
    opts.StartupTimeout = TimeSpan.FromMinutes(2);
});

// Background service
builder.Services.AddHostedService<ScanEventConsumer>();

var host = builder.Build();

// ── Run EF Core migrations before starting the host ───────────────────────────
// Done outside the host startup pipeline so it never races against startup timeouts.
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ParcelTrackingDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<ParcelTrackingDbContext>>();
    logger.LogInformation("[MIGRATION] Applying pending EF Core migrations...");
    await db.Database.MigrateAsync(CancellationToken.None);
    logger.LogInformation("[MIGRATION] Migrations applied.");
}

host.Run();
