using ParcelTracking.Domain.Enums;

namespace ParcelTracking.Domain.Models;

public sealed class AnomalyEvent
{
    public string AnomalyId { get; set; } = Guid.NewGuid().ToString();
    public string TrackingId { get; set; } = string.Empty;
    public ParcelStatus? FromStatus { get; set; }
    public ParcelStatus AttemptedStatus { get; set; }
    public DateTime EventTimeUtc { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string OriginalEventId { get; set; } = string.Empty;
}
