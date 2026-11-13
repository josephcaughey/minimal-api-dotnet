using Bookings.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bookings.Api.Rooms;

public static class RoomEndpoints
{
    public static IEndpointRouteBuilder MapRooms(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rooms").WithTags("Rooms");
        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapGet("/{id:int}/bookings", Bookings);
        return app;
    }

    private static async Task<Ok<List<RoomResponse>>> List(BookingsDb db, CancellationToken ct)
    {
        var rooms = await db.Rooms.OrderBy(r => r.Name).ToListAsync(ct);
        return TypedResults.Ok(rooms.ConvertAll(RoomResponse.From));
    }

    private static async Task<Results<Created<RoomResponse>, ValidationProblem>> Create(
        CreateRoom request, BookingsDb db, CancellationToken ct)
    {
        if (await db.Rooms.AnyAsync(r => r.Name == request.Name, ct))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateRoom.Name)] = [$"A room called {request.Name} already exists."],
            });
        }
        var room = new Room { Name = request.Name, Capacity = request.Capacity };
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/rooms/{room.Id}", RoomResponse.From(room));
    }

    /// <summary>Bookings for the room that overlap [from, to), earliest first.</summary>
    private static async Task<Results<Ok<List<BookingResponse>>, NotFound, ValidationProblem>> Bookings(
        int id, DateTimeOffset from, DateTimeOffset to, BookingsDb db, CancellationToken ct)
    {
        if (to <= from || to - from > BookingRules.MaxListWindow)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = [$"to must be after from and at most {BookingRules.MaxListWindow.TotalDays} days later."],
            });
        }
        if (!await db.Rooms.AnyAsync(r => r.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        var (start, end) = (from.UtcDateTime, to.UtcDateTime);
        var bookings = await db.Bookings
            .Where(b => b.RoomId == id && b.Start < end && start < b.End)
            .OrderBy(b => b.Start)
            .ToListAsync(ct);
        return TypedResults.Ok(bookings.ConvertAll(BookingResponse.From));
    }
}
