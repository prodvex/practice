namespace GamingClub.API;

/// <summary>DTO for a new gaming-seat reservation. Times include an explicit offset.</summary>
public sealed record CreateBookingRequest(int UserId, int SeatId, int TariffId, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>Information needed to draw a hall on PC and Android.</summary>
public sealed record SeatDto(int SeatId, int HallId, int SeatNumber, int X, int Y, bool IsActive);

/// <summary>Pricing data loaded from SQL Server.</summary>
public sealed record TariffDto(int TariffId, string Name, string TariffType, decimal PriceRub,
    int? PackageMinutes, TimeSpan? StartLocalTime, TimeSpan? EndLocalTime);

/// <summary>API representation of a booked seat.</summary>
public sealed record BookingDto(int BookingId, int UserId, int SeatId, int TariffId,
    DateTime StartUtc, DateTime EndUtc, string Status, decimal PriceRub);

public enum BookingError { None, Invalid, NotFound, Conflict }

/// <summary>Service operation result, including a safe HTTP-mappable error.</summary>
public sealed record BookingResult(BookingDto? Booking, BookingError Error, string? Message = null);

/// <summary>New UTC end of an ongoing game session.</summary>
public sealed record ExtendSessionRequest(DateTimeOffset NewEndUtc);

/// <summary>Authenticated booking request; user identity is never accepted from the client.</summary>
public sealed record AuthenticatedBookingRequest(int SeatId, int TariffId,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc);
