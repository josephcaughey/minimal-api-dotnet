using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Bookings.Api.Bookings;

public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bookings").WithTags("Bookings");
        group.MapPost("/", Create)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/{id:int}", Get);
        group.MapPatch("/{id:int}", Reschedule)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        group.MapDelete("/{id:int}", Cancel)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        return app;
    }

    private static async Task<Results<Created<BookingResponse>, ValidationProblem, ProblemHttpResult>> Create(
        CreateBooking request, BookingService service, HttpResponse response, CancellationToken ct)
    {
        var outcome = await service.CreateAsync(request, ct);
        if (outcome.Booking is { } booking)
        {
            response.Headers.ETag = ETag.For(booking);
            return TypedResults.Created($"/bookings/{booking.Id}", BookingResponse.From(booking));
        }
        return outcome.Error switch
        {
            BookingError.Overlaps => TypedResults.Problem(OverlapProblem(outcome)),
            _ => Invalid(outcome, request.RoomId),
        };
    }

    private static async Task<Results<Ok<BookingResponse>, NotFound>> Get(
        int id, Data.BookingsDb db, HttpResponse response, CancellationToken ct)
    {
        var booking = await db.Bookings.FindAsync([id], ct);
        if (booking is null)
        {
            return TypedResults.NotFound();
        }
        response.Headers.ETag = ETag.For(booking);
        return TypedResults.Ok(BookingResponse.From(booking));
    }

    private static async Task<Results<Ok<BookingResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Reschedule(
        int id, Reschedule request, BookingService service, HttpRequest httpRequest, HttpResponse response, CancellationToken ct)
    {
        var outcome = await service.RescheduleAsync(id, httpRequest.Headers.IfMatch, request, ct);
        if (outcome.Booking is { } booking)
        {
            response.Headers.ETag = ETag.For(booking);
            return TypedResults.Ok(BookingResponse.From(booking));
        }
        return outcome.Error switch
        {
            BookingError.NotFound => TypedResults.NotFound(),
            BookingError.Overlaps => TypedResults.Problem(OverlapProblem(outcome)),
            BookingError.PreconditionRequired or BookingError.VersionMismatch => PreconditionProblem(outcome.Error),
            _ => Invalid(outcome, roomId: null),
        };
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Cancel(
        int id, BookingService service, HttpRequest httpRequest, CancellationToken ct)
    {
        var outcome = await service.CancelAsync(id, httpRequest.Headers.IfMatch, ct);
        return outcome.Error switch
        {
            BookingError.None => TypedResults.NoContent(),
            BookingError.NotFound => TypedResults.NotFound(),
            _ => PreconditionProblem(outcome.Error),
        };
    }

    private static ValidationProblem Invalid(BookingOutcome outcome, int? roomId)
    {
        var (field, message) = outcome.Error switch
        {
            BookingError.RoomNotFound => (nameof(CreateBooking.RoomId), $"Room {roomId} does not exist."),
            BookingError.OverCapacity => (nameof(CreateBooking.Attendees), $"The room holds {outcome.Capacity} people."),
            BookingError.StartsInPast => (nameof(CreateBooking.Start), "Start must not be in the past."),
            _ => throw new InvalidOperationException($"{outcome.Error} is not a validation error."),
        };
        return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    }

    private static ProblemDetails OverlapProblem(BookingOutcome outcome) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "The room is already booked for part of that time.",
        Extensions = { ["conflictingBookingId"] = outcome.ClashId },
    };

    private static ProblemHttpResult PreconditionProblem(BookingError error) => error == BookingError.PreconditionRequired
        ? TypedResults.Problem(
            statusCode: StatusCodes.Status428PreconditionRequired,
            title: $"Send the booking's ETag in an {HeaderNames.IfMatch} header.")
        : TypedResults.Problem(
            statusCode: StatusCodes.Status412PreconditionFailed,
            title: "The booking has changed since you fetched it. Fetch it again and retry.");
}
