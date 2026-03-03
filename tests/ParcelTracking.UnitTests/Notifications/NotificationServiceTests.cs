using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ParcelTracking.Domain.Enums;
using ParcelTracking.Domain.Models;
using ParcelTracking.Notifications.Services;
using FluentAssertions;

namespace ParcelTracking.UnitTests.Notifications;

public sealed class StubNotificationServiceTests
{
    private readonly StubNotificationService _service =
        new(NullLogger<StubNotificationService>.Instance);

    private static Parcel BuildParcel(bool receiverOptIn) => new()
    {
        TrackingId = "PKG-12345678",
        Status = ParcelStatus.COLLECTED,
        SizeClass = SizeClass.STANDARD,
        Dimensions = new(30, 20, 15, 2m),
        FromAddress = new("1 St", "London", "E1", "GB"),
        ToAddress = new("2 Rd", "Manchester", "M1", "GB"),
        Sender = new("Alice", "+441234", "alice@example.com"),
        Receiver = new("Bob", "+449876", "bob@example.com", receiverOptIn),
        BaseCharge = 10m, LargeSurcharge = 0m, TotalCharge = 10m,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static ScanEvent BuildEvent(ParcelStatus type) => new()
    {
        EventId = "EVT-001",
        TrackingId = "PKG-12345678",
        EventType = type,
        EventTimeUtc = DateTime.UtcNow,
        LocationId = "HUB-LONDON",
        ActorId = "SCANNER-01",
    };

    [Fact]
    public async Task SendSenderNotification_CompletesWithoutException()
    {
        var parcel = BuildParcel(false);
        var scanEvent = BuildEvent(ParcelStatus.COLLECTED);
        var act = () => _service.SendSenderNotificationAsync(parcel, scanEvent);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendReceiverNotification_OptedIn_CompletesWithoutException()
    {
        var parcel = BuildParcel(true);
        var scanEvent = BuildEvent(ParcelStatus.READY_FOR_DELIVERY);
        var act = () => _service.SendReceiverNotificationAsync(parcel, scanEvent);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendReceiverNotification_NotOptedIn_SkipsSilently()
    {
        var parcel = BuildParcel(false);
        var scanEvent = BuildEvent(ParcelStatus.DELIVERED);
        // Should complete without throwing even when not opted-in
        var act = () => _service.SendReceiverNotificationAsync(parcel, scanEvent);
        await act.Should().NotThrowAsync();
    }
}
