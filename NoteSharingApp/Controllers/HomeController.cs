using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NoteSharingApp.Models;

namespace NoteSharingApp.Controllers;

public class HomeController : Controller
{


    private readonly DatabaseContext _dbContext;

    public HomeController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

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
            // Veritaban�ndan kullan�c�y� bul
            var _user = await _dbContext.Users.Find(x => x.Password == user.Password && x.UserName == user.UserName).FirstOrDefaultAsync();

            if (_user == null)
            {
                ModelState.AddModelError("", "Kullan�c� ad� veya �ifre hatal�.");
                return View();
            }

            // Claims olu�tururken veritaban�ndan gelen _user nesnesini kullan
            var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, _user.UserId.ToString()), // Veritaban�ndan gelen UserId
            new Claim(ClaimTypes.Name, _user.UserName ?? "Bilinmeyen Kullan�c�") // Veritaban�ndan gelen UserName
        };

            var identity = new ClaimsIdentity(claims, "Cookies");
            var principal = new ClaimsPrincipal(identity);

            // Kullan�c�y� oturum a�m�� olarak i�aretle
            await HttpContext.SignInAsync("Cookies", principal, new AuthenticationProperties
            {
                IsPersistent = true, // Cookie kal�c� olsun mu? (Taray�c� kapand���nda silinmez)
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) // 7 g�n ge�erli
            });

            return RedirectToAction("HomePage", "Notes");
        }
        return View();
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
                ModelState.AddModelError("Email", "Bu e-posta adresi zaten kullan�lmaktad�r.");
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

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
