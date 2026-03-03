using ParcelTracking.Domain.Models;
using ParcelTracking.Rules.Abstractions;

namespace ParcelTracking.Rules.Rules;

/// <summary>
/// Rule A: Validates a COLLECTED scan event and its parcel creation payload.
/// </summary>
public sealed class CollectionValidationRule : IScanEventRule
{
    // Dimension limits in cm
    private const decimal MaxDimensionCm = 300m;
    private const decimal MaxWeightKg = 70m;
    private const decimal MinDimension = 0m;

    public string RuleName => "RuleA_CollectionValidation";

    public Task<RuleResult> EvaluateAsync(ScanEvent scanEvent, CancellationToken ct = default)
    {
        // Must have a tracking ID in a valid format (non-empty, alphanumeric with dashes)
        if (string.IsNullOrWhiteSpace(scanEvent.TrackingId))
            return Task.FromResult(RuleResult.Failure("INVALID_TRACKING_ID", "TrackingId is required."));

        if (!IsValidTrackingIdFormat(scanEvent.TrackingId))
            return Task.FromResult(RuleResult.Failure("INVALID_TRACKING_ID_FORMAT",
                $"TrackingId '{scanEvent.TrackingId}' does not match the required format (alphanumeric, dashes allowed, 8-30 chars)."));

        // Parcel creation data must be present on COLLECTED events
        if (scanEvent.FromAddress is null)
            return Task.FromResult(RuleResult.Failure("MISSING_FROM_ADDRESS", "FromAddress is required for collection events."));

        if (scanEvent.ToAddress is null)
            return Task.FromResult(RuleResult.Failure("MISSING_TO_ADDRESS", "ToAddress is required for collection events."));

        var fromResult = ValidateAddress(scanEvent.FromAddress, "FromAddress");
        if (!fromResult.IsSuccess) return Task.FromResult(fromResult);

        var toResult = ValidateAddress(scanEvent.ToAddress, "ToAddress");
        if (!toResult.IsSuccess) return Task.FromResult(toResult);

        if (scanEvent.Sender is null)
            return Task.FromResult(RuleResult.Failure("MISSING_SENDER", "Sender contact details are required."));

        if (scanEvent.Receiver is null)
            return Task.FromResult(RuleResult.Failure("MISSING_RECEIVER", "Receiver contact details are required."));

        if (scanEvent.Dimensions is null)
            return Task.FromResult(RuleResult.Failure("MISSING_DIMENSIONS", "Parcel dimensions are required."));

        var dimResult = ValidateDimensions(scanEvent.Dimensions);
        if (!dimResult.IsSuccess) return Task.FromResult(dimResult);

        if (scanEvent.BaseCharge is null || scanEvent.BaseCharge <= 0)
            return Task.FromResult(RuleResult.Failure("INVALID_BASE_CHARGE", "BaseCharge must be a positive value."));

        return Task.FromResult(RuleResult.Success());
    }

    private static bool IsValidTrackingIdFormat(string trackingId)
    {
        if (trackingId.Length < 8 || trackingId.Length > 30) return false;
        return trackingId.All(c => char.IsLetterOrDigit(c) || c == '-');
    }

    private static RuleResult ValidateAddress(Address address, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(address.Line1))
            return RuleResult.Failure($"MISSING_{fieldName.ToUpperInvariant()}_LINE1", $"{fieldName}.Line1 is required.");
        if (string.IsNullOrWhiteSpace(address.City))
            return RuleResult.Failure($"MISSING_{fieldName.ToUpperInvariant()}_CITY", $"{fieldName}.City is required.");
        if (string.IsNullOrWhiteSpace(address.Postcode))
            return RuleResult.Failure($"MISSING_{fieldName.ToUpperInvariant()}_POSTCODE", $"{fieldName}.Postcode is required.");
        if (string.IsNullOrWhiteSpace(address.Country))
            return RuleResult.Failure($"MISSING_{fieldName.ToUpperInvariant()}_COUNTRY", $"{fieldName}.Country is required.");
        return RuleResult.Success();
    }

    private static RuleResult ValidateDimensions(Dimensions d)
    {
        if (d.LengthCm <= MinDimension)
            return RuleResult.Failure("INVALID_DIMENSION", "LengthCm must be positive.");
        if (d.WidthCm <= MinDimension)
            return RuleResult.Failure("INVALID_DIMENSION", "WidthCm must be positive.");
        if (d.HeightCm <= MinDimension)
            return RuleResult.Failure("INVALID_DIMENSION", "HeightCm must be positive.");
        if (d.WeightKg <= MinDimension)
            return RuleResult.Failure("INVALID_DIMENSION", "WeightKg must be positive.");

        if (d.LengthCm > MaxDimensionCm)
            return RuleResult.Failure("DIMENSION_EXCEEDED", $"LengthCm {d.LengthCm} exceeds maximum {MaxDimensionCm} cm.");
        if (d.WidthCm > MaxDimensionCm)
            return RuleResult.Failure("DIMENSION_EXCEEDED", $"WidthCm {d.WidthCm} exceeds maximum {MaxDimensionCm} cm.");
        if (d.HeightCm > MaxDimensionCm)
            return RuleResult.Failure("DIMENSION_EXCEEDED", $"HeightCm {d.HeightCm} exceeds maximum {MaxDimensionCm} cm.");
        if (d.WeightKg > MaxWeightKg)
            return RuleResult.Failure("WEIGHT_EXCEEDED", $"WeightKg {d.WeightKg} exceeds maximum {MaxWeightKg} kg.");

        return RuleResult.Success();
    }
}
