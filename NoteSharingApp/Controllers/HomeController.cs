using System.Diagnostics;
using System.Threading.Tasks;
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
        if(user !=null)
        {
            var _user =await  _dbContext.Users.Find(x => x.Password == user.Password && x.UserName == user.UserName).FirstOrDefaultAsync();

            if (_user == null)
            {
                ModelState.AddModelError("", "Kullanýcý adý veya þifre hatalý.");
                return View();
            }

            return RedirectToAction("HomePage","Notes");
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
                ModelState.AddModelError("Email", "Bu e-posta adresi zaten kullanýlmaktadýr.");
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
