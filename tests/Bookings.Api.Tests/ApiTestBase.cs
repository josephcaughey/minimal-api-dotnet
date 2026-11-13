using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Bookings.Api.Tests;

public abstract class ApiTestBase : IDisposable
{
    protected ApiTestBase()
    {
        Factory = new BookingsApiFactory();
        Client = Factory.CreateClient();
    }

    protected BookingsApiFactory Factory { get; }
    protected HttpClient Client { get; }
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A time on 2 June 2026, the day after the test clock's "now".</summary>
    protected static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 6, 2, hour, minute, 0, TimeSpan.Zero);

    protected async Task<RoomResponse> CreateRoomAsync(string name = "Fir", int capacity = 8)
    {
        var response = await Client.PostAsJsonAsync("/rooms", new CreateRoom(name, capacity), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RoomResponse>(Ct))!;
    }

    protected Task<HttpResponseMessage> BookAsync(int roomId, DateTimeOffset start, DateTimeOffset end, int attendees = 4) =>
        Client.PostAsJsonAsync("/bookings", new CreateBooking(roomId, start, end, "Sam Lee", attendees), Ct);

    protected async Task<BookingResponse> BookOkAsync(int roomId, DateTimeOffset start, DateTimeOffset end)
    {
        var response = await BookAsync(roomId, start, end);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Ct))!;
    }

    /// <summary>Reads a problem response and returns its parsed body.</summary>
    protected static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    protected static string[] Errors(JsonElement problem, string field) =>
        problem.GetProperty("errors").TryGetProperty(field, out var messages)
            ? messages.EnumerateArray().Select(m => m.GetString()!).ToArray()
            : [];

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
