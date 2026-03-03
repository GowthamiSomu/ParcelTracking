using ParcelTracking.Domain.Models;

namespace ParcelTracking.Infrastructure.Abstractions;

public interface IScanEventRepository
{
    Task AddAsync(ScanEvent scanEvent, CancellationToken ct = default);
    Task<IReadOnlyList<ScanEvent>> GetByTrackingIdAsync(string trackingId, int limit = 100, CancellationToken ct = default);
}
