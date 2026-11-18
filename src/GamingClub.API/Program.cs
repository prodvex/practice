using GamingClub.API;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<BookingStore>();
builder.Services.AddSingleton<OccupancyStore>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<SessionExtensionStore>();
builder.Services.AddSingleton<AuthStore>();
builder.Services.AddSingleton<BookingAccess>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var tokenService = new TokenService(builder.Configuration);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = tokenService.Issuer,
            ValidateAudience = true,
            ValidAudience = tokenService.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = tokenService.Key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Authentication is implemented for the secured routes below. The legacy
// /api/dev routes bypass authorization: never expose Development API publicly.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/auth/register", async (RegisterRequest request, AuthStore auth, TokenService tokens, CancellationToken ct) =>
{
    var outcome = await auth.RegisterAsync(request, ct);
    if (outcome.User is null) return Results.BadRequest(new { error = outcome.Error });
    var user = outcome.User;
    return Results.Ok(new AuthResponse(tokens.Issue(user), "Bearer", 7200,
        user.UserId, user.Login, user.Role, user.DisplayName));
}).WithName("RegisterVisitor");

app.MapPost("/api/auth/login", async (LoginRequest request, AuthStore auth, TokenService tokens, CancellationToken ct) =>
{
    var outcome = await auth.LoginAsync(request, ct);
    if (outcome.User is null) return Results.Unauthorized();
    var user = outcome.User;
    return Results.Ok(new AuthResponse(tokens.Issue(user), "Bearer", 7200,
        user.UserId, user.Login, user.Role, user.DisplayName));
}).WithName("LoginUser");

app.MapGet("/api/me", (ClaimsPrincipal principal) => Results.Ok(new
{
    userId = principal.FindFirstValue(ClaimTypes.NameIdentifier),
    login = principal.Identity?.Name,
    role = principal.FindFirstValue(ClaimTypes.Role)
})).RequireAuthorization();

app.MapGet("/api/bookings/mine", async (ClaimsPrincipal principal, BookingAccess db, CancellationToken ct) =>
{
    if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    return Results.Ok(await db.MineAsync(userId, ct));
}).RequireAuthorization();

app.MapPost("/api/bookings", async (CreateBookingRequest request, ClaimsPrincipal principal,
    BookingStore db, CancellationToken ct) =>
{
    if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    // Never trust a client-supplied UserId.
    var result = await db.CreateAsync(request with { UserId = userId }, ct);
    return result.Error switch
    {
        BookingError.None => Results.Ok(result.Booking),
        BookingError.Invalid => Results.BadRequest(new { error = result.Message }),
        BookingError.NotFound => Results.NotFound(new { error = result.Message }),
        BookingError.Conflict => Results.Conflict(new { error = result.Message }),
        _ => Results.Problem()
    };
}).RequireAuthorization();

app.MapGet("/api/sessions/{bookingId:int}", async (int bookingId, ClaimsPrincipal principal,
    BookingAccess access, SessionStore store, CancellationToken ct) =>
{
    if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    if (!principal.IsInRole("ADMIN") && !await access.IsOwnerAsync(bookingId, userId, ct))
        return Results.NotFound();
    var clock = await store.GetClockAsync(bookingId, ct);
    return clock is null ? Results.NotFound() : Results.Ok(clock);
}).RequireAuthorization();

app.MapPost("/api/sessions/{bookingId:int}/start", async (int bookingId, ClaimsPrincipal principal,
    BookingAccess access, SessionStore store, CancellationToken ct) =>
{
    if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    if (!principal.IsInRole("ADMIN") && !await access.IsOwnerAsync(bookingId, userId, ct))
        return Results.NotFound();
    var result = await store.StartAsync(bookingId, ct);
    return result.Success ? Results.Ok(new { message = result.Message }) :
        Results.BadRequest(new { error = result.Message });
}).RequireAuthorization();

app.MapPost("/api/sessions/{bookingId:int}/extend", async (int bookingId, ExtendSessionRequest request,
    ClaimsPrincipal principal, BookingAccess access, SessionExtensionStore store, CancellationToken ct) =>
{
    if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    if (!principal.IsInRole("ADMIN") && !await access.IsOwnerAsync(bookingId, userId, ct))
        return Results.NotFound();
    var result = await store.ExtendAsync(bookingId, request.NewEndUtc, ct);
    return result.Success ? Results.Ok(result) : result.Conflict
        ? Results.Conflict(new { error = result.Message })
        : Results.BadRequest(new { error = result.Message });
}).RequireAuthorization();

app.MapGet("/api/admin/reports/occupancy", async (DateTimeOffset from, DateTimeOffset to,
    OccupancyStore db, CancellationToken ct) =>
{
    if (to <= from || to - from > TimeSpan.FromDays(31))
        return Results.BadRequest(new { error = "Period must be positive and at most 31 days." });
    return Results.Ok(await db.GetReportAsync(from.UtcDateTime, to.UtcDateTime, ct));
}).RequireAuthorization(policy => policy.RequireRole("ADMIN"));

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
