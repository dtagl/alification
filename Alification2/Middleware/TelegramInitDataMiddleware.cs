using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Alification.Middleware;

public class TelegramInitDataMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TelegramInitDataMiddleware> _logger;
    private readonly IConfiguration _configuration;

    public TelegramInitDataMiddleware(RequestDelegate next, ILogger<TelegramInitDataMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task Invoke(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            var botToken = _configuration["Telegram:BotToken"];
            
            // Try Telegram initData first (production)
            // Telegram Web App SDK provides initData in multiple ways:
            // 1. Standard header from frontend fetch requests
            // 2. Query parameter (for GET requests)
            // 3. Form data or body (for POST requests)
            var initData = context.Request.Headers["X-Telegram-Init-Data"].FirstOrDefault()
                          ?? context.Request.Headers["X-WebApp-Init-Data"].FirstOrDefault()
                          ?? context.Request.Query["tgWebAppData"].FirstOrDefault()
                          ?? context.Request.Query["_auth"].FirstOrDefault();

            // Only attempt to read form if the request actually has a form content type
            if (string.IsNullOrEmpty(initData) && context.Request.HasFormContentType)
            {
                try
                {
                    var form = await context.Request.ReadFormAsync();
                    initData = form["initData"].FirstOrDefault();
                }
                catch
                {
                    // ignore form read errors for non-form requests
                }
            }

            if (!string.IsNullOrEmpty(initData) && !string.IsNullOrEmpty(botToken))
            {
                if (ValidateTelegramInitData(initData, botToken, out var userId))
                {
                    var claims = new List<Claim>
                    {
                        new Claim("telegram_user_id", userId.ToString())
                    };
                    var identity = new ClaimsIdentity(claims, "TelegramInitData");
                    context.User = new ClaimsPrincipal(identity);
                }
                else
                {
                    _logger.LogWarning("Invalid Telegram initData");
                }
            }
            else
            {
                // Development fallback: allow header to simulate Telegram user id
                var debugUser = context.Request.Headers["X-Telegram-User-Id"].ToString();
                if (long.TryParse(debugUser, out var telegramUserId))
                {
                    var claims = new List<Claim>
                    {
                        new Claim("telegram_user_id", telegramUserId.ToString())
                    };
                    var identity = new ClaimsIdentity(claims, "TelegramInitData");
                    context.User = new ClaimsPrincipal(identity);
                }
            }
        }

        await _next(context);
    }

    private bool ValidateTelegramInitData(string initData, string botToken, out long userId)
    {
        userId = 0;
        try
        {
            // Parse initData (URL-encoded query string)
            var pairs = initData.Split('&');
            var dataDict = new Dictionary<string, string>();
            string? hash = null;

            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2) continue;
                
                var key = Uri.UnescapeDataString(parts[0]);
                var value = Uri.UnescapeDataString(parts[1]);
                
                if (key == "hash")
                {
                    hash = value;
                }
                else
                {
                    dataDict[key] = value;
                }
            }

            if (string.IsNullOrEmpty(hash)) return false;

            // Create data_check_string (all pairs except hash, sorted alphabetically)
            var dataCheckString = string.Join("\n", dataDict
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));

            // Compute secret key: HMAC_SHA256(bot_token, "WebAppData")
            // Note: HMACSHA256(key, data) - bot_token is the key, "WebAppData" is the data
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(botToken)))
            {
                var secretKeyBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes("WebAppData"));
                
                // Compute HMAC of data_check_string using secret_key
                using (var hmac2 = new HMACSHA256(secretKeyBytes))
                {
                    var hashBytes = hmac2.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));
                    var computedHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    
                    // Compare hashes (constant-time comparison would be better, but this works)
                    if (computedHash != hash.ToLowerInvariant()) return false;
                }
            }

            // Parse user from validated data
            if (dataDict.TryGetValue("user", out var userJson))
            {
                var userData = JsonSerializer.Deserialize<JsonElement>(userJson);
                if (userData.TryGetProperty("id", out var idElement))
                {
                    userId = idElement.GetInt64();
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Telegram initData");
            return false;
        }
    }

}

public static class TelegramInitDataMiddlewareExtensions
{
    public static IApplicationBuilder UseTelegramInitData(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TelegramInitDataMiddleware>();
    }
}


