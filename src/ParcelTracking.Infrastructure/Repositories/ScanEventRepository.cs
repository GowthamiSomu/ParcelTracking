using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParcelTracking.Domain.Models;
using ParcelTracking.Infrastructure.Abstractions;
using ParcelTracking.Infrastructure.Persistence;

namespace ParcelTracking.Infrastructure.Repositories;

public sealed class ScanEventRepository : IScanEventRepository
{
    private const int MaxLimit = 500;
    private readonly ParcelTrackingDbContext _db;

    public ScanEventRepository(ParcelTrackingDbContext db) => _db = db;

    public async Task AddAsync(ScanEvent scanEvent, CancellationToken ct = default)
    {
        var entity = new ScanEventEntity
        {
            EventId = scanEvent.EventId,
            TrackingId = scanEvent.TrackingId,
            EventType = scanEvent.EventType,
            EventTimeUtc = scanEvent.EventTimeUtc,
            LocationId = scanEvent.LocationId,
            HubType = scanEvent.HubType,
            ActorId = scanEvent.ActorId,
            MetadataJson = JsonSerializer.Serialize(scanEvent.Metadata),
        };

        _db.ScanEvents.Add(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ScanEvent>> GetByTrackingIdAsync(
        string trackingId, int limit = 100, CancellationToken ct = default)
    {
        var effectiveLimit = Math.Min(limit, MaxLimit);

        var entities = await _db.ScanEvents
            .AsNoTracking()
            .Where(e => e.TrackingId == trackingId)
            .OrderByDescending(e => e.EventTimeUtc)
            .Take(effectiveLimit)
            .ToListAsync(ct);

        return entities.Select(e => new ScanEvent
        {
            EventId = e.EventId,
            TrackingId = e.TrackingId,
            EventType = e.EventType,
            EventTimeUtc = e.EventTimeUtc,
            LocationId = e.LocationId,
            HubType = e.HubType,
            ActorId = e.ActorId,
            Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(e.MetadataJson)
                       ?? new Dictionary<string, object>(),
        }).ToList();
    }
}
