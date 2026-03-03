namespace ParcelTracking.Domain.Models;

public sealed record Address(
    string Line1,
    string City,
    string Postcode,
    string Country
);
