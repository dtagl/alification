using Alification.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Alification.Data;

public class AppDbContext:DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options):base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Company> Companies { get; set; }
    public DbSet<Booking> Bookings { get; set; }
    public DbSet<Room> Rooms { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.TelegramId).IsUnique();
            entity.HasIndex(u => u.CompanyId);
            entity.Property(u => u.Name).IsRequired().HasMaxLength(200);
        });

        // Company configuration
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasIndex(c => c.Name).IsUnique();
            entity.Property(c => c.Name).IsRequired().HasMaxLength(200);
            entity.Property(c => c.PasswordHash).IsRequired().HasMaxLength(64);
        });

        // Room configuration
        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasIndex(r => r.CompanyId);
            entity.Property(r => r.Name).IsRequired().HasMaxLength(200);
            entity.Property(r => r.Description).HasMaxLength(1000);
        });

        // Booking configuration
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasIndex(b => b.UserId);
            entity.HasIndex(b => b.RoomId);
            entity.HasIndex(b => new { b.RoomId, b.Date, b.TimespanId }).IsUnique();
            entity.Property(b => b.Date).HasColumnType("date");
        });

        // Relationships
        modelBuilder.Entity<User>()
            .HasOne(u => u.Company)
            .WithMany(c => c.Users)
            .HasForeignKey(u => u.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Room>()
            .HasOne(r => r.Company)
            .WithMany(c => c.Rooms)
            .HasForeignKey(r => r.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.User)
            .WithMany(u => u.Bookings)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Room)
            .WithMany(r => r.Bookings)
            .HasForeignKey(b => b.RoomId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}