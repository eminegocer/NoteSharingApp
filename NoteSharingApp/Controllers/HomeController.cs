using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;
using MongoDB.Driver;
using NoteSharingApp.Models;

namespace NoteSharingApp.Controllers;

// Ana sayfa ve temel sayfa yönlendirmelerini yöneten controller
public class HomeController : Controller
{
    private readonly DatabaseContext _dbContext;

    public HomeController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Ana sayfayı görüntüler
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(User user)
    {
        if (user != null)
        {
            var _user = await _dbContext.Users.Find(x => x.Password == user.Password && x.UserName == user.UserName).FirstOrDefaultAsync();

            if (_user == null)
            {
                ModelState.AddModelError("", "Kullanici adi veya sifre hatali.");
                return View();
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, _user.UserId.ToString()),
                new Claim(ClaimTypes.Name, _user.UserName ?? "Bilinmeyen Kullanici")
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            return RedirectToAction("HomePage", "Notes");
        }
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Register(User user)
    {
        if(user == null)
        {
            return NotFound();
        }

        User _user = user;
        _user.UserName = user.UserName;
        _user.Email = user.Email;
        _user.Password = user.Password;
        _user.SchoolName = user.SchoolName;

        if(ModelState.IsValid)
        {
            var registered = await _dbContext.Users.Find(x => x.Email == user.Email).FirstOrDefaultAsync();

            if(registered != null)
            {
                ModelState.AddModelError("Email", "Bu e-posta adresi zaten kullanilmaktadir.");
                return View(user);
            }
            else
            {
                await _dbContext.Users.InsertOneAsync(_user);
                return RedirectToAction("Login");
            }
        }
        return View(user);
    }

    // Gizlilik politikası sayfasını görüntüler
    public IActionResult Privacy()
    {
        return View();
    }

    // Hata sayfasını görüntüler
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
