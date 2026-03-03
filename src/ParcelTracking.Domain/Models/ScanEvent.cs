using ParcelTracking.Domain.Enums;

namespace ParcelTracking.Domain.Models;

public sealed class ScanEvent
{
    public string EventId { get; set; } = string.Empty;
    public string TrackingId { get; set; } = string.Empty;
    public ParcelStatus EventType { get; set; }
    public DateTime EventTimeUtc { get; set; }
    public string LocationId { get; set; } = string.Empty;
    public string? HubType { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();

    // Parcel creation data — only present on COLLECTED events
    public Dimensions? Dimensions { get; set; }
    public Address? FromAddress { get; set; }
    public Address? ToAddress { get; set; }
    public Contact? Sender { get; set; }
    public Contact? Receiver { get; set; }
    public decimal? BaseCharge { get; set; }
}
