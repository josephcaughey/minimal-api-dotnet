using System.Net;
using System.Net.Http.Json;

namespace Bookings.Api.Tests;

public sealed class RoomTests : ApiTestBase
{
    [Fact]
    public async Task Created_rooms_are_listed_by_name()
    {
        var response = await Client.PostAsJsonAsync("/rooms", new CreateRoom("Spruce", 12), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/rooms/1", response.Headers.Location?.ToString());
        await CreateRoomAsync("Arbutus", 4);

        var rooms = await Client.GetFromJsonAsync<List<RoomResponse>>("/rooms", Ct);
        Assert.Equal(["Arbutus", "Spruce"], rooms!.Select(r => r.Name));
    }

    [Fact]
    public async Task Invalid_room_is_rejected_with_field_errors()
    {
        var response = await Client.PostAsJsonAsync("/rooms", new CreateRoom("", 0), Ct);
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.NotEmpty(Errors(problem, "Name"));
        Assert.NotEmpty(Errors(problem, "Capacity"));
    }

    [Fact]
    public async Task Room_names_are_unique()
    {
        await CreateRoomAsync("Fir");
        var response = await Client.PostAsJsonAsync("/rooms", new CreateRoom("Fir", 3), Ct);
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(["A room called Fir already exists."], Errors(problem, "Name"));
    }

    [Fact]
    public async Task Bookings_in_a_window_come_back_in_order()
    {
        var room = await CreateRoomAsync();
        var other = await CreateRoomAsync("Cedar");
        await BookOkAsync(room.Id, At(14), At(15));
        await BookOkAsync(room.Id, At(9), At(10));
        await BookOkAsync(other.Id, At(9), At(10));
        await BookOkAsync(room.Id, At(9).AddDays(3), At(10).AddDays(3));

        var url = $"/rooms/{room.Id}/bookings?from={Uri.EscapeDataString(At(0).ToString("O"))}&to={Uri.EscapeDataString(At(23).ToString("O"))}";
        var bookings = await Client.GetFromJsonAsync<List<BookingResponse>>(url, Ct);

        Assert.Equal([At(9), At(14)], bookings!.Select(b => b.Start));
    }

    [Fact]
    public async Task Window_must_be_forward_and_at_most_a_month()
    {
        var room = await CreateRoomAsync();
        foreach (var (from, to) in new[] { (At(10), At(9)), (At(0), At(0).AddDays(32)) })
        {
            var url = $"/rooms/{room.Id}/bookings?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
            var problem = await ProblemAsync(await Client.GetAsync(url, Ct), HttpStatusCode.BadRequest);
            Assert.NotEmpty(Errors(problem, "to"));
        }
    }

    [Fact]
    public async Task Bookings_for_an_unknown_room_is_not_found()
    {
        var url = $"/rooms/99/bookings?from=2026-06-02T00:00:00Z&to=2026-06-03T00:00:00Z";
        await ProblemAsync(await Client.GetAsync(url, Ct), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        Assert.Equal("Healthy", await Client.GetStringAsync("/health", Ct));
    }

    [Fact]
    public async Task OpenApi_document_lists_the_endpoints()
    {
        var document = await Client.GetStringAsync("/openapi/v1.json", Ct);
        Assert.Contains("\"/bookings/{id}\"", document, StringComparison.Ordinal);
        Assert.Contains("\"/rooms/{id}/bookings\"", document, StringComparison.Ordinal);
    }
}
