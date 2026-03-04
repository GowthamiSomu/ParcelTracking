using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParcelTracking.Infrastructure.Persistence;

namespace ParcelTracking.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string DbName = "IntegrationTestDb";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=.;Database=IntegrationTestDb;",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["ConnectionStrings:ServiceBus"] =
                    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;" +
                    "SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
                ["ServiceBus:AnomalyQueue"]         = "parcel-anomalies",
                ["ServiceBus:ScanEventsQueue"]       = "parcel-scan-events",
                ["ServiceBus:MaxConcurrentSessions"] = "1",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ParcelTrackingDbContext>));
            services.RemoveAll(typeof(DbContextOptions));
            services.RemoveAll(typeof(ParcelTrackingDbContext));

            services.AddDbContext<ParcelTrackingDbContext>(options =>
                options.UseInMemoryDatabase(DbName));
        });
    }
}
