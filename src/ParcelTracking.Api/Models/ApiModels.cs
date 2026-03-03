using ParcelTracking.Domain.Enums;

namespace ParcelTracking.Api.Models;

// ── Response DTOs ──────────────────────────────────────────────────────────────

public sealed record AddressDto(string Line1, string City, string Postcode, string Country);

public sealed record ChargesDto(decimal Base, decimal Surcharge, decimal Total);

public sealed record ContactDto(string Name, string ContactNumber, string Email);

public sealed record ReceiverDto(string Name, bool NotificationOptIn);

public sealed record ParcelResponse(
    string TrackingId,
    string CurrentStatus,
    string SizeClass,
    ChargesDto Charges,
    AddressDto From,
    AddressDto To,
    ContactDto Sender,
    ReceiverDto Receiver
);

public sealed record ScanEventDto(
    string EventId,
    string EventType,
    string EventTimeUtc,
    string LocationId,
    string? HubType,
    string ActorId,
    Dictionary<string, object> Metadata
);

public sealed record ParcelEventsResponse(
    string TrackingId,
    IReadOnlyList<ScanEventDto> Events
);

// ── Error response ─────────────────────────────────────────────────────────────

public sealed record ErrorDetail(string Code, string Message, object? Details = null);

public sealed record ErrorResponse(ErrorDetail Error);
