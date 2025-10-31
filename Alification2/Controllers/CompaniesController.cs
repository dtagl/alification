using System.Security.Cryptography;
using System.Text;
using Alification.Data;
using Alification.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Alification.Controllers;

[ApiController]
[Route("companies")]
public class CompaniesController:ControllerBase
{
    private readonly AppDbContext _db;

    public CompaniesController(AppDbContext db)
    {
        _db = db;
    }

    public record CreateCompanyRequest(string Name, string Password, string? UserName);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCompanyRequest req)
    {
        var telegramIdStr = User?.Claims?.FirstOrDefault(c => c.Type == "telegram_user_id")?.Value;
        if (!long.TryParse(telegramIdStr, out var telegramId)) return Unauthorized();

        if (await _db.Companies.AnyAsync(c => c.Name == req.Name))
        {
            return Conflict(new { error = "Company name already exists" });
        }

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            PasswordHash = Hash(req.Password),
            WorkingStart = DateTime.UtcNow.Date,
            WorkingHours = 8
        };
        _db.Companies.Add(company);

        var user = new User
        {
            Id = Guid.NewGuid(),
            TelegramId = telegramId,
            Name = string.IsNullOrWhiteSpace(req.UserName) ? $"tg_{telegramId}" : req.UserName,
            CompanyId = company.Id,
            Role = Role.Admin
        };
        _db.Users.Add(user);

        await _db.SaveChangesAsync();

        return Ok(new { companyId = company.Id, userId = user.Id });
    }

    public record JoinCompanyRequest(Guid CompanyId, string Password, string? UserName);

    [HttpPost("join")]
    public async Task<IActionResult> Join([FromBody] JoinCompanyRequest req)
    {
        var telegramIdStr = User?.Claims?.FirstOrDefault(c => c.Type == "telegram_user_id")?.Value;
        if (!long.TryParse(telegramIdStr, out var telegramId)) return Unauthorized();

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == req.CompanyId);
        if (company == null) return NotFound(new { error = "Company not found" });
        if (company.PasswordHash != Hash(req.Password)) return Unauthorized(new { error = "Invalid password" });

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId);
        if (existing != null) return Conflict(new { error = "User already exists" });

        var user = new User
        {
            Id = Guid.NewGuid(),
            TelegramId = telegramId,
            Name = string.IsNullOrWhiteSpace(req.UserName) ? $"tg_{telegramId}" : req.UserName,
            CompanyId = company.Id,
            Role = Role.User
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new { userId = user.Id, companyId = company.Id });
    }

    private static string Hash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}


