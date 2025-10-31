using Alification.Data;
using Alification.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Alification.Controllers;

[ApiController]
[Route("me")]
public class MeController:ControllerBase
{
    private readonly AppDbContext _db;

    public MeController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var telegramIdStr = User?.Claims?.FirstOrDefault(c => c.Type == "telegram_user_id")?.Value;
        if (!long.TryParse(telegramIdStr, out var telegramId))
        {
            return Unauthorized(new { error = "Missing telegram user id. Provide X-Telegram-User-Id header in development." });
        }

        var user = await _db.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (user == null)
        {
            return Ok(new { hasAccount = false });
        }

        return Ok(new
        {
            hasAccount = true,
            user = new { user.Id, user.Name, role = user.Role.ToString() },
            company = user.Company == null ? null : new { user.Company.Id, user.Company.Name }
        });
    }
}


