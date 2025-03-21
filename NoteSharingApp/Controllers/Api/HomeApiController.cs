using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NoteSharingApp.Models;

namespace NoteSharingApp.Controllers;

[ApiController]
[Route("api/home")]
public class HomeApiController : ControllerBase
{
    private readonly DatabaseContext _dbContext;

    public HomeApiController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] User user)
    {
        if (user == null)
        {
            return BadRequest(new { message = "Geçersiz giriş verileri." });
        }

        var _user = await _dbContext.Users.Find(x => x.Password == user.Password && x.UserName == user.UserName).FirstOrDefaultAsync();

        if (_user == null)
        {
            return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, _user.UserId.ToString()),
            new Claim(ClaimTypes.Name, _user.UserName ?? "Bilinmeyen Kullanıcı")
        };

        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("Cookies", principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        });

        return Ok(new { message = "Giriş başarılı.", userId = _user.UserId, userName = _user.UserName });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] User user)
    {
        if (user == null)
        {
            return BadRequest(new { message = "Geçersiz kayıt verileri." });
        }

        var existingUser = await _dbContext.Users.Find(x => x.Email == user.Email).FirstOrDefaultAsync();

        if (existingUser != null)
        {
            return Conflict(new { message = "Bu e-posta adresi zaten kullanılmaktadır." });
        }

        await _dbContext.Users.InsertOneAsync(user);
        return Ok(new { message = "Kayıt başarılı.", userId = user.UserId });
    }

    [HttpGet("error")]
    public IActionResult Error()
    {
        return Problem(detail: "Sunucu hatası oluştu.", statusCode: 500);
    }
}
