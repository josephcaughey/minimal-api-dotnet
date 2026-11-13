using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bookings.Api.Data;

public sealed class Room
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Capacity { get; set; }
}

public sealed class Booking
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public Room? Room { get; set; }

    // Stored as UTC DateTime because the SQLite provider can't compare or
    // order DateTimeOffset values in SQL.
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    public required string BookedBy { get; set; }
    public int Attendees { get; set; }

    // Bumped on every change and sent to clients as the ETag.
    public int Version { get; set; }
}

public sealed class BookingsDb(DbContextOptions<BookingsDb> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite hands DateTime back with an unspecified kind.
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        modelBuilder.Entity<Room>(room =>
        {
            room.Property(r => r.Name).HasMaxLength(100);
            room.HasIndex(r => r.Name).IsUnique();
        });

        modelBuilder.Entity<Booking>(booking =>
        {
            booking.Property(b => b.Start).HasConversion(utc);
            booking.Property(b => b.End).HasConversion(utc);
            booking.Property(b => b.BookedBy).HasMaxLength(200);
            booking.Property(b => b.Version).IsConcurrencyToken();
            booking.HasIndex(b => new { b.RoomId, b.Start });
        });
    }
}
