using ParcelTracking.Domain.Enums;
using ParcelTracking.Domain.Models;
using ParcelTracking.Rules.Rules;
using FluentAssertions;

namespace ParcelTracking.UnitTests.Rules;

public sealed class SizingRuleTests
{
    private static Parcel ParcelWithDimensions(decimal l, decimal w, decimal h, decimal baseCharge = 10m)
        => new()
        {
            TrackingId = "PKG-12345678",
            Dimensions = new Dimensions(l, w, h, 2.0m),
            BaseCharge = baseCharge,
            FromAddress = new Address("1 St", "London", "E1", "GB"),
            ToAddress = new Address("2 Rd", "Manchester", "M1", "GB"),
            Sender = new Contact("Alice", "123", "a@b.com"),
            Receiver = new Contact("Bob", "456", "b@c.com"),
        };

    [Fact]
    public void Apply_AllDimensionsUnder50_ClassifiesAsStandard()
    {
        var parcel = ParcelWithDimensions(30, 40, 50, 10m);
        SizingRule.Apply(parcel);
        parcel.SizeClass.Should().Be(SizeClass.STANDARD);
        parcel.LargeSurcharge.Should().Be(0m);
        parcel.TotalCharge.Should().Be(10m);
    }

    [Theory]
    [InlineData(51, 20, 20)]  // Length > 50
    [InlineData(20, 51, 20)]  // Width > 50
    [InlineData(20, 20, 51)]  // Height > 50
    public void Apply_AnyDimensionOver50_ClassifiesAsLarge(decimal l, decimal w, decimal h)
    {
        var parcel = ParcelWithDimensions(l, w, h, 100m);
        SizingRule.Apply(parcel);
        parcel.SizeClass.Should().Be(SizeClass.LARGE);
        parcel.LargeSurcharge.Should().Be(20m);       // 100 * 0.20
        parcel.TotalCharge.Should().Be(120m);          // 100 + 20
    }

    [Fact]
    public void Apply_LargeSurcharge_RoundedToTwoDecimalPlaces()
    {
        var parcel = ParcelWithDimensions(60, 20, 20, 9.99m);
        SizingRule.Apply(parcel);
        parcel.LargeSurcharge.Should().Be(2.00m);     // 9.99 * 0.20 = 1.998 → 2.00 (AwayFromZero)
        parcel.TotalCharge.Should().Be(11.99m);
    }

    [Fact]
    public void Apply_ExactlyAt50_ClassifiesAsStandard()
    {
        // Boundary: exactly 50 is NOT > 50, so should be STANDARD
        var parcel = ParcelWithDimensions(50, 50, 50, 10m);
        SizingRule.Apply(parcel);
        parcel.SizeClass.Should().Be(SizeClass.STANDARD);
    }
}
