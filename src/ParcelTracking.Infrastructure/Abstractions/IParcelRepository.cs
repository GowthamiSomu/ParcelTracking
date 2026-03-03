using ParcelTracking.Domain.Models;

namespace ParcelTracking.Infrastructure.Abstractions;

public interface IParcelRepository
{
    Task<Parcel?> GetByTrackingIdAsync(string trackingId, CancellationToken ct = default);
    Task AddAsync(Parcel parcel, CancellationToken ct = default);
    Task UpdateAsync(Parcel parcel, CancellationToken ct = default);
    Task<bool> ExistsAsync(string trackingId, CancellationToken ct = default);
}
