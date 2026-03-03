using ParcelTracking.Domain.Models;

namespace ParcelTracking.Rules.Abstractions;

/// <summary>
/// Marker interface for all business rules.
/// </summary>
public interface IBusinessRule
{
    string RuleName { get; }
}

/// <summary>
/// A rule that evaluates a scan event in isolation (e.g., collection validation).
/// </summary>
public interface IScanEventRule : IBusinessRule
{
    Task<RuleResult> EvaluateAsync(ScanEvent scanEvent, CancellationToken ct = default);
}

/// <summary>
/// A rule that evaluates a scan event in the context of the existing parcel (e.g., transition validation).
/// </summary>
public interface IParcelRule : IBusinessRule
{
    Task<RuleResult> EvaluateAsync(Parcel parcel, ScanEvent scanEvent, CancellationToken ct = default);
}
