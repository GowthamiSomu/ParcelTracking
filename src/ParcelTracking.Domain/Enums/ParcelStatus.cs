namespace ParcelTracking.Domain.Enums;

public enum ParcelStatus
{
    COLLECTED,
    SOURCE_SORT,
    DESTINATION_SORT,
    DELIVERY_CENTRE,
    READY_FOR_DELIVERY,
    DELIVERED,
    FAILED_TO_DELIVER,
    RETURNED
}
