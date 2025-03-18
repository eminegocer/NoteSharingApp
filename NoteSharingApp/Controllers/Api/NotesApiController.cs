using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NoteSharingApp.Models;
using NoteSharingApp.Repository;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;

namespace NoteSharingApp.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotesApiController : ControllerBase
    {
        private readonly DatabaseContext _database;

        public NotesApiController(DatabaseContext database)
        {
            _database = database;
        }

        [HttpGet("HomePage")]
        public async Task<IActionResult> HomePage()
        {
            var notes = _database.Notes
                .Find(x => true)
                .SortByDescending(x => x.CreatedAt)
                .ToList();

            return Ok(notes);
        }

        [HttpGet("AddNote")]
        public async Task<IActionResult> AddNote()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Unauthorized(new { message = "Lütfen giriş yapın." });
            }
            var categories = await _database.Categories.Find(_ => true).ToListAsync();
            return Ok(new { categories, userName = User.Identity.Name });
        }

        [HttpPost("AddNote")]
        public async Task<IActionResult> AddNote([FromBody] Note note, [FromForm] IFormFile PdfFile)
        {
            var categories = await _database.Categories.Find(_ => true).ToListAsync();

            if (PdfFile == null || PdfFile.Length == 0)
            {
                return BadRequest(new { message = "Lütfen bir PDF dosyası seçin." });
            }

            ModelState.Remove("PdfFilePath");
            ModelState.Remove("OwnerUsername");

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(PdfFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await PdfFile.CopyToAsync(stream);
                }

                note.PdfFilePath = "/uploads/" + fileName;
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Dosya yüklenirken bir hata oluştu: " + ex.Message });
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest(new { message = "Kullanıcı kimliği alınamadı." });
            }

            if (!ObjectId.TryParse(userId, out var parsedOwnerId))
            {
                return BadRequest(new { message = "Geçersiz kullanıcı kimliği." });
            }

            var user = await _database.Users.Find(u => u.UserId == parsedOwnerId).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound(new { message = "Kullanıcı bulunamadı." });
            }

            note.OwnerId = parsedOwnerId;
            note.OwnerUsername = user.UserName;

            try
            {
                var notesCollection = _database.Notes;
                await notesCollection.InsertOneAsync(note);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Veritabanına kaydedilirken bir hata oluştu: " + ex.Message });
            }

            return Ok(new { message = "Not başarıyla eklendi." });
        }
    }
}
