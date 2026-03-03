using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParcelTracking.Domain.Models;
using ParcelTracking.Infrastructure.Abstractions;
using ParcelTracking.Infrastructure.Persistence;

namespace ParcelTracking.Infrastructure.Repositories;

public sealed class ParcelRepository : IParcelRepository
{
    private readonly ParcelTrackingDbContext _db;

    public ParcelRepository(ParcelTrackingDbContext db) => _db = db;

    public async Task<Parcel?> GetByTrackingIdAsync(string trackingId, CancellationToken ct = default)
    {
        var entity = await _db.Parcels
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TrackingId == trackingId, ct);

        return entity is null ? null : MapToDomain(entity);
    }

    public async Task AddAsync(Parcel parcel, CancellationToken ct = default)
    {
        var entity = MapToEntity(parcel);
        _db.Parcels.Add(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Parcel parcel, CancellationToken ct = default)
    {
        var entity = await _db.Parcels.FindAsync([parcel.TrackingId], ct)
            ?? throw new InvalidOperationException($"Parcel '{parcel.TrackingId}' not found for update.");

        entity.Status = parcel.Status;
        entity.UpdatedAt = parcel.UpdatedAt;
        entity.SizeClass = parcel.SizeClass;
        entity.BaseCharge = parcel.BaseCharge;
        entity.LargeSurcharge = parcel.LargeSurcharge;
        entity.TotalCharge = parcel.TotalCharge;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> ExistsAsync(string trackingId, CancellationToken ct = default)
        => await _db.Parcels.AnyAsync(p => p.TrackingId == trackingId, ct);

    // ---------------------------------------------------------------------------

    private static Parcel MapToDomain(ParcelEntity e) => new()
    {
        TrackingId = e.TrackingId,
        Status = e.Status,
        SizeClass = e.SizeClass,
        Dimensions = new(e.LengthCm, e.WidthCm, e.HeightCm, e.WeightKg),
        FromAddress = new(e.FromLine1, e.FromCity, e.FromPostcode, e.FromCountry),
        ToAddress = new(e.ToLine1, e.ToCity, e.ToPostcode, e.ToCountry),
        Sender = new(e.SenderName, e.SenderContactNumber, e.SenderEmail),
        Receiver = new(e.ReceiverName, e.ReceiverContactNumber, e.ReceiverEmail, e.ReceiverNotificationOptIn),
        BaseCharge = e.BaseCharge,
        LargeSurcharge = e.LargeSurcharge,
        TotalCharge = e.TotalCharge,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    private static ParcelEntity MapToEntity(Parcel p) => new()
    {
        TrackingId = p.TrackingId,
        Status = p.Status,
        SizeClass = p.SizeClass,
        LengthCm = p.Dimensions.LengthCm,
        WidthCm = p.Dimensions.WidthCm,
        HeightCm = p.Dimensions.HeightCm,
        WeightKg = p.Dimensions.WeightKg,
        FromLine1 = p.FromAddress.Line1,
        FromCity = p.FromAddress.City,
        FromPostcode = p.FromAddress.Postcode,
        FromCountry = p.FromAddress.Country,
        ToLine1 = p.ToAddress.Line1,
        ToCity = p.ToAddress.City,
        ToPostcode = p.ToAddress.Postcode,
        ToCountry = p.ToAddress.Country,
        SenderName = p.Sender.Name,
        SenderContactNumber = p.Sender.ContactNumber,
        SenderEmail = p.Sender.Email,
        ReceiverName = p.Receiver.Name,
        ReceiverContactNumber = p.Receiver.ContactNumber,
        ReceiverEmail = p.Receiver.Email,
        ReceiverNotificationOptIn = p.Receiver.NotificationOptIn,
        BaseCharge = p.BaseCharge,
        LargeSurcharge = p.LargeSurcharge,
        TotalCharge = p.TotalCharge,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
    };
}
