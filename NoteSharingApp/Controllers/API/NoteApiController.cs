using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NoteSharingApp.Models;
using NoteSharingApp.Repository;
using System.Security.Claims;
using System.IO;
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
                var categories = await _database.Notes
                    .Find(_ => true)
                    .Project(n => n.Category)
                    .ToListAsync();

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
        [Authorize]
        public async Task<IActionResult> AddNote(
            [FromForm] string Title,
            [FromForm] string Content,
            [FromForm] string Category,
            [FromForm] int Page,
            [FromForm] IFormFile PdfFile)
        {
            if (PdfFile == null || PdfFile.Length == 0)
            {
                return BadRequest(new { message = "Lütfen bir PDF dosyası seçin." });
            }

            var note = new Note
            {
                Title = Title,
                Content = Content,
                Category = Category,
                Page = Page,
                CreatedAt = DateTime.UtcNow
            };

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
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Dosya yüklenirken bir hata oluştu.", error = ex.Message });
            }

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
            note.OwnerUsername = user.UserName;

            try
            {
                await _database.Notes.InsertOneAsync(note);

                // Yeni eklenen notun NoteId'sini al ve SharedNotes listesine ekle
                if (!user.SharedNotes.Contains(note.NoteId))
                {
                    user.SharedNotes.Add(note.NoteId);
                    user.SharedNotesCount = user.SharedNotes.Count;
                    await _database.Users.UpdateOneAsync(
                        u => u.UserId == parsedOwnerId,
                        Builders<User>.Update
                            .Set(u => u.SharedNotes, user.SharedNotes)
                            .Set(u => u.SharedNotesCount, user.SharedNotesCount));
                }

                return CreatedAtAction(nameof(GetNotes), new { id = note.NoteId }, note);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Veritabanına kaydedilirken bir hata oluştu.", error = ex.Message });
            }
        }

        [HttpPost("download")]
        [Authorize]
        public async Task<IActionResult> TrackDownload(string noteId, string source)
        {
            if (string.IsNullOrEmpty(noteId) || !ObjectId.TryParse(noteId, out var parsedNoteId))
            {
                return BadRequest(new { success = false, message = "Geçersiz not kimliği." });
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out var parsedUserId))
            {
                return Unauthorized(new { success = false, message = "Geçersiz kullanıcı kimliği." });
            }

            var user = await _database.Users.Find(u => u.UserId == parsedUserId).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound(new { success = false, message = "Kullanıcı bulunamadı." });
            }

            // Check if the note exists
            var note = await _database.Notes.Find(n => n.NoteId == parsedNoteId).FirstOrDefaultAsync();
            if (note == null)
            {
                return NotFound(new { success = false, message = "Not bulunamadı." });
            }

            // Avoid duplicate downloads
            if (!user.ReceivedNotes.Contains(parsedNoteId))
            {
                user.ReceivedNotes.Add(parsedNoteId);
                user.ReceivedNotesCount = user.ReceivedNotes.Count;
                await _database.Users.UpdateOneAsync(
                    u => u.UserId == parsedUserId,
                    Builders<User>.Update
                        .Set(u => u.ReceivedNotes, user.ReceivedNotes)
                        .Set(u => u.ReceivedNotesCount, user.ReceivedNotesCount));
            }

            return Ok(new { success = true, message = "Not indirildi." });
        }

        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<IActionResult> SearchNotes([FromQuery] string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Ok(new List<Note>());

            var userFilter = Builders<User>.Filter.Or(
                Builders<User>.Filter.Regex("SchoolName", new BsonRegularExpression(term, "i")),
                Builders<User>.Filter.Regex("UserName", new BsonRegularExpression(term, "i"))
            );

            var matchedUsers = await _database.Users.Find(userFilter).ToListAsync();
            var matchedUserIds = matchedUsers.Select(u => u.UserId).ToList();

            var noteFilter = Builders<Note>.Filter.Or(
                Builders<Note>.Filter.Regex("Title", new BsonRegularExpression(term, "i")),
                Builders<Note>.Filter.Regex("Content", new BsonRegularExpression(term, "i")),
                Builders<Note>.Filter.Regex("Category", new BsonRegularExpression(term, "i")),
                Builders<Note>.Filter.In("OwnerId", matchedUserIds)
            );

            var notes = await _database.Notes.Find(noteFilter).ToListAsync();
            return Ok(notes);
        }
    }
}