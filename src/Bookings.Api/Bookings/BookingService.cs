using Bookings.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bookings.Api.Bookings;

/// <summary>
/// Serialises the check-then-insert for bookings within this process, so two
/// requests for the same slot can't both see it free. Running several
/// instances needs the rule in the database instead, such as a PostgreSQL
/// exclusion constraint on the room and time range.
/// </summary>
public sealed class BookingGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}

public enum BookingError
{
    None,
    NotFound,
    RoomNotFound,
    StartsInPast,
    OverCapacity,
    Overlaps,
    PreconditionRequired,
    VersionMismatch,
}

public sealed record BookingOutcome(BookingError Error, Booking? Booking = null, int? ClashId = null, int? Capacity = null)
{
    public static BookingOutcome Ok(Booking booking) => new(BookingError.None, booking);
    public static BookingOutcome Fail(BookingError error) => new(error);
}

public sealed class BookingService(BookingsDb db, TimeProvider clock, BookingGate gate)
{
    public async Task<BookingOutcome> CreateAsync(CreateBooking request, CancellationToken ct)
    {
        var room = await db.Rooms.FindAsync([request.RoomId], ct);
        if (room is null)
        {
            return BookingOutcome.Fail(BookingError.RoomNotFound);
        }
        if (request.Attendees > room.Capacity)
        {
            return new BookingOutcome(BookingError.OverCapacity, Capacity: room.Capacity);
        }
        if (request.Start < clock.GetUtcNow())
        {
            return BookingOutcome.Fail(BookingError.StartsInPast);
        }

        var start = request.Start.UtcDateTime;
        var end = request.End.UtcDateTime;

        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var clash = await FindClashAsync(room.Id, start, end, exclude: null, ct);
            if (clash is not null)
            {
                return new BookingOutcome(BookingError.Overlaps, ClashId: clash);
            }

            var booking = new Booking
            {
                RoomId = room.Id,
                Start = start,
                End = end,
                BookedBy = request.BookedBy,
                Attendees = request.Attendees,
                Version = 1,
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync(ct);
            return BookingOutcome.Ok(booking);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    public async Task<BookingOutcome> RescheduleAsync(int id, string? ifMatch, Reschedule request, CancellationToken ct)
    {
        var start = request.Start.UtcDateTime;
        var end = request.End.UtcDateTime;

        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var (booking, error) = await LoadForChangeAsync(id, ifMatch, ct);
            if (booking is null)
            {
                return BookingOutcome.Fail(error);
            }
            if (request.Start < clock.GetUtcNow())
            {
                return BookingOutcome.Fail(BookingError.StartsInPast);
            }

            var clash = await FindClashAsync(booking.RoomId, start, end, exclude: booking.Id, ct);
            if (clash is not null)
            {
                return new BookingOutcome(BookingError.Overlaps, ClashId: clash);
            }

            booking.Start = start;
            booking.End = end;
            booking.Version++;
            await db.SaveChangesAsync(ct);
            return BookingOutcome.Ok(booking);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BookingOutcome.Fail(BookingError.VersionMismatch);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    public async Task<BookingOutcome> CancelAsync(int id, string? ifMatch, CancellationToken ct)
    {
        var (booking, error) = await LoadForChangeAsync(id, ifMatch, ct);
        if (booking is null)
        {
            return BookingOutcome.Fail(error);
        }
        try
        {
            db.Bookings.Remove(booking);
            await db.SaveChangesAsync(ct);
            return BookingOutcome.Ok(booking);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BookingOutcome.Fail(BookingError.VersionMismatch);
        }
    }

    private async Task<(Booking? Booking, BookingError Error)> LoadForChangeAsync(int id, string? ifMatch, CancellationToken ct)
    {
        var booking = await db.Bookings.FindAsync([id], ct);
        if (booking is null)
        {
            return (null, BookingError.NotFound);
        }
        if (string.IsNullOrEmpty(ifMatch))
        {
            return (null, BookingError.PreconditionRequired);
        }
        if (ifMatch != ETag.For(booking))
        {
            return (null, BookingError.VersionMismatch);
        }
        return (booking, BookingError.None);
    }

    // Two ranges overlap when each starts before the other ends, so a booking
    // ending at 10:00 doesn't clash with one starting at 10:00.
    private Task<int?> FindClashAsync(int roomId, DateTime start, DateTime end, int? exclude, CancellationToken ct) =>
        db.Bookings
            .Where(b => b.RoomId == roomId && b.Id != exclude && b.Start < end && start < b.End)
            .OrderBy(b => b.Start)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync(ct);
}

public static class ETag
{
    public static string For(Booking booking) => $"\"{booking.Version}\"";
}
