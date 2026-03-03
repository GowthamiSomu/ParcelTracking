using ParcelTracking.Domain.Models;
using ParcelTracking.Notifications.Models;

namespace ParcelTracking.Notifications.Abstractions;

/// <summary>
/// Defines the notification contract. The business rules layer depends only on this interface —
/// never on a concrete implementation.
/// </summary>
public interface INotificationService
{
    /// <summary>Sends an email notification to the parcel sender. Always called on status change.</summary>
    Task SendSenderNotificationAsync(Parcel parcel, ScanEvent scanEvent, CancellationToken ct = default);

    /// <summary>
    /// Sends an SMS notification to the receiver.
    /// Only called when <see cref="Contact.NotificationOptIn"/> is <c>true</c>.
    /// </summary>
    Task SendReceiverNotificationAsync(Parcel parcel, ScanEvent scanEvent, CancellationToken ct = default);
}
