using ParcelTracking.Domain.Enums;
using ParcelTracking.Domain.Models;
using ParcelTracking.Notifications.Abstractions;
using ParcelTracking.Notifications.Models;
using Microsoft.Extensions.Logging;

namespace ParcelTracking.Notifications.Services;

/// <summary>
/// MVP stub implementation. Writes structured log entries and (optionally) enqueues to a queue.
/// Swap this for a real email/SMS provider (SendGrid, Twilio, Azure Communication Services) without
/// touching any business logic.
/// </summary>
public sealed class StubNotificationService : INotificationService
{
    private readonly ILogger<StubNotificationService> _logger;

    public StubNotificationService(ILogger<StubNotificationService> logger)
    {
        _logger = logger;
    }

    public Task SendSenderNotificationAsync(Parcel parcel, ScanEvent scanEvent, CancellationToken ct = default)
    {
        var payload = BuildPayload(parcel, scanEvent,
            parcel.Sender.Name, parcel.Sender.Email);

        _logger.LogInformation(
            "[NOTIFY:EMAIL] Sender notification | TrackingId={TrackingId} Status={Status} Location={Location} NextStep={NextStep} Recipient={Recipient}",
            payload.TrackingId, payload.NewStatus, payload.LocationName, payload.NextExpectedStep, payload.RecipientContact);

        return Task.CompletedTask;
    }

    public Task SendReceiverNotificationAsync(Parcel parcel, ScanEvent scanEvent, CancellationToken ct = default)
    {
        if (!parcel.Receiver.NotificationOptIn)
            return Task.CompletedTask; // Guard — callers should check, but we double-check here

        var payload = BuildPayload(parcel, scanEvent,
            parcel.Receiver.Name, parcel.Receiver.ContactNumber);

        _logger.LogInformation(
            "[NOTIFY:SMS] Receiver notification | TrackingId={TrackingId} Status={Status} Location={Location} NextStep={NextStep} Recipient={Recipient}",
            payload.TrackingId, payload.NewStatus, payload.LocationName, payload.NextExpectedStep, payload.RecipientContact);

        return Task.CompletedTask;
    }

    private static NotificationPayload BuildPayload(
        Parcel parcel, ScanEvent scanEvent, string recipientName, string recipientContact)
    {
        return new NotificationPayload(
            TrackingId: parcel.TrackingId,
            NewStatus: scanEvent.EventType.ToString(),
            TimestampUtc: scanEvent.EventTimeUtc,
            LocationName: scanEvent.LocationId,
            NextExpectedStep: ResolveNextStep(scanEvent.EventType),
            RecipientName: recipientName,
            RecipientContact: recipientContact
        );
    }

    private static string ResolveNextStep(ParcelStatus status) => status switch
    {
        ParcelStatus.COLLECTED        => "Your parcel has been collected and is heading to the sorting hub.",
        ParcelStatus.SOURCE_SORT      => "Your parcel has been sorted and is in transit to the destination hub.",
        ParcelStatus.DESTINATION_SORT => "Your parcel has arrived at the destination sorting hub.",
        ParcelStatus.DELIVERY_CENTRE  => "Your parcel is at the local delivery centre.",
        ParcelStatus.READY_FOR_DELIVERY => "Your parcel is out for delivery today.",
        ParcelStatus.DELIVERED        => "Your parcel has been delivered. Thank you!",
        ParcelStatus.FAILED_TO_DELIVER => "Delivery was unsuccessful. We will attempt re-delivery or you can arrange collection.",
        ParcelStatus.RETURNED         => "Your parcel is being returned to the sender.",
        _                             => "Status updated."
    };
}
