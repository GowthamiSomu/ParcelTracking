using ParcelTracking.Domain.Models;

namespace ParcelTracking.Infrastructure.Abstractions;

/// <summary>
/// Publishes anomaly events to a dedicated anomaly topic/queue for downstream alerting or replay.
/// </summary>
public interface IAnomalyEventPublisher
{
    Task PublishAsync(AnomalyEvent anomaly, CancellationToken ct = default);
}
