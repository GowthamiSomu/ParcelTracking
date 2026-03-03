namespace ParcelTracking.Rules.Abstractions;

public sealed record RuleResult(bool IsSuccess, string? FailureCode = null, string? FailureMessage = null)
{
    public static RuleResult Success() => new(true);
    public static RuleResult Failure(string code, string message) => new(false, code, message);
}
