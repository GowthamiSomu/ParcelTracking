using ParcelTracking.Domain.Enums;
using ParcelTracking.Domain.Models;
using ParcelTracking.Rules.Abstractions;

namespace ParcelTracking.Rules.Rules;

/// <summary>
/// Rule B: Classifies the parcel as STANDARD or LARGE based on dimensions,
/// and computes baseCharge, largeSurcharge, and totalCharge.
/// </summary>
public sealed class SizingRule : IScanEventRule
{
    private const decimal LargeDimensionThresholdCm = 50m;
    private const decimal LargeSurchargeRate = 0.20m;

    public string RuleName => "RuleB_Sizing";

    public Task<RuleResult> EvaluateAsync(ScanEvent scanEvent, CancellationToken ct = default)
    {
        // Sizing only applies to COLLECTED events
        if (scanEvent.Dimensions is null || scanEvent.BaseCharge is null)
            return Task.FromResult(RuleResult.Success()); // Let Rule A handle missing data

        return Task.FromResult(RuleResult.Success());
    }

    /// <summary>
    /// Applies sizing classification and pricing to the parcel entity in-place.
    /// Call this after the parcel is constructed from the COLLECTED event.
    /// </summary>
    public static void Apply(Parcel parcel)
    {
        var d = parcel.Dimensions;
        bool isLarge = d.LengthCm > LargeDimensionThresholdCm
                    || d.WidthCm > LargeDimensionThresholdCm
                    || d.HeightCm > LargeDimensionThresholdCm;

        parcel.SizeClass = isLarge ? SizeClass.LARGE : SizeClass.STANDARD;
        parcel.LargeSurcharge = isLarge
            ? Math.Round(parcel.BaseCharge * LargeSurchargeRate, 2, MidpointRounding.AwayFromZero)
            : 0m;
        parcel.TotalCharge = parcel.BaseCharge + parcel.LargeSurcharge;
    }
}
