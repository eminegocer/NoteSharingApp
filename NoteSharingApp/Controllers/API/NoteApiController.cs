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

namespace NoteSharingApp.Controllers.API
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
        // Controller'da (örneğin NotesController.cs içinde)
        [HttpGet("category/{category}")]
        public async Task<IActionResult> GetNotesByCategory(string category)
        {
            try
            {
                var filter = Builders<Note>.Filter.Eq(n => n.Category, category);
                var notes = await _database.Notes.Find(filter).ToListAsync();

                var noteDtos = notes.Select(n => new NoteDto
                {
                    NoteId = n.NoteId.ToString(),
                    OwnerId = n.OwnerId.ToString(),
                    OwnerUsername = n.OwnerUsername,
                    Title = n.Title,
                    Content = n.Content,
                    Category = n.Category,
                    PdfFilePath = n.PdfFilePath,
                    Page = n.Page,
                    CreatedAt = n.CreatedAt,
                    DownloadCount = n.DownloadCount,
                    LastViewedAt = n.LastViewedAt
                }).ToList();

                return Ok(noteDtos);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Notlar getirilirken bir hata oluştu", error = ex.Message });
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
        [Authorize]
        [HttpGet("top-downloaded")]
        public async Task<IActionResult> GetTopDownloadedNotes()
        {
            var topNotes = await _database.Notes
                .Find(x => true)
                .SortByDescending(x => x.DownloadCount)
                .Limit(5)
                .ToListAsync();

            var result = topNotes.Select(note => new NoteDto
            {
                NoteId = note.NoteId.ToString(), // .Id MongoDB'de _id alanı
                Page = note.Page,
                OwnerId = note.OwnerId.ToString(),
                OwnerUsername = note.OwnerUsername,
                Title = note.Title,
                Content = note.Content,
                Category = note.Category,
                PdfFilePath = note.PdfFilePath,
                CreatedAt = note.CreatedAt,
                DownloadCount = note.DownloadCount,
                LastViewedAt = note.LastViewedAt
            }).ToList();

            return Ok(result);
        }

        [Authorize]
        [HttpGet("recently-viewed")]
        public async Task<IActionResult> GetRecentlyViewedNotes()
        {
            var recentNotes = await _database.Notes
                .Find(x => true)
                .SortByDescending(x => x.LastViewedAt)
                .Limit(5)
                .ToListAsync();

            var result = recentNotes.Select(note => new NoteDto
            {
                NoteId = note.NoteId.ToString(), // .Id MongoDB'de _id alanı
                Page = note.Page,
                OwnerId = note.OwnerId.ToString(),
                OwnerUsername = note.OwnerUsername,
                Title = note.Title,
                Content = note.Content,
                Category = note.Category,
                PdfFilePath = note.PdfFilePath,
                CreatedAt = note.CreatedAt,
                DownloadCount = note.DownloadCount,
                LastViewedAt = note.LastViewedAt
            }).ToList();

            return Ok(result);
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
            var notesDto = notes.Select(n => new {
                noteId = n.NoteId.ToString(),
                page = n.Page,
                ownerUsername = n.OwnerUsername,
                title = n.Title,
                content = n.Content,
                category = n.Category,
                pdfFilePath = n.PdfFilePath,
                createdAt = n.CreatedAt,
                downloadCount = n.DownloadCount,
                lastViewedAt = n.LastViewedAt
            });
            return Ok(notesDto);
        }

        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetNoteById(string id)
        {
            if (string.IsNullOrEmpty(id) || !ObjectId.TryParse(id, out var objectId))
            {
                return BadRequest(new { message = "Geçersiz not kimliği." });
            }

            var note = await _database.Notes.Find(n => n.NoteId == objectId).FirstOrDefaultAsync();
            if (note == null)
            {
                return NotFound(new { message = "Not bulunamadı." });
            }

            // DTO'ya map'le (güvenlik için)
            var noteDto = new NoteDto
            {
                NoteId = note.NoteId.ToString(),
                Page = note.Page,
                OwnerId = note.OwnerId.ToString(),
                OwnerUsername = note.OwnerUsername,
                Title = note.Title,
                Content = note.Content,
                Category = note.Category,
                PdfFilePath = note.PdfFilePath,
                CreatedAt = note.CreatedAt,
                DownloadCount = note.DownloadCount,
                LastViewedAt = note.LastViewedAt
            };

            return Ok(noteDto);
        }

        [HttpPost("view")]
        [Authorize]
        public async Task<IActionResult> TrackNoteView([FromBody] string noteId)
        {
            if (string.IsNullOrEmpty(noteId) || !ObjectId.TryParse(noteId, out var parsedNoteId))
                return BadRequest(new { message = "Geçersiz not kimliği." });

            // Notun LastViewedAt alanını güncelle
            var update = Builders<Note>.Update.Set(n => n.LastViewedAt, DateTime.UtcNow);
            var result = await _database.Notes.UpdateOneAsync(n => n.NoteId == parsedNoteId, update);

            if (result.ModifiedCount == 0)
                return NotFound(new { message = "Not bulunamadı." });

            // Kullanıcıyı bul
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out var parsedUserId))
                return BadRequest(new { message = "Invalid user ID" });

            var user = await _database.Users.Find(u => u.UserId == parsedUserId).FirstOrDefaultAsync();
            if (user == null)
                return NotFound(new { message = "User not found" });

            // Notu bul
            var note = await _database.Notes.Find(n => n.NoteId == parsedNoteId).FirstOrDefaultAsync();
            if (note == null)
                return NotFound(new { message = "Not bulunamadı." });

            // VisitedNotes güncelle
            var visitedNote = new VisitedNote
            {
                NoteId = note.NoteId,
                Title = note.Title,
                VisitedAt = DateTime.UtcNow
            };

            // Eğer daha önce eklenmişse eskiyi sil
            user.VisitedNotes.RemoveAll(n => n.NoteId.ToString() == noteId);
            // En başa ekle
            user.VisitedNotes.Insert(0, visitedNote);
            // Maksimum 10 kayıt tut (isteğe bağlı)
            if (user.VisitedNotes.Count > 10)
                user.VisitedNotes = user.VisitedNotes.Take(10).ToList();

            // Kullanıcıyı güncelle
            var userUpdate = Builders<User>.Update.Set(u => u.VisitedNotes, user.VisitedNotes);
            await _database.Users.UpdateOneAsync(u => u.UserId == parsedUserId, userUpdate);

            return Ok(new { success = true });
        }

        [HttpGet("detail/{id}")]
        [Authorize]
        public async Task<IActionResult> GetNoteDetail(string id)
        {
            if (string.IsNullOrEmpty(id))
                return BadRequest(new { message = "Note ID is required" });

            if (!ObjectId.TryParse(id, out var objectId))
                return BadRequest(new { message = "Invalid Note ID format" });

            var note = await _database.Notes.Find(x => x.NoteId == objectId).FirstOrDefaultAsync();
            if (note == null)
                return NotFound(new { message = "Note not found" });

            // Update LastViewedAt timestamp
            await _database.Notes.UpdateOneAsync(
                n => n.NoteId == objectId,
                Builders<Note>.Update.Set(n => n.LastViewedAt, DateTime.UtcNow)
            );

            // Update user's visited notes
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId) && ObjectId.TryParse(userId, out var parsedUserId))
            {
                var user = await _database.Users.Find(u => u.UserId == parsedUserId).FirstOrDefaultAsync();
                if (user != null)
                {
                    // Create new visited note entry
                    var visitedNote = new VisitedNote
                    {
                        NoteId = note.NoteId,
                        Title = note.Title,
                        VisitedAt = DateTime.UtcNow
                    };

                    // Remove if note already exists in visited notes
                    user.VisitedNotes.RemoveAll(vn => vn.NoteId == note.NoteId);

                    // Add to visited notes list and keep only last 4
                    user.VisitedNotes.Insert(0, visitedNote);
                    if (user.VisitedNotes.Count > 4)
                    {
                        user.VisitedNotes = user.VisitedNotes.Take(4).ToList();
                    }

                    // Update user in database
                    await _database.Users.UpdateOneAsync(
                        u => u.UserId == parsedUserId,
                        Builders<User>.Update.Set(u => u.VisitedNotes, user.VisitedNotes)
                    );
                }
            }

            // Return note as DTO
            var noteDto = new NoteDto
            {
                NoteId = note.NoteId.ToString(),
                Page = note.Page,
                OwnerId = note.OwnerId.ToString(),
                OwnerUsername = note.OwnerUsername,
                Title = note.Title,
                Content = note.Content,
                Category = note.Category,
                PdfFilePath = note.PdfFilePath,
                CreatedAt = note.CreatedAt,
                DownloadCount = note.DownloadCount,
                LastViewedAt = note.LastViewedAt
            };

            return Ok(noteDto);
        }

        [HttpGet("visited")]
        [Authorize]
        public async Task<IActionResult> GetVisitedNotes()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out var parsedUserId))
                return BadRequest(new { message = "Invalid user ID" });

            var user = await _database.Users.Find(u => u.UserId == parsedUserId).FirstOrDefaultAsync();
            if (user == null)
                return NotFound(new { message = "User not found" });

            // Get full note details for visited notes
            var visitedNotesWithDetails = new List<object>();
            foreach (var visitedNote in user.VisitedNotes)
            {
                var note = await _database.Notes.Find(n => n.NoteId == visitedNote.NoteId).FirstOrDefaultAsync();
                if (note != null)
                {
                    visitedNotesWithDetails.Add(new
                    {
                        NoteId = note.NoteId.ToString(),
                        Title = note.Title,
                        Category = note.Category,
                        OwnerUsername = note.OwnerUsername,
                        VisitedAt = visitedNote.VisitedAt,
                        PdfFilePath = note.PdfFilePath,
                        DownloadCount = note.DownloadCount
                    });
                }
            }

            return Ok(visitedNotesWithDetails);
        }

        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentNotes()
        {
            var notes = await _database.Notes
                .Find(x => true)
                .SortByDescending(x => x.LastViewedAt)
                .Limit(5)
                .ToListAsync();

            return Ok(notes);
        }
    }

    public class NoteDto
    {
        public string NoteId { get; set; }
        public string OwnerId { get; set; }
        public string OwnerUsername { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Category { get; set; }
        public string PdfFilePath { get; set; }
        public int Page { get; set; }
        public DateTime CreatedAt { get; set; }
        public int DownloadCount { get; set; }
        public DateTime? LastViewedAt { get; set; }
        // Diğer alanlar...
    }
}