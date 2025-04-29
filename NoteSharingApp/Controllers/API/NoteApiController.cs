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
    [Authorize(AuthenticationSchemes = "Bearer")] // Sadece Bearer Token ile kimlik doğrulaması yapılmış kullanıcılar erişebilir.
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

        [HttpGet("categories")]
        public async Task<IActionResult> GetNoteCategories()
        {
            try
            {
                // Tüm notlardan benzersiz kategorileri al
                var categories = await _database.Notes
                    .Find(_ => true)
                    .Project(n => n.Category)
                    .ToListAsync();

                // Boş ve null kategorileri filtrele ve benzersiz kategorileri al
                var uniqueCategories = categories
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                return Ok(uniqueCategories);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Kategoriler alınırken bir hata oluştu.", error = ex.Message });
            }
        }

        [HttpPost]
        [Authorize] // Kullanıcının kimlik doğrulaması yapılmış olmalı
        public async Task<IActionResult> AddNote(
            [FromForm] string Title,
            [FromForm] string Content,
            [FromForm] string Category,
            [FromForm] int Page,
            [FromForm] IFormFile PdfFile)  
        {
            // PDF dosyası seçilmiş mi kontrolü
            if (PdfFile == null || PdfFile.Length == 0)
            {
                return BadRequest(new { message = "Lütfen bir PDF dosyası seçin." });
            }

            // Yeni bir Note nesnesi oluşturulur
            var note = new Note
            {
                Title = Title,
                Content = Content,
                Category = Category,
                Page = Page,
                CreatedAt = DateTime.UtcNow  
            };

            // Dosya yükleme işlemi
            try
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
                // Eğer uploads klasörü yoksa oluştur
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }
                // Dosya adı benzersiz olsun diye GUID ile yeni bir isim ver
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(PdfFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);

                // Dosyayı sunucudaki ilgili dizine kaydet
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await PdfFile.CopyToAsync(stream);
                }

                // Not nesnesine dosya yolu eklenir (kullanıcılar ulaşabilsin diye)
                note.PdfFilePath = "/uploads/" + fileName;
            }
            catch (Exception ex)
            {
              
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Dosya yüklenirken bir hata oluştu.", error = ex.Message });
            }

            // Kullanıcı kimliğini al (token içinden)
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            // Kimlik geçerli mi kontrolü
            if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out var parsedOwnerId))
            {
                return Unauthorized(new { message = "Geçersiz kullanıcı kimliği." });
            }

            // Kullanıcı bilgileri veritabanından çekilir
            var user = await _database.Users.Find(u => u.UserId == parsedOwnerId).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound(new { message = "Kullanıcı bulunamadı." });
            }

            // Not nesnesine kullanıcı bilgileri atanır
            note.OwnerId = parsedOwnerId;
            note.OwnerUsername = user.UserName;

            // Notu veritabanına kaydet
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

        // Not arama işlemi
        [HttpGet("search")]
        public async Task<IActionResult> SearchNotes([FromQuery] string term)
        {
            // Arama terimi boş mu kontrolü
            if (string.IsNullOrWhiteSpace(term))
                return BadRequest("Arama terimi boş olamaz.");

            // Kullanıcıları arama: SchoolName veya UserName alanlarında arar
            var userFilter = Builders<User>.Filter.Or(
                Builders<User>.Filter.Regex("SchoolName", new BsonRegularExpression(term, "i")), // "i" => büyük/küçük harf duyarsız arama
                Builders<User>.Filter.Regex("UserName", new BsonRegularExpression(term, "i"))
            );

            // Eşleşen kullanıcıları bul
            var matchedUsers = await _database.Users.Find(userFilter).ToListAsync();
            var matchedUserIds = matchedUsers.Select(u => u.UserId).ToList();

            // Notları arama: Başlık, içerik, kategori veya kullanıcı id'si eşleşiyorsa
            var noteFilter = Builders<Note>.Filter.Or(
                Builders<Note>.Filter.Regex("Title", new BsonRegularExpression(term, "i")),
                Builders<Note>.Filter.Regex("Content", new BsonRegularExpression(term, "i")),
                Builders<Note>.Filter.Regex("Category", new BsonRegularExpression(term, "i")),
                Builders<Note>.Filter.In("OwnerId", matchedUserIds) // Kullanıcı ile ilişkili notlar
            );

            // Eşleşen notları getir
            var notes = await _database.Notes.Find(noteFilter).ToListAsync();

            // Sonucu döner
            return Ok(notes);
        }

    }
}
