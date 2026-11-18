using GamingClub.API;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<BookingStore>();
builder.Services.AddSingleton<OccupancyStore>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<SessionExtensionStore>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// IMPORTANT: Development-only prototype. Identity and access control are NOT
// implemented yet. Keep the server bound to localhost until authentication exists.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "GamingClub.API",
    status = "ok",
    variant = 18,
    seed = 241810
}));

app.MapGet("/api/seats", async (BookingStore db, CancellationToken ct) =>
    Results.Ok(await db.GetSeatsAsync(ct)))
    .WithName("GetSeats");

app.MapGet("/api/tariffs", async (BookingStore db, CancellationToken ct) =>
    Results.Ok(await db.GetTariffsAsync(ct)))
    .WithName("GetTariffs");

// Occupancy is an administrative report. Until authorization is implemented,
// expose it only in Development and keep the API on localhost.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/dev/reports/occupancy", async (DateTimeOffset from, DateTimeOffset to,
        OccupancyStore db, CancellationToken ct) =>
    {
        var start = from.UtcDateTime;
        var end = to.UtcDateTime;
        if (end <= start || end - start > TimeSpan.FromDays(31))
            return Results.BadRequest(new { error = "Period must be positive and at most 31 days." });
        return Results.Ok(await db.GetReportAsync(start, end, ct));
    }).WithName("GetDevOccupancy");

    app.MapGet("/api/dev/reports/occupancy.csv", async (DateTimeOffset from, DateTimeOffset to,
        OccupancyStore db, CancellationToken ct) =>
    {
        var start = from.UtcDateTime;
        var end = to.UtcDateTime;
        if (end <= start || end - start > TimeSpan.FromDays(31))
            return Results.BadRequest(new { error = "Period must be positive and at most 31 days." });
        var report = await db.GetReportAsync(start, end, ct);
        return Results.File(CsvReport.Export(report), "text/csv; charset=utf-8", "gamingclub-occupancy.csv");
    }).WithName("ExportDevOccupancy");
}

// No production booking endpoint is exposed before authentication is implemented.
// This development-only endpoint is used to verify SQL transactions and concurrency.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/dev/seats/{seatId:int}/bookings", async (int seatId, BookingStore db, CancellationToken ct) =>
    {
        if (seatId <= 0) return Results.BadRequest(new { error = "Invalid seat ID." });
        return Results.Ok(await db.GetBookingsAsync(seatId, ct));
    }).WithName("GetDevSeatBookings");

    app.MapGet("/api/dev/sessions/{bookingId:int}", async (int bookingId,
        SessionStore store, CancellationToken ct) =>
    {
        var clock = await store.GetClockAsync(bookingId, ct);
        return clock is null ? Results.NotFound() : Results.Ok(clock);
    }).WithName("GetDevSessionClock");

    app.MapPost("/api/dev/sessions/{bookingId:int}/extend", async (int bookingId,
        ExtendSessionRequest request, SessionExtensionStore store, CancellationToken ct) =>
    {
        var result = await store.ExtendAsync(bookingId, request.NewEndUtc, ct);
        if (result.Success) return Results.Ok(result);
        return result.Conflict ? Results.Conflict(new { error = result.Message })
            : Results.BadRequest(new { error = result.Message });
    }).WithName("ExtendDevSession");

    app.MapPost("/api/dev/sessions/{bookingId:int}/start", async (int bookingId,
        SessionStore store, CancellationToken ct) =>
    {
        var result = await store.StartAsync(bookingId, ct);
        return result.Success ? Results.Ok(new { message = result.Message })
            : Results.BadRequest(new { error = result.Message });
    }).WithName("StartDevSession");

    app.MapPost("/api/dev/bookings", async (CreateBookingRequest input, BookingStore db, CancellationToken ct) =>
    {
        var result = await db.CreateAsync(input, ct);
        return result.Error switch
        {
            BookingError.None => Results.Created($"/api/seats/{input.SeatId}/bookings", result.Booking),
            BookingError.Invalid => Results.BadRequest(new { error = result.Message }),
            BookingError.NotFound => Results.NotFound(new { error = result.Message }),
            BookingError.Conflict => Results.Conflict(new { error = result.Message }),
            _ => Results.Problem("Unexpected booking state.")
        };
    }).WithName("CreateDevBooking");
}

app.Run();

public partial class Program { }
