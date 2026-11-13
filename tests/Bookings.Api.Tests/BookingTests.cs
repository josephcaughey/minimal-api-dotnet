using System.Net;
using System.Net.Http.Json;

namespace Bookings.Api.Tests;

public sealed class BookingTests : ApiTestBase
{
    [Fact]
    public async Task Booking_is_created_with_location_and_etag()
    {
        var room = await CreateRoomAsync();
        var response = await BookAsync(room.Id, At(9), At(10, 30));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/bookings/1", response.Headers.Location?.ToString());
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);

        var fetched = await Client.GetAsync("/bookings/1", Ct);
        var booking = await fetched.Content.ReadFromJsonAsync<BookingResponse>(Ct);
        Assert.Equal(new BookingResponse(1, room.Id, At(9), At(10, 30), "Sam Lee", 4), booking);
        Assert.Equal("\"1\"", fetched.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Times_in_other_offsets_are_stored_as_utc()
    {
        var room = await CreateRoomAsync();
        var pacific = TimeSpan.FromHours(-7);
        var start = new DateTimeOffset(2026, 6, 2, 9, 0, 0, pacific);
        var booking = await BookOkAsync(room.Id, start, start.AddHours(1));
        Assert.Equal(At(16), booking.Start);
        Assert.Equal(TimeSpan.Zero, booking.Start.Offset);
    }

    [Theory]
    [InlineData(10, 9, "End must be after start.")]
    [InlineData(9, 9, "End must be after start.")]
    [InlineData(8, 17, "A booking can last at most 8 hours.")]
    public async Task Bad_time_ranges_are_rejected(int startHour, int endHour, string message)
    {
        var room = await CreateRoomAsync();
        var problem = await ProblemAsync(await BookAsync(room.Id, At(startHour), At(endHour)), HttpStatusCode.BadRequest);
        Assert.Equal([message], Errors(problem, "End"));
    }

    [Fact]
    public async Task Bookings_cannot_start_in_the_past()
    {
        var room = await CreateRoomAsync();
        var start = BookingsApiFactory.Now.AddMinutes(-1);
        var problem = await ProblemAsync(await BookAsync(room.Id, start, start.AddHours(1)), HttpStatusCode.BadRequest);
        Assert.Equal(["Start must not be in the past."], Errors(problem, "Start"));
    }

    [Fact]
    public async Task Attendees_must_fit_the_room()
    {
        var room = await CreateRoomAsync(capacity: 3);
        var problem = await ProblemAsync(await BookAsync(room.Id, At(9), At(10), attendees: 4), HttpStatusCode.BadRequest);
        Assert.Equal(["The room holds 3 people."], Errors(problem, "Attendees"));
    }

    [Fact]
    public async Task Unknown_room_is_a_validation_error()
    {
        var problem = await ProblemAsync(await BookAsync(42, At(9), At(10)), HttpStatusCode.BadRequest);
        Assert.Equal(["Room 42 does not exist."], Errors(problem, "RoomId"));
    }

    // Each range overlaps an existing 09:00-11:00 booking.
    [Theory]
    [InlineData(9, 0, 11, 0)]
    [InlineData(8, 0, 9, 30)]
    [InlineData(10, 30, 11, 30)]
    [InlineData(9, 30, 10, 30)]
    [InlineData(8, 0, 12, 0)]
    public async Task Overlapping_booking_is_a_conflict(int startHour, int startMinute, int endHour, int endMinute)
    {
        var room = await CreateRoomAsync();
        var existing = await BookOkAsync(room.Id, At(9), At(11));

        var response = await BookAsync(room.Id, At(startHour, startMinute), At(endHour, endMinute));
        var problem = await ProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal(existing.Id, problem.GetProperty("conflictingBookingId").GetInt32());
    }

    [Fact]
    public async Task Back_to_back_bookings_and_other_rooms_are_fine()
    {
        var room = await CreateRoomAsync();
        var other = await CreateRoomAsync("Cedar");
        await BookOkAsync(room.Id, At(9), At(10));
        await BookOkAsync(room.Id, At(10), At(11));
        await BookOkAsync(room.Id, At(8), At(9));
        await BookOkAsync(other.Id, At(9), At(10));
    }

    [Fact]
    public async Task Simultaneous_requests_for_one_slot_produce_one_booking()
    {
        var room = await CreateRoomAsync();
        var attempts = Enumerable.Range(0, 20).Select(_ => BookAsync(room.Id, At(13), At(14)));
        var statuses = (await Task.WhenAll(attempts)).Select(r => r.StatusCode).ToList();

        Assert.Single(statuses, HttpStatusCode.Created);
        Assert.Equal(19, statuses.Count(s => s == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Rescheduling_needs_the_current_etag()
    {
        var room = await CreateRoomAsync();
        var booking = await BookOkAsync(room.Id, At(9), At(10));
        var body = new Reschedule(At(15), At(16));

        var missing = await Client.PatchAsJsonAsync($"/bookings/{booking.Id}", body, Ct);
        await ProblemAsync(missing, (HttpStatusCode)428);

        var stale = await PatchAsync(booking.Id, body, "\"7\"");
        await ProblemAsync(stale, HttpStatusCode.PreconditionFailed);

        var ok = await PatchAsync(booking.Id, body, "\"1\"");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("\"2\"", ok.Headers.ETag?.Tag);
        var moved = await ok.Content.ReadFromJsonAsync<BookingResponse>(Ct);
        Assert.Equal((At(15), At(16)), (moved!.Start, moved.End));

        var reused = await PatchAsync(booking.Id, new Reschedule(At(17), At(18)), "\"1\"");
        await ProblemAsync(reused, HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Rescheduling_checks_for_clashes_but_not_with_itself()
    {
        var room = await CreateRoomAsync();
        var first = await BookOkAsync(room.Id, At(9), At(11));
        await BookOkAsync(room.Id, At(12), At(13));

        var extend = await PatchAsync(first.Id, new Reschedule(At(9), At(12)), "\"1\"");
        Assert.Equal(HttpStatusCode.OK, extend.StatusCode);

        var clash = await PatchAsync(first.Id, new Reschedule(At(11), At(13)), "\"2\"");
        await ProblemAsync(clash, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Rescheduling_validates_times()
    {
        var room = await CreateRoomAsync();
        var booking = await BookOkAsync(room.Id, At(9), At(10));

        var backwards = await PatchAsync(booking.Id, new Reschedule(At(10), At(9)), "\"1\"");
        Assert.NotEmpty(Errors(await ProblemAsync(backwards, HttpStatusCode.BadRequest), "End"));

        Factory.Clock.Advance(TimeSpan.FromDays(2));
        var past = await PatchAsync(booking.Id, new Reschedule(At(15), At(16)), "\"1\"");
        Assert.NotEmpty(Errors(await ProblemAsync(past, HttpStatusCode.BadRequest), "Start"));
    }

    [Fact]
    public async Task Cancelling_frees_the_slot()
    {
        var room = await CreateRoomAsync();
        var booking = await BookOkAsync(room.Id, At(9), At(10));

        Assert.Equal((HttpStatusCode)428, (await Client.DeleteAsync($"/bookings/{booking.Id}", Ct)).StatusCode);

        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/bookings/{booking.Id}");
        delete.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        Assert.Equal(HttpStatusCode.NoContent, (await Client.SendAsync(delete, Ct)).StatusCode);

        await ProblemAsync(await Client.GetAsync($"/bookings/{booking.Id}", Ct), HttpStatusCode.NotFound);
        await BookOkAsync(room.Id, At(9), At(10));
    }

    [Fact]
    public async Task Unknown_bookings_are_not_found()
    {
        await ProblemAsync(await Client.GetAsync("/bookings/5", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await PatchAsync(5, new Reschedule(At(9), At(10)), "\"1\""), HttpStatusCode.NotFound);
        await ProblemAsync(await Client.DeleteAsync("/bookings/5", Ct), HttpStatusCode.NotFound);
    }

    private Task<HttpResponseMessage> PatchAsync(int id, Reschedule body, string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/bookings/{id}") { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return Client.SendAsync(request, Ct);
    }
}
