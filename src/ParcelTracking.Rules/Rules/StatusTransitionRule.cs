using ParcelTracking.Domain.Models;
using ParcelTracking.Domain.StateMachine;
using ParcelTracking.Rules.Abstractions;

namespace ParcelTracking.Rules.Rules;

/// <summary>
/// Rule C: Validates that the incoming scan event represents a permitted status transition.
/// </summary>
public sealed class StatusTransitionRule : IParcelRule
{
    public string RuleName => "RuleC_StatusTransition";

    public Task<RuleResult> EvaluateAsync(Parcel parcel, ScanEvent scanEvent, CancellationToken ct = default)
    {
        bool isValid = ParcelStateMachine.IsValidTransition(parcel.Status, scanEvent.EventType);

        if (!isValid)
        {
            var allowed = ParcelStateMachine.GetAllowedTransitions(parcel.Status);
            var allowedList = string.Join(", ", allowed);
            return Task.FromResult(RuleResult.Failure(
                "INVALID_STATUS_TRANSITION",
                $"Cannot transition parcel '{parcel.TrackingId}' from '{parcel.Status}' to '{scanEvent.EventType}'. " +
                $"Allowed transitions: [{allowedList}]."));
        }

        return Task.FromResult(RuleResult.Success());
    }
}
