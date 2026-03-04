using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ParcelTracking.Domain.Models;
using ParcelTracking.Ingestion.Processing;

namespace ParcelTracking.Ingestion;

/// <summary>
/// Long-running Azure Service Bus consumer. Uses Service Bus sessions to guarantee
/// per-parcel ordering (session key = trackingId).
/// Implements backpressure via the concurrency limit on ServiceBusSessionProcessor.
/// </summary>
public sealed class ScanEventConsumer : BackgroundService
{
    // Case-insensitive + camelCase so the consumer accepts messages from any producer
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly ServiceBusSessionProcessor _processor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScanEventConsumer> _logger;

    public ScanEventConsumer(
        ServiceBusSessionProcessor processor,
        IServiceScopeFactory scopeFactory,
        ILogger<ScanEventConsumer> logger)
    {
        _processor = processor;
        _scopeFactory = scopeFactory;
        _logger = logger;

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[INGESTION] Starting Service Bus session processor...");
        // Use CancellationToken.None here: the SDK's token param only guards the *start* call,
        // not the processor's lifetime. If we passed stoppingToken and it was already signalled
        // (e.g. chained from a startup timeout), it would throw immediately before connecting.
        await _processor.StartProcessingAsync(CancellationToken.None);

        // Block until the host signals shutdown.
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { });

        _logger.LogInformation("[INGESTION] Stopping Service Bus session processor...");
        await _processor.StopProcessingAsync();
    }

    private async Task OnMessageAsync(ProcessSessionMessageEventArgs args)
    {
        var ct = args.CancellationToken;

        ScanEvent? scanEvent;
        try
        {
            scanEvent = JsonSerializer.Deserialize<ScanEvent>(args.Message.Body.ToString(), _jsonOptions);
            if (scanEvent is null)
                throw new InvalidOperationException("Deserialized ScanEvent was null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[INGESTION] Failed to deserialize message | MessageId={MessageId}", args.Message.MessageId);
            // Dead-letter the poison message immediately
            await args.DeadLetterMessageAsync(args.Message,
                deadLetterReason: "DESERIALIZATION_FAILURE",
                deadLetterErrorDescription: ex.Message,
                cancellationToken: ct);
            return;
        }

        // Create a DI scope per message (scoped services: DbContext, repositories)
        await using var scope = _scopeFactory.CreateAsyncScope();
        var eventProcessor = scope.ServiceProvider.GetRequiredService<ScanEventProcessor>();

        try
        {
            var result = await eventProcessor.ProcessAsync(scanEvent, ct);

            if (result.Success)
            {
                await args.CompleteMessageAsync(args.Message, ct);
            }
            else
            {
                // Route to DLQ with structured reason
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: result.ErrorCode,
                    deadLetterErrorDescription: result.ErrorMessage,
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[INGESTION] Unhandled error processing message | MessageId={MessageId} TrackingId={TrackingId}",
                args.Message.MessageId, scanEvent.TrackingId);

            // Abandon — Service Bus will retry with exponential backoff up to MaxDeliveryCount
            await args.AbandonMessageAsync(args.Message, cancellationToken: ct);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "[INGESTION] Service Bus processor error | Source={Source} EntityPath={Path}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _processor.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
