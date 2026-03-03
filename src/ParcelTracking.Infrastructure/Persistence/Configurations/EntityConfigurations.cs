using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParcelTracking.Infrastructure.Persistence;

namespace ParcelTracking.Infrastructure.Persistence.Configurations;

internal sealed class ParcelEntityConfiguration : IEntityTypeConfiguration<ParcelEntity>
{
    public void Configure(EntityTypeBuilder<ParcelEntity> builder)
    {
        builder.HasKey(p => p.TrackingId);
        builder.Property(p => p.TrackingId).HasMaxLength(30);

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.SizeClass).HasConversion<string>().HasMaxLength(10);

        // Decimal precision — never float
        builder.Property(p => p.BaseCharge).HasPrecision(18, 4);
        builder.Property(p => p.LargeSurcharge).HasPrecision(18, 4);
        builder.Property(p => p.TotalCharge).HasPrecision(18, 4);
        builder.Property(p => p.LengthCm).HasPrecision(10, 2);
        builder.Property(p => p.WidthCm).HasPrecision(10, 2);
        builder.Property(p => p.HeightCm).HasPrecision(10, 2);
        builder.Property(p => p.WeightKg).HasPrecision(10, 3);

        builder.Property(p => p.CreatedAt).HasColumnType("datetime2");
        builder.Property(p => p.UpdatedAt).HasColumnType("datetime2");

        builder.ToTable("Parcels");
    }
}

internal sealed class ScanEventEntityConfiguration : IEntityTypeConfiguration<ScanEventEntity>
{
    public void Configure(EntityTypeBuilder<ScanEventEntity> builder)
    {
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).HasMaxLength(100);
        builder.Property(e => e.TrackingId).HasMaxLength(30).IsRequired();
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.LocationId).HasMaxLength(100);
        builder.Property(e => e.HubType).HasMaxLength(50);
        builder.Property(e => e.ActorId).HasMaxLength(100);
        builder.Property(e => e.MetadataJson).HasColumnType("nvarchar(max)");
        builder.Property(e => e.EventTimeUtc).HasColumnType("datetime2");

        // Index for efficient per-parcel event queries (ordered by time)
        builder.HasIndex(e => new { e.TrackingId, e.EventTimeUtc });

        builder.ToTable("ScanEvents");
    }
}
