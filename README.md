# minimal-api-dotnet

[![CI](https://github.com/josephcaughey/minimal-api-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/josephcaughey/minimal-api-dotnet/actions/workflows/ci.yml)

A meeting-room booking API built with ASP.NET Core minimal APIs on .NET 10.
It's small, but it handles the parts that usually get skipped:

- **No double bookings.** A room can't be booked twice for overlapping times,
  even when requests for the same slot arrive at once. Back-to-back bookings
  (one ends at 10:00, the next starts at 10:00) are fine.
- **Optimistic concurrency.** Every booking has an ETag. Changing or
  cancelling one requires `If-Match`; a stale ETag gets `412`, a missing one
  `428`.
- **Problem Details everywhere.** Validation errors, conflicts and 404s all
  come back as `application/problem+json`.
- **Testable time.** "Now" comes from an injected `TimeProvider`, so tests run
  against a fixed clock.
- **Integration tests** through `WebApplicationFactory` against a private
  in-memory SQLite database per test.
- **A chiseled Docker image**: no shell, non-root user.

## Run it

```bash
dotnet run --project src/Bookings.Api
```

or with Docker:

```bash
docker build -t bookings-api .
docker run -p 8080:8080 -v bookings-data:/data bookings-api
```

The OpenAPI document is at `/openapi/v1.json` and a health check at
`/health`. Data is kept in SQLite (`bookings.db`, or `/data` in the
container).

## Endpoints

| method | path | |
| --- | --- | --- |
| `GET` | `/rooms` | all rooms, by name |
| `POST` | `/rooms` | create a room: `name`, `capacity` |
| `GET` | `/rooms/{id}/bookings?from=&to=` | bookings overlapping a window of up to 31 days |
| `POST` | `/bookings` | book: `roomId`, `start`, `end`, `bookedBy`, `attendees` |
| `GET` | `/bookings/{id}` | one booking, with its `ETag` |
| `PATCH` | `/bookings/{id}` | move it: `start`, `end`, with `If-Match` |
| `DELETE` | `/bookings/{id}` | cancel it, with `If-Match` |

A booking lasts at most 8 hours, can't start in the past and can't have more
attendees than the room holds. Times can be sent in any offset and are
returned in UTC.

## Walkthrough

```
$ curl -i -X POST localhost:5142/rooms -H 'Content-Type: application/json' \
    -d '{"name":"Arbutus","capacity":6}'
HTTP/1.1 201 Created
Location: /rooms/1
{"id":1,"name":"Arbutus","capacity":6}

$ curl -i -X POST localhost:5142/bookings -H 'Content-Type: application/json' \
    -d '{"roomId":1,"start":"2030-03-02T09:00:00-08:00","end":"2030-03-02T10:00:00-08:00","bookedBy":"Sam Lee","attendees":4}'
HTTP/1.1 201 Created
ETag: "1"
{"id":1,"roomId":1,"start":"2030-03-02T17:00:00+00:00","end":"2030-03-02T18:00:00+00:00","bookedBy":"Sam Lee","attendees":4}
```

A second booking that overlaps is refused and points at the one in the way:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "The room is already booked for part of that time.",
  "status": 409,
  "conflictingBookingId": 1
}
```

Moving a booking needs the ETag from when it was read:

```
$ curl -X PATCH localhost:5142/bookings/1 -H 'Content-Type: application/json' \
    -d '{"start":"2030-03-02T13:00:00-08:00","end":"2030-03-02T14:00:00-08:00"}'
{"title":"Send the booking's ETag in an If-Match header.","status":428}

$ curl -i -X PATCH localhost:5142/bookings/1 -H 'Content-Type: application/json' -H 'If-Match: "1"' \
    -d '{"start":"2030-03-02T13:00:00-08:00","end":"2030-03-02T14:00:00-08:00"}'
HTTP/1.1 200 OK
ETag: "2"
```

Validation errors are keyed by the request property they refer to, using the
C# property name (`BookedBy`, `RoomId`), which is what the framework's own
validation produces.

## Design notes

**Overlap and concurrency.** Two ranges overlap when each starts before the
other ends. The check and the insert run under a process-wide lock, so twenty
simultaneous requests for one slot produce exactly one booking (there's a
test for that). The lock only covers one instance. Running several would
need the rule in the database instead, such as a PostgreSQL exclusion
constraint over the room and a `tstzrange`.

**Versions.** Each booking carries an integer `Version` that EF Core uses as
a concurrency token and the API exposes as the ETag. A write with a matching
`If-Match` still fails with `412` if another write landed in between.

**Dates in SQLite.** The SQLite provider can't compare `DateTimeOffset` in
SQL, so times are stored as UTC `DateTime` and converted at the edge.

**Schema.** The database is created on start with `EnsureCreated`. A schema
with real history would move to EF Core migrations.

## Development

```bash
dotnet test                            # 28 integration tests
dotnet format --verify-no-changes
```

Needs the .NET 10 SDK. Warnings are errors, and package versions are managed
centrally in `Directory.Packages.props`.

## License

MIT
