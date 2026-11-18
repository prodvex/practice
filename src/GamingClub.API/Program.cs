var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Week 1 skeleton only. Booking and pricing endpoints will follow.
app.MapGet("/health", () => Results.Ok(new
{
    service = "GamingClub.API",
    status = "ok",
    variant = 18,
    seed = 241810
}));

app.Run();

// Allows API integration tests to host the application later.
public partial class Program { }
