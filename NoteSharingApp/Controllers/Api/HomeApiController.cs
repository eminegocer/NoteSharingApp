using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using NoteSharingApp.Models;

namespace NoteSharingApp.Controllers;

[ApiController]
[Route("api/home")]
[Authorize]
public class HomeApiController : ControllerBase
{
    private readonly DatabaseContext _dbContext;

    public HomeApiController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    [AllowAnonymous]
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

        // Cookie kimlik doğrulama için - Web uygulaması için
        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("Cookies", principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        });

        // JWT token üretimi - Mobil uygulama için
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("MySuperStrongSecretKeyForJWTAuth123456789")); // En az 32 karakter
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "NoteSharingApp",
            audience: "NoteSharingAppMobile",
            claims: claims,
            expires: DateTime.Now.AddDays(7),
            signingCredentials: creds
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // Hem cookie kimlik doğrulama hem de JWT token dönüş
        return Ok(new
        {
            message = "Giriş başarılı.",
            userId = _user.UserId,
            userName = _user.UserName,
            token = tokenString // JWT token mobil uygulama için eklendi
        });
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