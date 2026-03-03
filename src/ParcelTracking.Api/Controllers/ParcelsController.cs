using Microsoft.AspNetCore.Mvc;
using ParcelTracking.Api.Models;
using ParcelTracking.Infrastructure.Abstractions;

namespace ParcelTracking.Api.Controllers;

[ApiController]
[Route("api/parcels")]
[Produces("application/json")]
public sealed class ParcelsController : ControllerBase
{
    private static readonly System.Text.RegularExpressions.Regex TrackingIdRegex =
        new(@"^[A-Za-z0-9\-]{8,30}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly IParcelRepository _parcels;
    private readonly IScanEventRepository _events;
    private readonly ILogger<ParcelsController> _logger;

    public ParcelsController(
        IParcelRepository parcels,
        IScanEventRepository events,
        ILogger<ParcelsController> logger)
    {
        _parcels = parcels;
        _events = events;
        _logger = logger;
    }

    /// <summary>Returns the current state and header info for a parcel.</summary>
    [HttpGet("{trackingId}")]
    [ProducesResponseType(typeof(ParcelResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetParcel(string trackingId, CancellationToken ct)
    {
        if (!TrackingIdRegex.IsMatch(trackingId))
        {
            return BadRequest(new ErrorResponse(new ErrorDetail(
                "INVALID_TRACKING_ID",
                "trackingId must be 8–30 alphanumeric characters (dashes allowed).")));
        }

        var parcel = await _parcels.GetByTrackingIdAsync(trackingId, ct);
        if (parcel is null)
        {
            return NotFound(new ErrorResponse(new ErrorDetail(
                "PARCEL_NOT_FOUND",
                $"No parcel found with trackingId '{trackingId}'.")));
        }

        var response = new ParcelResponse(
            TrackingId: parcel.TrackingId,
            CurrentStatus: parcel.Status.ToString(),
            SizeClass: parcel.SizeClass.ToString(),
            Charges: new ChargesDto(parcel.BaseCharge, parcel.LargeSurcharge, parcel.TotalCharge),
            From: new AddressDto(parcel.FromAddress.Line1, parcel.FromAddress.City,
                                 parcel.FromAddress.Postcode, parcel.FromAddress.Country),
            To: new AddressDto(parcel.ToAddress.Line1, parcel.ToAddress.City,
                               parcel.ToAddress.Postcode, parcel.ToAddress.Country),
            Sender: new ContactDto(parcel.Sender.Name, parcel.Sender.ContactNumber, parcel.Sender.Email),
            Receiver: new ReceiverDto(parcel.Receiver.Name, parcel.Receiver.NotificationOptIn)
        );

        return Ok(response);
    }

    /// <summary>
    /// Returns all scan events for a parcel in reverse chronological order (latest first).
    /// </summary>
    [HttpGet("{trackingId}/events")]
    [ProducesResponseType(typeof(ParcelEventsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetParcelEvents(
        string trackingId,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        if (!TrackingIdRegex.IsMatch(trackingId))
        {
            return BadRequest(new ErrorResponse(new ErrorDetail(
                "INVALID_TRACKING_ID",
                "trackingId must be 8–30 alphanumeric characters (dashes allowed).")));
        }

        if (limit <= 0 || limit > 500)
        {
            return BadRequest(new ErrorResponse(new ErrorDetail(
                "INVALID_LIMIT",
                "limit must be between 1 and 500.")));
        }

        if (!await _parcels.ExistsAsync(trackingId, ct))
        {
            return NotFound(new ErrorResponse(new ErrorDetail(
                "PARCEL_NOT_FOUND",
                $"No parcel found with trackingId '{trackingId}'.")));
        }

        var scanEvents = await _events.GetByTrackingIdAsync(trackingId, limit, ct);

        var response = new ParcelEventsResponse(
            TrackingId: trackingId,
            Events: scanEvents.Select(e => new ScanEventDto(
                EventId: e.EventId,
                EventType: e.EventType.ToString(),
                EventTimeUtc: e.EventTimeUtc.ToString("O"),
                LocationId: e.LocationId,
                HubType: e.HubType,
                ActorId: e.ActorId,
                Metadata: e.Metadata
            )).ToList()
        );

        return Ok(response);
    }
}
