namespace ParcelTracking.Domain.Models;

public sealed record Contact(
    string Name,
    string ContactNumber,
    string Email,
    bool NotificationOptIn = false
);
