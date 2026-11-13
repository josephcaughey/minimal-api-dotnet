using System.ComponentModel.DataAnnotations;
using Bookings.Api.Data;

namespace Bookings.Api;

public sealed record CreateRoom(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Range(1, 500)] int Capacity);

public sealed record RoomResponse(int Id, string Name, int Capacity)
{
    public static RoomResponse From(Room room) => new(room.Id, room.Name, room.Capacity);
}

public sealed record CreateBooking(
    [Range(1, int.MaxValue)] int RoomId,
    DateTimeOffset Start,
    DateTimeOffset End,
    [Required, StringLength(200, MinimumLength = 1)] string BookedBy,
    [Range(1, 500)] int Attendees) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BookingRules.CheckTimes(Start, End);
}

public sealed record Reschedule(DateTimeOffset Start, DateTimeOffset End) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BookingRules.CheckTimes(Start, End);
}

public sealed record BookingResponse(
    int Id,
    int RoomId,
    DateTimeOffset Start,
    DateTimeOffset End,
    string BookedBy,
    int Attendees)
{
    public static BookingResponse From(Booking booking) => new(
        booking.Id,
        booking.RoomId,
        new DateTimeOffset(booking.Start),
        new DateTimeOffset(booking.End),
        booking.BookedBy,
        booking.Attendees);
}

public static class BookingRules
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(8);
    public static readonly TimeSpan MaxListWindow = TimeSpan.FromDays(31);

    public static IEnumerable<ValidationResult> CheckTimes(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            yield return new ValidationResult("End must be after start.", [nameof(CreateBooking.End)]);
        }
        else if (end - start > MaxDuration)
        {
            yield return new ValidationResult(
                $"A booking can last at most {MaxDuration.TotalHours} hours.",
                [nameof(CreateBooking.End)]);
        }
    }
}
