using ParcelTracking.Domain.Enums;
using ParcelTracking.Domain.StateMachine;
using FluentAssertions;

namespace ParcelTracking.UnitTests.StateMachine;

public sealed class ParcelStateMachineTests
{
    [Theory]
    [InlineData(null,                              ParcelStatus.COLLECTED)]
    [InlineData(ParcelStatus.COLLECTED,            ParcelStatus.SOURCE_SORT)]
    [InlineData(ParcelStatus.SOURCE_SORT,          ParcelStatus.DESTINATION_SORT)]
    [InlineData(ParcelStatus.DESTINATION_SORT,     ParcelStatus.DELIVERY_CENTRE)]
    [InlineData(ParcelStatus.DELIVERY_CENTRE,      ParcelStatus.READY_FOR_DELIVERY)]
    [InlineData(ParcelStatus.READY_FOR_DELIVERY,   ParcelStatus.DELIVERED)]
    [InlineData(ParcelStatus.READY_FOR_DELIVERY,   ParcelStatus.FAILED_TO_DELIVER)]
    [InlineData(ParcelStatus.FAILED_TO_DELIVER,    ParcelStatus.READY_FOR_DELIVERY)]
    [InlineData(ParcelStatus.FAILED_TO_DELIVER,    ParcelStatus.RETURNED)]
    public void IsValidTransition_ValidPaths_ReturnsTrue(ParcelStatus? from, ParcelStatus to)
    {
        ParcelStateMachine.IsValidTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(ParcelStatus.COLLECTED,            ParcelStatus.DESTINATION_SORT)]
    [InlineData(ParcelStatus.COLLECTED,            ParcelStatus.DELIVERED)]
    [InlineData(ParcelStatus.DELIVERED,            ParcelStatus.COLLECTED)]
    [InlineData(ParcelStatus.RETURNED,             ParcelStatus.COLLECTED)]
    [InlineData(ParcelStatus.SOURCE_SORT,          ParcelStatus.READY_FOR_DELIVERY)]
    [InlineData(ParcelStatus.READY_FOR_DELIVERY,   ParcelStatus.SOURCE_SORT)]
    public void IsValidTransition_InvalidPaths_ReturnsFalse(ParcelStatus from, ParcelStatus to)
    {
        ParcelStateMachine.IsValidTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void IsValidTransition_TerminalStatuses_ReturnFalse()
    {
        // DELIVERED and RETURNED are terminal — no outgoing transitions
        ParcelStateMachine.IsValidTransition(ParcelStatus.DELIVERED, ParcelStatus.RETURNED).Should().BeFalse();
        ParcelStateMachine.IsValidTransition(ParcelStatus.RETURNED, ParcelStatus.DELIVERED).Should().BeFalse();
    }

    [Fact]
    public void GetAllowedTransitions_FailedToDeliver_ReturnsTwoOptions()
    {
        var allowed = ParcelStateMachine.GetAllowedTransitions(ParcelStatus.FAILED_TO_DELIVER);
        allowed.Should().BeEquivalentTo(new[]
        {
            ParcelStatus.READY_FOR_DELIVERY,
            ParcelStatus.RETURNED
        });
    }
}
