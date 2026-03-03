using System.Text.Json;
using Microsoft.Extensions.Logging;
using ParcelTracking.Domain.Models;
using ParcelTracking.Domain.StateMachine;
using ParcelTracking.Domain.Enums;
using ParcelTracking.Infrastructure.Abstractions;
using ParcelTracking.Notifications.Abstractions;
using ParcelTracking.Rules.Rules;

namespace ParcelTracking.Ingestion.Processing;

/// <summary>
/// Core event processor: idempotency check → rule evaluation → persistence → notifications.
/// Injected into the Service Bus consumer. Fully async and non-blocking.
/// </summary>
public sealed class ScanEventProcessor
{
    private readonly IParcelRepository _parcels;
    private readonly IScanEventRepository _events;
    private readonly IEventIdempotencyStore _idempotency;
    private readonly IAnomalyEventPublisher _anomalyPublisher;
    private readonly INotificationService _notifications;
    private readonly CollectionValidationRule _collectionRule;
    private readonly StatusTransitionRule _transitionRule;
    private readonly ILogger<ScanEventProcessor> _logger;

    public ScanEventProcessor(
        IParcelRepository parcels,
        IScanEventRepository events,
        IEventIdempotencyStore idempotency,
        IAnomalyEventPublisher anomalyPublisher,
        INotificationService notifications,
        CollectionValidationRule collectionRule,
        StatusTransitionRule transitionRule,
        ILogger<ScanEventProcessor> logger)
    {
        _parcels = parcels;
        _events = events;
        _idempotency = idempotency;
        _anomalyPublisher = anomalyPublisher;
        _notifications = notifications;
        _collectionRule = collectionRule;
        _transitionRule = transitionRule;
        _logger = logger;
    }

    public async Task<ProcessResult> ProcessAsync(ScanEvent scanEvent, CancellationToken ct = default)
    {
        // ── 1. Idempotency check ──────────────────────────────────────────────
        if (await _idempotency.IsDuplicateAsync(scanEvent.EventId, ct))
        {
            _logger.LogInformation(
                "[IDEMPOTENCY] Duplicate event skipped | EventId={EventId} TrackingId={TrackingId}",
                scanEvent.EventId, scanEvent.TrackingId);
            return ProcessResult.Duplicate();
        }

        // ── 2. COLLECTED event: Rule A + B (create new parcel) ───────────────
        if (scanEvent.EventType == ParcelStatus.COLLECTED)
        {
            return await HandleCollectionEventAsync(scanEvent, ct);
        }

        // ── 3. Subsequent events: Rule C (status transition) ─────────────────
        return await HandleStatusUpdateEventAsync(scanEvent, ct);
    }

    // -------------------------------------------------------------------------

    private async Task<ProcessResult> HandleCollectionEventAsync(ScanEvent scanEvent, CancellationToken ct)
    {
        // Rule A: collection validation
        var ruleAResult = await _collectionRule.EvaluateAsync(scanEvent, ct);
        if (!ruleAResult.IsSuccess)
        {
            _logger.LogWarning(
                "[RULE_A_FAIL] Collection validation failed | EventId={EventId} TrackingId={TrackingId} Code={Code} Message={Message}",
                scanEvent.EventId, scanEvent.TrackingId, ruleAResult.FailureCode, ruleAResult.FailureMessage);
            return ProcessResult.RejectedToDlq(ruleAResult.FailureCode!, ruleAResult.FailureMessage!);
        }

        // Check uniqueness (parcel must not already exist)
        if (await _parcels.ExistsAsync(scanEvent.TrackingId, ct))
        {
            _logger.LogWarning(
                "[DUPLICATE_PARCEL] TrackingId already exists | TrackingId={TrackingId}",
                scanEvent.TrackingId);
            return ProcessResult.RejectedToDlq("DUPLICATE_TRACKING_ID",
                $"A parcel with trackingId '{scanEvent.TrackingId}' already exists.");
        }

        // Validate initial transition: null → COLLECTED
        if (!ParcelStateMachine.IsValidTransition(null, ParcelStatus.COLLECTED))
        {
            // Defensive — should never happen
            return ProcessResult.RejectedToDlq("INVALID_INITIAL_STATUS", "COLLECTED is not a valid initial status.");
        }

        // Build parcel entity
        var now = DateTime.UtcNow;
        var parcel = new Parcel
        {
            TrackingId = scanEvent.TrackingId,
            Status = ParcelStatus.COLLECTED,
            Dimensions = scanEvent.Dimensions!,
            FromAddress = scanEvent.FromAddress!,
            ToAddress = scanEvent.ToAddress!,
            Sender = scanEvent.Sender!,
            Receiver = scanEvent.Receiver!,
            BaseCharge = scanEvent.BaseCharge!.Value,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Rule B: sizing + pricing
        SizingRule.Apply(parcel);

        // Persist
        await _parcels.AddAsync(parcel, ct);
        await _events.AddAsync(scanEvent, ct);

        _logger.LogInformation(
            "[COLLECTED] Parcel created | TrackingId={TrackingId} SizeClass={SizeClass} TotalCharge={TotalCharge}",
            parcel.TrackingId, parcel.SizeClass, parcel.TotalCharge);

        // Notifications
        await _notifications.SendSenderNotificationAsync(parcel, scanEvent, ct);
        if (parcel.Receiver.NotificationOptIn)
            await _notifications.SendReceiverNotificationAsync(parcel, scanEvent, ct);

        return ProcessResult.Ok();
    }

    private async Task<ProcessResult> HandleStatusUpdateEventAsync(ScanEvent scanEvent, CancellationToken ct)
    {
        var parcel = await _parcels.GetByTrackingIdAsync(scanEvent.TrackingId, ct);
        if (parcel is null)
        {
            _logger.LogWarning(
                "[NOT_FOUND] Parcel not found for scan event | TrackingId={TrackingId} EventId={EventId}",
                scanEvent.TrackingId, scanEvent.EventId);
            return ProcessResult.RejectedToDlq("PARCEL_NOT_FOUND",
                $"No parcel found with trackingId '{scanEvent.TrackingId}'.");
        }

        // Rule C: status transition
        var ruleCResult = await _transitionRule.EvaluateAsync(parcel, scanEvent, ct);
        if (!ruleCResult.IsSuccess)
        {
            var anomaly = new AnomalyEvent
            {
                TrackingId = scanEvent.TrackingId,
                FromStatus = parcel.Status,
                AttemptedStatus = scanEvent.EventType,
                EventTimeUtc = scanEvent.EventTimeUtc,
                ActorId = scanEvent.ActorId,
                Reason = ruleCResult.FailureMessage!,
                OriginalEventId = scanEvent.EventId,
            };

            await _anomalyPublisher.PublishAsync(anomaly, ct);

            _logger.LogWarning(
                "[RULE_C_FAIL] Invalid transition | TrackingId={TrackingId} From={From} To={To} AnomalyId={AnomalyId}",
                scanEvent.TrackingId, parcel.Status, scanEvent.EventType, anomaly.AnomalyId);

            return ProcessResult.RejectedToDlq(ruleCResult.FailureCode!, ruleCResult.FailureMessage!);
        }

        // Apply transition
        parcel.Status = scanEvent.EventType;
        parcel.UpdatedAt = DateTime.UtcNow;

        await _parcels.UpdateAsync(parcel, ct);
        await _events.AddAsync(scanEvent, ct);

        _logger.LogInformation(
            "[STATUS_UPDATE] Parcel status updated | TrackingId={TrackingId} NewStatus={Status}",
            parcel.TrackingId, parcel.Status);

        // Notifications
        await _notifications.SendSenderNotificationAsync(parcel, scanEvent, ct);
        if (parcel.Receiver.NotificationOptIn)
            await _notifications.SendReceiverNotificationAsync(parcel, scanEvent, ct);

        return ProcessResult.Ok();
    }
}

public sealed record ProcessResult(bool Success, bool IsDuplicate, string? ErrorCode, string? ErrorMessage)
{
    public static ProcessResult Ok() => new(true, false, null, null);
    public static ProcessResult Duplicate() => new(true, true, null, null);
    public static ProcessResult RejectedToDlq(string code, string message) => new(false, false, code, message);
}
