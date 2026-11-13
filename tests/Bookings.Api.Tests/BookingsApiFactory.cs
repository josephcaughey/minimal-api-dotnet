using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Bookings.Api.Tests;

/// <summary>
/// Runs the real app against a private in-memory SQLite database and a clock
/// the test controls.
/// </summary>
public sealed class BookingsApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly string connectionString =
        $"Data Source=file:bookings-{Guid.NewGuid():N}?mode=memory&cache=shared";

    // A shared in-memory database lives only while a connection is open.
    private readonly SqliteConnection keepAlive;

    public BookingsApiFactory()
    {
        keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
    }

    public FakeTimeProvider Clock { get; } = new(Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Bookings", connectionString);
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            keepAlive.Dispose();
        }
    }
}
