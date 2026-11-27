using GamingClub.API;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

if (args is ["--initialize-demo", var scripts, var dataDirectory])
{
    await DemoDatabase.InitializeAsync(scripts, dataDirectory);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<BookingStore>();
builder.Services.AddSingleton<OccupancyStore>();
builder.Services.AddSingleton<RevenueStore>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<SessionExtensionStore>();
builder.Services.AddSingleton<AuthStore>();
builder.Services.AddSingleton<BookingAccess>();
builder.Services.AddSingleton<CancellationStore>();
builder.Services.AddSingleton<AdminStore>();
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
// A small in-memory protection against repeated password attempts.
// For multi-instance deployments use a shared limiter and trusted forwarded IPs.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 8,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Authentication is implemented for the secured routes below. The legacy
// /api/dev routes bypass authorization: never expose Development API publicly.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("LocalDemo"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/auth/register", async (RegisterRequest request, AuthStore auth, TokenService tokens, CancellationToken ct) =>
{
    var outcome = await auth.RegisterAsync(request, ct);
    if (outcome.User is null) return Results.BadRequest(new { error = outcome.Error });
    var user = outcome.User;
    return Results.Ok(new AuthResponse(tokens.Issue(user), "Bearer", 7200,
        user.UserId, user.Login, user.Role, user.DisplayName));
}).WithName("RegisterVisitor").RequireRateLimiting("auth");

app.MapPost("/api/auth/login", async (LoginRequest request, AuthStore auth, TokenService tokens, CancellationToken ct) =>
{
    var outcome = await auth.LoginAsync(request, ct);
    if (outcome.User is null) return Results.Unauthorized();
    var user = outcome.User;
    return Results.Ok(new AuthResponse(tokens.Issue(user), "Bearer", 7200,
        user.UserId, user.Login, user.Role, user.DisplayName));
}).WithName("LoginUser").RequireRateLimiting("auth");

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

app.MapPost("/api/bookings", async (AuthenticatedBookingRequest request, ClaimsPrincipal principal,
    BookingStore db, CancellationToken ct) =>
{
    if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    // Never trust a client-supplied UserId.
    var result = await db.CreateAsync(new CreateBookingRequest(userId, request.SeatId, request.TariffId,
        request.StartUtc, request.EndUtc), ct);
    return result.Error switch
    {
        BookingError.None => Results.Ok(result.Booking),
        BookingError.Invalid => Results.BadRequest(new { error = result.Message }),
        BookingError.NotFound => Results.NotFound(new { error = result.Message }),
        BookingError.Conflict => Results.Conflict(new { error = result.Message }),
        _ => Results.Problem()
    };
}).RequireAuthorization();

// An authenticated visitor may cancel only their own, not-yet-started booking.
app.MapPost("/api/bookings/{bookingId:int}/cancel", async (int bookingId,
    ClaimsPrincipal principal, BookingAccess access, CancellationStore store, CancellationToken ct) =>
{
    if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    if (!await access.IsOwnerAsync(bookingId, userId, ct))
        return Results.NotFound();
    var result = await store.CancelAsync(bookingId, ct);
    return result.Error switch
    {
        CancellationError.None => Results.Ok(result),
        CancellationError.NotFound => Results.NotFound(new { error = result.Message }),
        CancellationError.Conflict => Results.Conflict(new { error = result.Message }),
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

app.MapPost("/api/sessions/{bookingId:int}/finish", async (int bookingId, ClaimsPrincipal principal,
    BookingAccess access, SessionStore store, CancellationToken ct) =>
{
    if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        return Results.Unauthorized();
    if (!principal.IsInRole("ADMIN") && !await access.IsOwnerAsync(bookingId, userId, ct))
        return Results.NotFound();
    var result = await store.FinishAsync(bookingId, ct);
    return result.Success ? Results.Ok(result) : Results.BadRequest(new { error = result.Message });
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

// Administrative endpoints: JWT ADMIN role is required on every route.
var admin = app.MapGroup("/api/admin").RequireAuthorization(policy => policy.RequireRole("ADMIN"));

admin.MapPost("/bookings/{bookingId:int}/cancel", async (int bookingId,
    CancellationStore store, CancellationToken ct) =>
{
    var result = await store.CancelAsync(bookingId, ct);
    return result.Error switch
    {
        CancellationError.None => Results.Ok(result),
        CancellationError.NotFound => Results.NotFound(new { error = result.Message }),
        CancellationError.Conflict => Results.Conflict(new { error = result.Message }),
        _ => Results.Problem()
    };
});

admin.MapGet("/bookings", async (AdminStore db, CancellationToken ct) =>
    Results.Ok(await db.RecentBookingsAsync(ct)));

admin.MapGet("/tariffs", async (AdminStore db, CancellationToken ct) =>
    Results.Ok(await db.AllTariffsAsync(ct)));

admin.MapPost("/tariffs", async (CreateTariffRequest request, AdminStore db, CancellationToken ct) =>
{
    var result = await db.CreateTariffAsync(request, ct);
    return result.Success
        ? Results.Created($"/api/admin/tariffs/{result.Id}", result)
        : Results.BadRequest(new { error = result.Message });
});

admin.MapPost("/tariffs/{tariffId:int}/deactivate", async (int tariffId, AdminStore db, CancellationToken ct) =>
    await db.DeactivateTariffAsync(tariffId, ct) ? Results.NoContent() : Results.NotFound());

admin.MapGet("/reports/occupancy.csv", async (DateTimeOffset from, DateTimeOffset to,
    OccupancyStore db, CancellationToken ct) =>
{
    if (to <= from || to - from > TimeSpan.FromDays(31))
        return Results.BadRequest(new { error = "Period must be positive and at most 31 days." });
    var report = await db.GetReportAsync(from.UtcDateTime, to.UtcDateTime, ct);
    return Results.File(CsvReport.Export(report), "text/csv; charset=utf-8", "gamingclub-occupancy.csv");
});

admin.MapGet("/reports/revenue", async (DateTimeOffset from, DateTimeOffset to,
    RevenueStore db, CancellationToken ct) =>
{
    if (to <= from || to - from > TimeSpan.FromDays(31))
        return Results.BadRequest(new { error = "Period must be positive and at most 31 days." });
    return Results.Ok(await db.GetReportAsync(from.UtcDateTime, to.UtcDateTime, ct));
});

admin.MapGet("/reports/revenue.csv", async (DateTimeOffset from, DateTimeOffset to,
    RevenueStore db, CancellationToken ct) =>
{
    if (to <= from || to - from > TimeSpan.FromDays(31))
        return Results.BadRequest(new { error = "Period must be positive and at most 31 days." });
    var report = await db.GetReportAsync(from.UtcDateTime, to.UtcDateTime, ct);
    return Results.File(CsvReport.Export(report), "text/csv; charset=utf-8", "gamingclub-revenue.csv");
});

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

    app.MapPost("/api/dev/sessions/{bookingId:int}/finish", async (int bookingId,
        SessionStore store, CancellationToken ct) =>
    {
        var result = await store.FinishAsync(bookingId, ct);
        return result.Success ? Results.Ok(result)
            : Results.BadRequest(new { error = result.Message });
    }).WithName("FinishDevSession");

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

var portFile = app.Configuration["GAMINGCLUB_PORT_FILE"];
if (app.Environment.IsEnvironment("LocalDemo") && !string.IsNullOrWhiteSpace(portFile))
{
    app.Urls.Clear();
    app.Urls.Add("http://127.0.0.1:0");
    await app.StartAsync();
    var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
    var port = new Uri(addresses!.Addresses.Single()).Port;
    await File.WriteAllTextAsync(portFile + ".tmp", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    File.Move(portFile + ".tmp", portFile, overwrite: true);
    await app.WaitForShutdownAsync();
}
else
{
    app.Run();
}

public partial class Program { }
