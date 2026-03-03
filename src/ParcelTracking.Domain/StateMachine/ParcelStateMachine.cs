using ParcelTracking.Domain.Enums;

namespace ParcelTracking.Domain.StateMachine;

/// <summary>
/// Explicit transition map for parcel lifecycle status transitions.
/// Null key represents the initial "no status" state (parcel not yet created).
/// </summary>
public static class ParcelStateMachine
{
    // Sentinel value used to represent "no current status" (parcel not yet created).
    private const int NullStatusSentinel = -1;

    private static readonly Dictionary<int, HashSet<ParcelStatus>> _transitions =
        new()
        {
            [NullStatusSentinel]                      = [ParcelStatus.COLLECTED],
            [(int)ParcelStatus.COLLECTED]             = [ParcelStatus.SOURCE_SORT],
            [(int)ParcelStatus.SOURCE_SORT]           = [ParcelStatus.DESTINATION_SORT],
            [(int)ParcelStatus.DESTINATION_SORT]      = [ParcelStatus.DELIVERY_CENTRE],
            [(int)ParcelStatus.DELIVERY_CENTRE]       = [ParcelStatus.READY_FOR_DELIVERY],
            [(int)ParcelStatus.READY_FOR_DELIVERY]    = [ParcelStatus.DELIVERED, ParcelStatus.FAILED_TO_DELIVER],
            [(int)ParcelStatus.FAILED_TO_DELIVER]     = [ParcelStatus.READY_FOR_DELIVERY, ParcelStatus.RETURNED],
        };

    /// <summary>
    /// Returns true if transitioning from <paramref name="current"/> to <paramref name="next"/> is valid.
    /// Pass <c>null</c> for <paramref name="current"/> when creating a new parcel.
    /// </summary>
    public static bool IsValidTransition(ParcelStatus? current, ParcelStatus next)
    {
        int key = current.HasValue ? (int)current.Value : NullStatusSentinel;
        return _transitions.TryGetValue(key, out var allowed) && allowed.Contains(next);
    }

    /// <summary>Returns all valid next statuses from the given current status.</summary>
    public static IReadOnlySet<ParcelStatus> GetAllowedTransitions(ParcelStatus? current)
    {
        int key = current.HasValue ? (int)current.Value : NullStatusSentinel;
        return _transitions.TryGetValue(key, out var allowed)
            ? allowed
            : new HashSet<ParcelStatus>();
    }
}
