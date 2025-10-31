using System.Security.Cryptography;
using System.Text;
using Alification.Data;
using Alification.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Alification.Controllers;

[ApiController]
[Route("admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminController(AppDbContext db)
    {
        _db = db;
    }

    private async Task<User?> GetCurrentAdminUser()
    {
        var telegramIdStr = User?.Claims?.FirstOrDefault(c => c.Type == "telegram_user_id")?.Value;
        if (!long.TryParse(telegramIdStr, out var telegramId)) return null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null || user.Role != Role.Admin) return null;
        return user;
    }

    [HttpDelete("rooms/{id:guid}")]
    public async Task<IActionResult> DeleteRoom([FromRoute] Guid id)
    {
        var user = await GetCurrentAdminUser();
        if (user == null) return Forbid();

        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == user.CompanyId);
        if (room == null) return NotFound();

        _db.Rooms.Remove(room);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser([FromRoute] Guid id)
    {
        var user = await GetCurrentAdminUser();
        if (user == null) return Forbid();

        var targetUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == user.CompanyId);
        if (targetUser == null) return NotFound();
        if (targetUser.Role == Role.Admin && targetUser.Id != user.Id) 
            return BadRequest(new { error = "Cannot delete another admin" });

        _db.Users.Remove(targetUser);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("bookings/{id:guid}")]
    public async Task<IActionResult> DeleteBooking([FromRoute] Guid id)
    {
        var user = await GetCurrentAdminUser();
        if (user == null) return Forbid();

        var booking = await _db.Bookings
            .Include(b => b.Room)
            .FirstOrDefaultAsync(b => b.Id == id && b.Room.CompanyId == user.CompanyId);
        if (booking == null) return NotFound();

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

    public record ChangePasswordRequest(string NewPassword);
    [HttpPut("company/password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var user = await GetCurrentAdminUser();
        if (user == null) return Forbid();

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == user.CompanyId);
        if (company == null) return NotFound();

        company.PasswordHash = Hash(req.NewPassword);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    public record SetWorkingHoursRequest(TimeOnly StartTime, int Hours);
    [HttpPut("company/working-hours")]
    public async Task<IActionResult> SetWorkingHours([FromBody] SetWorkingHoursRequest req)
    {
        var user = await GetCurrentAdminUser();
        if (user == null) return Forbid();

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == user.CompanyId);
        if (company == null) return NotFound();

        company.WorkingStart = DateTime.UtcNow.Date + req.StartTime.ToTimeSpan();
        company.WorkingHours = req.Hours;
        await _db.SaveChangesAsync();
        return Ok(new { workingStart = company.WorkingStart, workingHours = company.WorkingHours });
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        var user = await GetCurrentAdminUser();
        if (user == null) return Forbid();

        var users = await _db.Users
            .Where(u => u.CompanyId == user.CompanyId)
            .Select(u => new { u.Id, u.Name, u.TelegramId, role = u.Role.ToString() })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("bookings")]
    public async Task<IActionResult> ListAllBookings()
    {
        var user = await GetCurrentAdminUser();
        if (user == null) return Forbid();

        var bookings = await _db.Bookings
            .Include(b => b.User)
            .Include(b => b.Room)
            .Where(b => b.Room.CompanyId == user.CompanyId)
            .OrderByDescending(b => b.Date).ThenBy(b => b.TimespanId)
            .ToListAsync();

        var grouped = bookings
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
                startSlot = g.Min(s => s.TimespanId),
                endSlot = g.Max(s => s.TimespanId),
                slots = g.Count()
            })
            .ToList();

        return Ok(grouped);
    }

    private static string Hash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}

