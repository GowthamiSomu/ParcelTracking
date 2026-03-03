namespace ParcelTracking.Notifications.Models;

public sealed record NotificationPayload(
    string TrackingId,
    string NewStatus,
    DateTime TimestampUtc,
    string LocationName,
    string NextExpectedStep,
    string RecipientName,
    string RecipientContact
);
