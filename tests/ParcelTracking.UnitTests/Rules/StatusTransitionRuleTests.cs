using ParcelTracking.Domain.Enums;
using ParcelTracking.Domain.Models;
using ParcelTracking.Rules.Rules;
using FluentAssertions;

namespace ParcelTracking.UnitTests.Rules;

public sealed class StatusTransitionRuleTests
{
    private readonly StatusTransitionRule _rule = new();

    private static Parcel ParcelWithStatus(ParcelStatus status) => new()
    {
        TrackingId = "PKG-12345678",
        Status = status,
        Dimensions = new Dimensions(30, 20, 15, 2m),
        FromAddress = new Address("1 St", "London", "E1", "GB"),
        ToAddress = new Address("2 Rd", "Manchester", "M1", "GB"),
        Sender = new Contact("Alice", "123", "a@b.com"),
        Receiver = new Contact("Bob", "456", "b@c.com"),
    };

    private static ScanEvent EventWithType(ParcelStatus type) => new()
    {
        EventId = Guid.NewGuid().ToString(),
        TrackingId = "PKG-12345678",
        EventType = type,
        EventTimeUtc = DateTime.UtcNow,
        LocationId = "HUB-1",
        ActorId = "SCANNER-1",
    };

    [Theory]
    [InlineData(ParcelStatus.COLLECTED,          ParcelStatus.SOURCE_SORT)]
    [InlineData(ParcelStatus.SOURCE_SORT,        ParcelStatus.DESTINATION_SORT)]
    [InlineData(ParcelStatus.DESTINATION_SORT,   ParcelStatus.DELIVERY_CENTRE)]
    [InlineData(ParcelStatus.DELIVERY_CENTRE,    ParcelStatus.READY_FOR_DELIVERY)]
    [InlineData(ParcelStatus.READY_FOR_DELIVERY, ParcelStatus.DELIVERED)]
    [InlineData(ParcelStatus.READY_FOR_DELIVERY, ParcelStatus.FAILED_TO_DELIVER)]
    [InlineData(ParcelStatus.FAILED_TO_DELIVER,  ParcelStatus.READY_FOR_DELIVERY)]
    [InlineData(ParcelStatus.FAILED_TO_DELIVER,  ParcelStatus.RETURNED)]
    public async Task ValidTransition_ReturnsSuccess(ParcelStatus from, ParcelStatus to)
    {
        var result = await _rule.EvaluateAsync(ParcelWithStatus(from), EventWithType(to));
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(ParcelStatus.COLLECTED,          ParcelStatus.DELIVERED)]
    [InlineData(ParcelStatus.DELIVERED,          ParcelStatus.COLLECTED)]
    [InlineData(ParcelStatus.RETURNED,           ParcelStatus.COLLECTED)]
    [InlineData(ParcelStatus.SOURCE_SORT,        ParcelStatus.READY_FOR_DELIVERY)]
    public async Task InvalidTransition_ReturnsFailure(ParcelStatus from, ParcelStatus to)
    {
        var result = await _rule.EvaluateAsync(ParcelWithStatus(from), EventWithType(to));
        result.IsSuccess.Should().BeFalse();
        result.FailureCode.Should().Be("INVALID_STATUS_TRANSITION");
    }
}
