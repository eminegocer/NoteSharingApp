using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NoteSharingApp.Models;
using NoteSharingApp.Repository;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NoteSharingApp.Controllers
{
    [Route("api/notes")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class NoteApiController : ControllerBase
    {
        private readonly DatabaseContext _database;

        public NoteApiController(DatabaseContext database)
        {
            _database = database;
        }

        [HttpGet]
        public async Task<IActionResult> GetNotes()
        {
            var notes = await _database.Notes.Find(_ => true).SortByDescending(x => x.CreatedAt).ToListAsync();
            return Ok(notes);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AddNote(
    [FromForm] string Title,
    [FromForm] string Content,
    [FromForm] string Category,
    [FromForm] int Page,
    [FromForm] IFormFile PdfFile) // Dosya parametresi aynı kalıyor
        {
            // PDF dosyası kontrolü (aynı kalıyor)
            if (PdfFile == null || PdfFile.Length == 0)
            {
                return BadRequest(new { message = "Lütfen bir PDF dosyası seçin." });
            }

            // Note nesnesini metodun içinde manuel olarak oluştur
            var note = new Note
            {
                Title = Title,
                Content = Content,
                Category = Category,
                Page = Page,
                // CreatedAt gibi diğer gerekli alanları burada veya veritabanı katmanında ayarlayabilirsin
                CreatedAt = DateTime.UtcNow // Örnek
            };

            // --- Geri kalan kod aynı ---
            // try-catch bloğu (dosya yükleme)
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
                note.PdfFilePath = "/uploads/" + fileName; // PdfFilePath burada atanıyor
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Dosya yüklenirken bir hata oluştu.", error = ex.Message });
            }

            // Kullanıcı bilgilerini alıp note nesnesine ata (aynı kalıyor)
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out var parsedOwnerId))
            {
                return Unauthorized(new { message = "Geçersiz kullanıcı kimliği." });
            }
            var user = await _database.Users.Find(u => u.UserId == parsedOwnerId).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound(new { message = "Kullanıcı bulunamadı." });
            }
            note.OwnerId = parsedOwnerId;
            note.OwnerUsername = user.UserName; // OwnerUsername burada atanıyor

            // Veritabanına kaydet (aynı kalıyor)
            try
            {
                await _database.Notes.InsertOneAsync(note);
                return CreatedAtAction(nameof(GetNotes), new { id = note.NoteId }, note);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Veritabanına kaydedilirken bir hata oluştu.", error = ex.Message });
            }
        }
    }
}
