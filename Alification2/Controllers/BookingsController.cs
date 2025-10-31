using Alification.Data;
using Alification.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Alification.Controllers;

[ApiController]
[Route("bookings")]
public class BookingsController:ControllerBase
{
    private readonly AppDbContext _db;

    public BookingsController(AppDbContext db)
    {
        _db = db;
    }

    public record CreateBookingRequest(Guid RoomId, DateOnly Date, int StartSlot, int EndSlotInclusive);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookingRequest req)
    {
        var telegramIdStr = User?.Claims?.FirstOrDefault(c => c.Type == "telegram_user_id")?.Value;
        if (!long.TryParse(telegramIdStr, out var telegramId)) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null) return Forbid();

        if (req.StartSlot < 0 || req.EndSlotInclusive > 95 || req.StartSlot > req.EndSlotInclusive)
            return BadRequest(new { error = "Invalid slot range" });

        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == req.RoomId && r.CompanyId == user.CompanyId);
        if (room == null) return NotFound(new { error = "Room not found" });

        var existing = await _db.Bookings.Where(b => b.RoomId == room.Id && b.Date == req.Date).ToListAsync();
        var occupied = new bool[96];
        foreach (var b in existing)
        {
            if (b.TimespanId >= 0 && b.TimespanId < 96) occupied[b.TimespanId] = true;
        }
        for (int i = req.StartSlot; i <= req.EndSlotInclusive; i++)
        {
            if (occupied[i])
                return Conflict(new { error = "Slot already booked", slot = i });
        }

        var groupId = Guid.NewGuid();
        var toInsert = new List<Booking>();
        for (int i = req.StartSlot; i <= req.EndSlotInclusive; i++)
        {
            toInsert.Add(new Booking
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                RoomId = room.Id,
                Date = req.Date,
                TimespanId = i,
                BookingGroupId = groupId
            });
        }

        _db.Bookings.AddRange(toInsert);
        await _db.SaveChangesAsync();
        return Ok(new { created = toInsert.Count, bookingGroupId = groupId });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        var telegramIdStr = User?.Claims?.FirstOrDefault(c => c.Type == "telegram_user_id")?.Value;
        if (!long.TryParse(telegramIdStr, out var telegramId)) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null) return Forbid();

        var booking = await _db.Bookings.Include(b => b.User).FirstOrDefaultAsync(b => b.Id == id);
        if (booking == null) return NotFound();
        if (booking.UserId != user.Id && user.Role != Role.Admin) return Forbid();

        // Delete all bookings in the same group (all slots of the same booking)
        if (booking.BookingGroupId.HasValue)
        {
            var bookingsToDelete = await _db.Bookings
                .Where(b => b.BookingGroupId == booking.BookingGroupId)
                .ToListAsync();
            _db.Bookings.RemoveRange(bookingsToDelete);
        }
        else
        {
            _db.Bookings.Remove(booking);
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<IActionResult> MyBookings()
    {
        var telegramIdStr = User?.Claims?.FirstOrDefault(c => c.Type == "telegram_user_id")?.Value;
        if (!long.TryParse(telegramIdStr, out var telegramId)) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null) return Forbid();

        var list = await _db.Bookings
            .Include(b => b.Room)
            .Where(b => b.UserId == user.Id)
            .OrderByDescending(b => b.Date).ThenBy(b => b.TimespanId)
            .ToListAsync();

        // Group bookings by BookingGroupId and date for cleaner response
        var grouped = list
            .GroupBy(b => new { 
                GroupId = b.BookingGroupId ?? b.Id, 
                b.Date, 
                b.RoomId, 
                RoomName = b.Room.Name,
                b.UserId,
                UserName = b.User.Name
            })
            .Select(g => new
            {
                bookingGroupId = g.Key.GroupId,
                roomId = g.Key.RoomId,
                roomName = g.Key.RoomName,
                userId = g.Key.UserId,
                userName = g.Key.UserName,
                date = g.Key.Date.ToString("yyyy-MM-dd"),
                slots = g.OrderBy(s => s.TimespanId).Select(s => s.TimespanId).ToList(),
                startSlot = g.Min(s => s.TimespanId),
                endSlot = g.Max(s => s.TimespanId)
            })
            .ToList();

        return Ok(grouped);
    }
}


