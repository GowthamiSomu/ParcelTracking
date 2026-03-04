using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ParcelTracking.Domain.Enums;
using ParcelTracking.Infrastructure.Persistence;
using ParcelTracking.IntegrationTests.Infrastructure;

namespace ParcelTracking.IntegrationTests.Api;

/// <summary>
/// Integration tests for the Parcel Tracking REST API.
///
/// Coverage:
///   GET /api/parcels/{id}               — 200, 404, 400 (invalid format)
///   GET /api/parcels/{id}/events        — 200 ordered, 404, 400 (invalid limit)
///   GET /healthz/live                   — liveness probe
///   PII masking                         — receiver.contactNumber/email absent
/// </summary>
public sealed class ParcelApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ParcelApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Seed helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task SeedParcelAsync(string trackingId,
        ParcelStatus status = ParcelStatus.COLLECTED)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelTrackingDbContext>();

        if (await db.Parcels.FindAsync(trackingId) is not null) return; // already seeded

        db.Parcels.Add(new ParcelEntity
        {
            TrackingId    = trackingId,
            Status        = status,
            SizeClass     = SizeClass.STANDARD,
            LengthCm = 30m, WidthCm = 20m, HeightCm = 15m, WeightKg = 2.5m,
            FromLine1     = "1 Sender St",  FromCity = "London",     FromPostcode = "E1 1AA",  FromCountry = "GB",
            ToLine1       = "2 Receiver Rd", ToCity = "Manchester", ToPostcode = "M1 1AA",   ToCountry = "GB",
            SenderName    = "Alice Smith",  SenderContactNumber = "07700000001", SenderEmail = "alice@test.com",
            ReceiverName  = "Bob Jones",    ReceiverContactNumber = "07700000002", ReceiverEmail = "bob@test.com",
            ReceiverNotificationOptIn = true,
            BaseCharge = 8.50m, LargeSurcharge = 0m, TotalCharge = 8.50m,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedScanEventAsync(string trackingId,
        ParcelStatus eventType, DateTime eventTimeUtc)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelTrackingDbContext>();

        db.ScanEvents.Add(new ScanEventEntity
        {
            EventId      = Guid.NewGuid().ToString(),
            TrackingId   = trackingId,
            EventType    = eventType,
            EventTimeUtc = eventTimeUtc,
            LocationId   = "HUB-LONDON",
            ActorId      = "SCANNER-01",
            MetadataJson = "{}",
        });
        await db.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GET /api/parcels/{trackingId}
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetParcel_ExistingParcel_Returns200WithCorrectBody()
    {
        const string id = "PKG-APITST001";
        await SeedParcelAsync(id);

        var response = await _client.GetAsync($"/api/parcels/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ParcelResponseDto>(JsonOpts);
        body.Should().NotBeNull();
        body!.TrackingId.Should().Be(id);
        body.CurrentStatus.Should().Be("COLLECTED");
        body.SizeClass.Should().Be("STANDARD");
        body.Charges.Total.Should().Be(8.50m);
        body.Charges.Surcharge.Should().Be(0m);
    }

    [Fact]
    public async Task GetParcel_ReceiverPii_IsStrippedFromResponse()
    {
        const string id = "PKG-APITST002";
        await SeedParcelAsync(id);

        var response = await _client.GetAsync($"/api/parcels/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Parse raw JSON to verify receiver fields
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var receiver = doc.RootElement.GetProperty("receiver");

        receiver.TryGetProperty("contactNumber", out _)
            .Should().BeFalse("receiver.contactNumber must be masked (OWASP PII)");
        receiver.TryGetProperty("email", out _)
            .Should().BeFalse("receiver.email must be masked (OWASP PII)");
        receiver.GetProperty("notificationOptIn").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetParcel_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/api/parcels/PKG-UNKNOWN001");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(JsonOpts);
        body!.Error.Code.Should().Be("PARCEL_NOT_FOUND");
    }

    [Theory]
    [InlineData("ab")]           // too short — only 2 chars
    [InlineData("PKG.DOTCHAR1")] // period is not in [A-Za-z0-9\-]
    public async Task GetParcel_InvalidTrackingIdFormat_Returns400(string trackingId)
    {
        var response = await _client.GetAsync($"/api/parcels/{trackingId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(JsonOpts);
        body!.Error.Code.Should().Be("INVALID_TRACKING_ID");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GET /api/parcels/{trackingId}/events
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEvents_ExistingParcel_ReturnsEventsLatestFirst()
    {
        const string id = "PKG-EVTTST001";
        await SeedParcelAsync(id);

        var t1 = DateTime.UtcNow.AddMinutes(-20);
        var t2 = DateTime.UtcNow.AddMinutes(-10);
        var t3 = DateTime.UtcNow;

        await SeedScanEventAsync(id, ParcelStatus.COLLECTED,        t1);
        await SeedScanEventAsync(id, ParcelStatus.SOURCE_SORT,      t2);
        await SeedScanEventAsync(id, ParcelStatus.DESTINATION_SORT, t3);

        var response = await _client.GetAsync($"/api/parcels/{id}/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ParcelEventsResponseDto>(JsonOpts);
        body!.TrackingId.Should().Be(id);
        body.Events.Should().HaveCount(3);

        // Reverse-chronological order (latest first)
        body.Events[0].EventType.Should().Be("DESTINATION_SORT");
        body.Events[1].EventType.Should().Be("SOURCE_SORT");
        body.Events[2].EventType.Should().Be("COLLECTED");
    }

    [Fact]
    public async Task GetEvents_DefaultLimit_Returns100Max()
    {
        const string id = "PKG-LIMTST001";
        await SeedParcelAsync(id);

        // Seed 150 events
        for (var i = 0; i < 150; i++)
            await SeedScanEventAsync(id, ParcelStatus.COLLECTED,
                DateTime.UtcNow.AddSeconds(-i));

        var response = await _client.GetAsync($"/api/parcels/{id}/events");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ParcelEventsResponseDto>(JsonOpts);
        body!.Events.Should().HaveCount(100, "default limit is 100");
    }

    [Fact]
    public async Task GetEvents_UnknownParcel_Returns404()
    {
        var response = await _client.GetAsync("/api/parcels/PKG-NOEVT0001/events");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(JsonOpts);
        body!.Error.Code.Should().Be("PARCEL_NOT_FOUND");
    }

    [Theory]
    [InlineData(0)]    // below minimum
    [InlineData(-1)]   // negative
    [InlineData(501)]  // above maximum
    public async Task GetEvents_InvalidLimit_Returns400(int limit)
    {
        // Seed a parcel so we don't hit 404 first
        const string id = "PKG-INVLIM001";
        await SeedParcelAsync(id);

        var response = await _client.GetAsync($"/api/parcels/{id}/events?limit={limit}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponseDto>(JsonOpts);
        body!.Error.Code.Should().Be("INVALID_LIMIT");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Health check
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HealthLive_Returns200()
    {
        var response = await _client.GetAsync("/healthz/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Private test DTOs (match camelCase API response shape)
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record ChargesDto(decimal Base, decimal Surcharge, decimal Total);
    private sealed record ReceiverDto(string Name, bool NotificationOptIn);
    private sealed record ParcelResponseDto(
        string TrackingId, string CurrentStatus, string SizeClass,
        ChargesDto Charges, ReceiverDto Receiver);

    private sealed record EventItemDto(string EventId, string EventType, string EventTimeUtc);
    private sealed record ParcelEventsResponseDto(string TrackingId, List<EventItemDto> Events);

    private sealed record ErrorDetailDto(string Code, string Message);
    private sealed record ErrorResponseDto(ErrorDetailDto Error);
}
