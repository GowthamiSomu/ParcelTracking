namespace ParcelTracking.Domain.Models;

public sealed record Dimensions(
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    decimal WeightKg
);
