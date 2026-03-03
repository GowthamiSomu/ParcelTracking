using ParcelTracking.Domain.Enums;

namespace ParcelTracking.Domain.Models;

public sealed class Parcel
{
    public string TrackingId { get; set; } = string.Empty;
    public ParcelStatus Status { get; set; }
    public SizeClass SizeClass { get; set; }
    public Dimensions Dimensions { get; set; } = null!;
    public Address FromAddress { get; set; } = null!;
    public Address ToAddress { get; set; } = null!;
    public Contact Sender { get; set; } = null!;
    public Contact Receiver { get; set; } = null!;
    public decimal BaseCharge { get; set; }
    public decimal LargeSurcharge { get; set; }
    public decimal TotalCharge { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
