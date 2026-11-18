using GamingClub.API;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<BookingStore>();
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
    .WithName("GetSeats").WithOpenApi();

app.MapGet("/api/tariffs", async (BookingStore db, CancellationToken ct) =>
    Results.Ok(await db.GetTariffsAsync(ct)))
    .WithName("GetTariffs").WithOpenApi();

app.MapGet("/api/seats/{seatId:int}/bookings", async (int seatId, BookingStore db, CancellationToken ct) =>
{
    if (seatId <= 0) return Results.BadRequest(new { error = "Invalid seat ID." });
    return Results.Ok(await db.GetBookingsAsync(seatId, ct));
}).WithName("GetSeatBookings").WithOpenApi();

// No production booking endpoint is exposed before authentication is implemented.
// This development-only endpoint is used to verify SQL transactions and concurrency.
if (app.Environment.IsDevelopment())
{
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
    }).WithName("CreateDevBooking").WithOpenApi();
}

app.Run();

public partial class Program { }
