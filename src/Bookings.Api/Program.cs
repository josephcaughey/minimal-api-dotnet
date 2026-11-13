using Bookings.Api.Bookings;
using Bookings.Api.Data;
using Bookings.Api.Rooms;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

builder.Services.AddDbContext<BookingsDb>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Bookings")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BookingGate>();
builder.Services.AddScoped<BookingService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// The schema is small and has no history yet, so it's created on start
// rather than through migrations.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<BookingsDb>().Database.EnsureCreated();
}

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapRooms();
app.MapBookings();

app.Run();

public partial class Program;
