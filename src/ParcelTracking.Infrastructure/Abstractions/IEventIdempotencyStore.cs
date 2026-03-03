namespace ParcelTracking.Infrastructure.Abstractions;

/// <summary>
/// Fast idempotency check store. Backed by Redis in production; can be in-memory for tests.
/// </summary>
public interface IEventIdempotencyStore
{
    /// <summary>
    /// Returns <c>true</c> if the eventId has already been processed (duplicate).
    /// If not seen, marks it as processed and returns <c>false</c>.
    /// This is an atomic check-and-set operation.
    /// </summary>
    Task<bool> IsDuplicateAsync(string eventId, CancellationToken ct = default);
}
