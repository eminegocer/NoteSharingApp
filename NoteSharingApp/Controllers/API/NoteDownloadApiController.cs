using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NoteSharingApp.Models;
using NoteSharingApp.Repository;
using System.Security.Claims;

namespace NoteSharingApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class NoteDownloadApiController : ControllerBase
    {
        private readonly DatabaseContext _database;
        private readonly DownloadedNoteRepository _downloadedNoteRepository;

        public NoteDownloadApiController(DatabaseContext database)
        {
            _database = database;
            _downloadedNoteRepository = new DownloadedNoteRepository(database);
        }

        [HttpPost("track-download")]
        public async Task<IActionResult> TrackDownload([FromBody] DownloadRequest request)
        {
            if (!User.Identity.IsAuthenticated)
                return Unauthorized(new { success = false, message = "Bu işlem için giriş yapmalısınız." });

            if (string.IsNullOrEmpty(request.NoteId))
                return BadRequest(new { success = false, message = "Geçersiz not kimliği." });

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!ObjectId.TryParse(userId, out var parsedUserId))
                return BadRequest(new { success = false, message = "Geçersiz kullanıcı kimliği." });

            // Notu önce ObjectId olarak, sonra string olarak aramayı dene
            Note note = null;
            if (ObjectId.TryParse(request.NoteId, out var parsedNoteId))
            {
                note = await _database.Notes.Find(n => n.NoteId == parsedNoteId).FirstOrDefaultAsync();
            }
            if (note == null)
            {
                note = await _database.Notes.Find(n => n.NoteId.ToString() == request.NoteId).FirstOrDefaultAsync();
            }
            if (note == null)
                return NotFound(new { success = false, message = "Not bulunamadı." });

            // Kullanıcı daha önce bu notu indirmiş mi kontrol et (NoteId ile)
            var hasDownloaded = await _downloadedNoteRepository.GetUserDownloadedNotes(parsedUserId);
            bool alreadyDownloaded = hasDownloaded.Any(x => x.NoteId == note.NoteId);

            if (!alreadyDownloaded)
            {
                var downloadedNote = new DownloadedNote
                {
                    UserId = parsedUserId,
                    NoteId = note.NoteId,
                    Source = request.Source ?? "note_detail",
                    DownloadedAt = DateTime.UtcNow,
                    NoteTitle = note.Title,
                    NoteOwnerId = note.OwnerId,
                    NoteOwnerUsername = note.OwnerUsername,
                    NoteCategory = note.Category,
                    NotePdfFilePath = note.PdfFilePath,
                    NotePage = note.Page,
                    NoteContent = note.Content
                };
                await _downloadedNoteRepository.AddAsync(downloadedNote);

                // --- KULLANICIYA NOTU EKLE ---
                var user = await _database.Users.Find(u => u.UserId == parsedUserId).FirstOrDefaultAsync();
                if (user != null)
                {
                    if (!user.ReceivedNotes.Contains(note.NoteId))
                    {
                        user.ReceivedNotes.Add(note.NoteId);
                        user.ReceivedNotesCount = user.ReceivedNotes.Count;
                        await _database.Users.UpdateOneAsync(
                            u => u.UserId == parsedUserId,
                            Builders<User>.Update
                                .Set(u => u.ReceivedNotes, user.ReceivedNotes)
                                .Set(u => u.ReceivedNotesCount, user.ReceivedNotesCount)
                        );
                    }
                }
            }
            return Ok(new { success = true });
        }

        [HttpGet("downloaded-notes")]
        public async Task<IActionResult> GetDownloadedNotes()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Unauthorized(new { success = false, message = "Bu işlem için giriş yapmalısınız." });
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!ObjectId.TryParse(userId, out var parsedUserId))
            {
                return BadRequest(new { success = false, message = "Geçersiz kullanıcı kimliği." });
            }

            var downloadedNotes = await _downloadedNoteRepository.GetUserDownloadedNotes(parsedUserId);

            var notesDto = downloadedNotes.Select(n => new {
                NoteId = n.NoteId.ToString(),
                NoteTitle = n.NoteTitle,
                NoteCategory = n.NoteCategory,
                DownloadedAt = n.DownloadedAt,
                NoteContent = n.NoteContent,
                NoteOwnerUsername = n.NoteOwnerUsername,
                NotePdfFilePath = n.NotePdfFilePath,
                NotePage = n.NotePage,
                Source = n.Source
            });

            return Ok(new { success = true, notes = notesDto });
        }
    }

    public class DownloadRequest
    {
        public string NoteId { get; set; }
        public string Source { get; set; }
    }
} 