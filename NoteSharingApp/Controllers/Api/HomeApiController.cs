using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NoteSharingApp.Models;

namespace NoteSharingApp.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class HomeApiController : ControllerBase
    {
        private readonly DatabaseContext _dbContext;

        public HomeApiController(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("Index")]
        public IActionResult Index()
        {
            return Ok(new { message = "Welcome to the API!" });
        }

        [HttpGet("Login")]
        public IActionResult Login()
        {
            return Ok(new { message = "Please provide login credentials." });
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login([FromBody] User user)
        {
            if (user != null)
            {
                var _user = await _dbContext.Users.Find(x => x.Password == user.Password && x.UserName == user.UserName).FirstOrDefaultAsync();

                if (_user == null)
                {
                    return Unauthorized(new { message = "Kullanici adi veya sifre hatali." });
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, _user.UserId.ToString()),
                    new Claim(ClaimTypes.Name, _user.UserName ?? "Bilinmeyen Kullanici")
                };

                var identity = new ClaimsIdentity(claims, "Cookies");
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync("Cookies", principal, new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                });

                return Ok(new { message = "Login successful." });
            }
            return BadRequest(new { message = "Invalid user data." });
        }

        [HttpGet("Register")]
        public IActionResult Register()
        {
            return Ok(new { message = "Please provide registration details." });
        }

        [HttpPost("Register")]
        public async Task<IActionResult> Register([FromBody] User user)
        {
            if (user == null)
            {
                return BadRequest(new { message = "User data is null." });
            }

            User _user = user;
            _user.UserName = user.UserName;
            _user.Email = user.Email;
            _user.Password = user.Password;
            _user.SchoolName = user.SchoolName;

            if (ModelState.IsValid)
            {
                var registered = await _dbContext.Users.Find(x => x.Email == user.Email).FirstOrDefaultAsync();

                if (registered != null)
                {
                    return Conflict(new { message = "Bu e-posta adresi zaten kullanilmaktadir." });
                }
                else
                {
                    await _dbContext.Users.InsertOneAsync(_user);
                    return Ok(new { message = "Registration successful." });
                }
            }
            return BadRequest(ModelState);
        }

        [HttpGet("Privacy")]
        public IActionResult Privacy()
        {
            return Ok(new { message = "Privacy details here." });
        }

        [HttpGet("Error")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return StatusCode(500, new { message = "An error occurred.", requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
