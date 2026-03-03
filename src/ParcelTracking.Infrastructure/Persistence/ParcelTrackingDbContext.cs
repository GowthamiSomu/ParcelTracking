using Microsoft.EntityFrameworkCore;
using ParcelTracking.Domain.Enums;
using ParcelTracking.Domain.Models;

namespace ParcelTracking.Infrastructure.Persistence;

public sealed class ParcelTrackingDbContext : DbContext
{
    public ParcelTrackingDbContext(DbContextOptions<ParcelTrackingDbContext> options)
        : base(options) { }

    public DbSet<ParcelEntity> Parcels => Set<ParcelEntity>();
    public DbSet<ScanEventEntity> ScanEvents => Set<ScanEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ParcelTrackingDbContext).Assembly);
    }
}

// ---------------------------------------------------------------------------
// Flat EF entities (owned types for value objects)
// ---------------------------------------------------------------------------

public sealed class ParcelEntity
{
    public string TrackingId { get; set; } = string.Empty;
    public ParcelStatus Status { get; set; }
    public SizeClass SizeClass { get; set; }

    // Dimensions (owned)
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal WeightKg { get; set; }

    // FromAddress (owned)
    public string FromLine1 { get; set; } = string.Empty;
    public string FromCity { get; set; } = string.Empty;
    public string FromPostcode { get; set; } = string.Empty;
    public string FromCountry { get; set; } = string.Empty;

    // ToAddress (owned)
    public string ToLine1 { get; set; } = string.Empty;
    public string ToCity { get; set; } = string.Empty;
    public string ToPostcode { get; set; } = string.Empty;
    public string ToCountry { get; set; } = string.Empty;

    // Sender
    public string SenderName { get; set; } = string.Empty;
    public string SenderContactNumber { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;

    // Receiver
    public string ReceiverName { get; set; } = string.Empty;
    public string ReceiverContactNumber { get; set; } = string.Empty;
    public string ReceiverEmail { get; set; } = string.Empty;
    public bool ReceiverNotificationOptIn { get; set; }

    // Charges (decimal columns — never float)
    public decimal BaseCharge { get; set; }
    public decimal LargeSurcharge { get; set; }
    public decimal TotalCharge { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ScanEventEntity
{
    public string EventId { get; set; } = string.Empty;
    public string TrackingId { get; set; } = string.Empty;
    public ParcelStatus EventType { get; set; }
    public DateTime EventTimeUtc { get; set; }
    public string LocationId { get; set; } = string.Empty;
    public string? HubType { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
}
