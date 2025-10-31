using Alification.Data;
using Alification.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Alification.Controllers;

[ApiController]
[Route("rooms")]
public class RoomsController:ControllerBase
{
    private readonly AppDbContext _db;

    public RoomsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var telegramIdStr = User?.Claims?.FirstOrDefault(c => c.Type == "telegram_user_id")?.Value;
        if (!long.TryParse(telegramIdStr, out var telegramId)) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null) return Forbid();

        var rooms = await _db.Rooms.Where(r => r.CompanyId == user.CompanyId).ToListAsync();
        return Ok(rooms.Select(r => new { r.Id, r.Name, r.Capacity, r.Description }));
    }

    [HttpGet("available-now")]
    public async Task<IActionResult> AvailableNow()
    {
        var telegramIdStr = User?.Claims?.FirstOrDefault(c => c.Type == "telegram_user_id")?.Value;
        if (!long.TryParse(telegramIdStr, out var telegramId)) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null) return Forbid();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;
        var currentSlot = (now.Hour * 4) + (now.Minute / 15);

        var rooms = await _db.Rooms
            .Where(r => r.CompanyId == user.CompanyId)
            .ToListAsync();

        var available = new List<object>();
        foreach (var room in rooms)
        {
            var bookings = await _db.Bookings
                .Where(b => b.RoomId == room.Id && b.Date == today && b.TimespanId >= currentSlot)
                .Select(b => b.TimespanId)
                .ToListAsync();

            var freeSlots = new List<int>();
            for (int i = currentSlot; i < 96; i++)
            {
                if (!bookings.Contains(i))
                    freeSlots.Add(i);
            }

            if (freeSlots.Any())
            {
                available.Add(new
                {
                    roomId = room.Id,
                    roomName = room.Name,
                    capacity = room.Capacity,
                    freeSlots = freeSlots,
                    nextFreeSlot = freeSlots.Min()
                });
            }
        }

        return Ok(available);
    }

    public record CreateRoomRequest(string Name, int Capacity, string? Description);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoomRequest req)
    {
        var telegramIdStr = User?.Claims?.FirstOrDefault(c => c.Type == "telegram_user_id")?.Value;
        if (!long.TryParse(telegramIdStr, out var telegramId)) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null) return Forbid();
        if (user.Role != Role.Admin) return Forbid();

        var room = new Room
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Capacity = req.Capacity,
            Description = req.Description ?? string.Empty,
            CompanyId = user.CompanyId
        };
        _db.Rooms.Add(room);
        await _db.SaveChangesAsync();
        return Ok(new { room.Id });
    }

    [HttpGet("{id:guid}/availability")]
    public async Task<IActionResult> Availability([FromRoute] Guid id, [FromQuery] DateOnly date)
    {
        var telegramIdStr = User?.Claims?.FirstOrDefault(c => c.Type == "telegram_user_id")?.Value;
        if (!long.TryParse(telegramIdStr, out var telegramId)) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null) return Forbid();

        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == user.CompanyId);
        if (room == null) return NotFound();

        var slots = new bool[96];
        var bookings = await _db.Bookings
            .Include(b => b.User)
            .Where(b => b.RoomId == id && b.Date == date)
            .ToListAsync();

        var busyInfo = new List<object>();
        foreach (var b in bookings)
        {
            var idx = b.TimespanId;
            if (idx >= 0 && idx < 96)
            {
                slots[idx] = true;
                busyInfo.Add(new { slot = idx, userId = b.UserId, userName = b.User?.Name });
            }
        }

        return Ok(new
        {
            date = date.ToString("yyyy-MM-dd"),
            busy = slots,
            busySlots = busyInfo
        });
    }
}


