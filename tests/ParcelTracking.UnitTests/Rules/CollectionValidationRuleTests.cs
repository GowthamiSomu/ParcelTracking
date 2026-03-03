using ParcelTracking.Domain.Enums;
using ParcelTracking.Domain.Models;
using ParcelTracking.Rules.Rules;
using FluentAssertions;

namespace ParcelTracking.UnitTests.Rules;

public sealed class CollectionValidationRuleTests
{
    private readonly CollectionValidationRule _rule = new();

    private static ScanEvent ValidCollectionEvent() => new()
    {
        EventId = "EVT-001",
        TrackingId = "PKG-12345678",
        EventType = ParcelStatus.COLLECTED,
        EventTimeUtc = DateTime.UtcNow,
        LocationId = "HUB-LONDON",
        ActorId = "SCANNER-01",
        Dimensions = new Dimensions(30m, 20m, 15m, 2.5m),
        FromAddress = new Address("1 Sender St", "London", "E1 1AA", "GB"),
        ToAddress = new Address("2 Receiver Rd", "Manchester", "M1 1AA", "GB"),
        Sender = new Contact("Alice", "+441234567890", "alice@example.com"),
        Receiver = new Contact("Bob", "+449876543210", "bob@example.com", true),
        BaseCharge = 10.00m,
    };

    [Fact]
    public async Task ValidEvent_ReturnsSuccess()
    {
        var result = await _rule.EvaluateAsync(ValidCollectionEvent());
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task MissingTrackingId_ReturnsFailure(string trackingId)
    {
        var evt = ValidCollectionEvent();
        evt.TrackingId = trackingId;
        var result = await _rule.EvaluateAsync(evt);
        result.IsSuccess.Should().BeFalse();
        result.FailureCode.Should().Be("INVALID_TRACKING_ID");
    }

    [Theory]
    [InlineData("abc")]           // Too short
    [InlineData("this has spaces")]
    [InlineData("has_underscore")]
    public async Task InvalidTrackingIdFormat_ReturnsFailure(string trackingId)
    {
        var evt = ValidCollectionEvent();
        evt.TrackingId = trackingId;
        var result = await _rule.EvaluateAsync(evt);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task MissingFromAddress_ReturnsFailure()
    {
        var evt = ValidCollectionEvent();
        evt.FromAddress = null;
        var result = await _rule.EvaluateAsync(evt);
        result.IsSuccess.Should().BeFalse();
        result.FailureCode.Should().Be("MISSING_FROM_ADDRESS");
    }

    [Fact]
    public async Task MissingToAddress_ReturnsFailure()
    {
        var evt = ValidCollectionEvent();
        evt.ToAddress = null;
        var result = await _rule.EvaluateAsync(evt);
        result.IsSuccess.Should().BeFalse();
        result.FailureCode.Should().Be("MISSING_TO_ADDRESS");
    }

    [Fact]
    public async Task MissingDimensions_ReturnsFailure()
    {
        var evt = ValidCollectionEvent();
        evt.Dimensions = null;
        var result = await _rule.EvaluateAsync(evt);
        result.IsSuccess.Should().BeFalse();
        result.FailureCode.Should().Be("MISSING_DIMENSIONS");
    }

    [Theory]
    [InlineData(0, 20, 15, 2.5)]    // Zero length
    [InlineData(30, -1, 15, 2.5)]   // Negative width
    [InlineData(30, 20, 301, 2.5)]  // Height exceeds max
    [InlineData(30, 20, 15, 71)]    // Weight exceeds max
    public async Task InvalidDimensions_ReturnsFailure(
        double l, double w, double h, double kg)
    {
        var evt = ValidCollectionEvent();
        evt.Dimensions = new Dimensions((decimal)l, (decimal)w, (decimal)h, (decimal)kg);
        var result = await _rule.EvaluateAsync(evt);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ZeroBaseCharge_ReturnsFailure()
    {
        var evt = ValidCollectionEvent();
        evt.BaseCharge = 0m;
        var result = await _rule.EvaluateAsync(evt);
        result.IsSuccess.Should().BeFalse();
        result.FailureCode.Should().Be("INVALID_BASE_CHARGE");
    }
}
