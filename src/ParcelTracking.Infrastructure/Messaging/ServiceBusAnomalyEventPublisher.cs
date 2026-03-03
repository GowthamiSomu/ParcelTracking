using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using ParcelTracking.Domain.Models;
using ParcelTracking.Infrastructure.Abstractions;

namespace ParcelTracking.Infrastructure.Messaging;

/// <summary>
/// Publishes anomaly events to a dedicated Azure Service Bus topic/queue.
/// </summary>
public sealed class ServiceBusAnomalyEventPublisher : IAnomalyEventPublisher
{
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusAnomalyEventPublisher> _logger;

    public ServiceBusAnomalyEventPublisher(ServiceBusSender sender, ILogger<ServiceBusAnomalyEventPublisher> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task PublishAsync(AnomalyEvent anomaly, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(anomaly);
        var message = new ServiceBusMessage(json)
        {
            MessageId = anomaly.AnomalyId,
            ContentType = "application/json",
            Subject = "AnomalyEvent",
        };
        message.ApplicationProperties["TrackingId"] = anomaly.TrackingId;

        await _sender.SendMessageAsync(message, ct);

        _logger.LogWarning(
            "[ANOMALY] Published anomaly event | AnomalyId={AnomalyId} TrackingId={TrackingId} Reason={Reason}",
            anomaly.AnomalyId, anomaly.TrackingId, anomaly.Reason);
    }
}
