using ParcelTracking.Infrastructure.Abstractions;
using StackExchange.Redis;

namespace ParcelTracking.Infrastructure.Idempotency;

/// <summary>
/// Redis-backed idempotency store. Uses SET NX (set if not exists) for atomic check-and-set.
/// Keys expire after 7 days — long enough to catch retries, prevents unbounded growth.
/// </summary>
public sealed class RedisEventIdempotencyStore : IEventIdempotencyStore
{
    private static readonly TimeSpan KeyTtl = TimeSpan.FromDays(7);
    private const string KeyPrefix = "parcel:eventid:";

    private readonly IDatabase _redis;

    public RedisEventIdempotencyStore(IConnectionMultiplexer redis)
        => _redis = redis.GetDatabase();

    public async Task<bool> IsDuplicateAsync(string eventId, CancellationToken ct = default)
    {
        var key = $"{KeyPrefix}{eventId}";
        // SET key 1 NX EX ttl — returns true if key was SET (first time), false if already existed
        bool wasSet = await _redis.StringSetAsync(key, "1", KeyTtl, When.NotExists);
        return !wasSet; // duplicate if key already existed (wasSet == false)
    }
}
