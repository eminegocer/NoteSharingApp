using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NoteSharingApp.Models;
using NoteSharingApp.Repository;
using System.Security.Claims;

namespace NoteSharingApp.Controllers
{
    // Not indirme işlemlerini yöneten controller
    public class NoteDownloadController : Controller
    {
        private readonly DatabaseContext _database;
        private readonly DownloadedNoteRepository _downloadedNoteRepository;

        public NoteDownloadController(DatabaseContext database)
        {
            _database = database;
            _downloadedNoteRepository = new DownloadedNoteRepository(database);
        }

        // Not indirme sayfasını görüntüler
        public IActionResult Index()
        {
            // ... existing code ...
            return View();
        }

        // Not indirme işlemini gerçekleştirir
        [HttpPost]
        public async Task<IActionResult> TrackDownload(string noteId, string source)
        {
            if (source == "chat")
            {
                return Json(new { success = true, message = "Chat dosyaları alınan notlara eklenmez." });
            }

            if (!User.Identity.IsAuthenticated)
            {
                return Json(new { success = false, message = "Bu işlem için giriş yapmalısınız." });
            }

            if (!ObjectId.TryParse(noteId, out var parsedNoteId))
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!ObjectId.TryParse(userId, out var parsedUserId))
                {
                    return Json(new { success = false, message = "Geçersiz kullanıcı kimliği." });
                }

                var hasDownloaded = await _downloadedNoteRepository.GetUserDownloadedNotes(parsedUserId);
                bool alreadyDownloaded = hasDownloaded.Any(x => x.NotePdfFilePath != null && x.NotePdfFilePath.Contains(noteId));
                if (!alreadyDownloaded)
                {
                    var downloadedNote = new DownloadedNote
                    {
                        UserId = parsedUserId,
                        NoteId = ObjectId.Empty,
                        Source = source ?? "chat",
                        DownloadedAt = DateTime.UtcNow,
                        NoteTitle = noteId,
                        NoteOwnerId = ObjectId.Empty,
                        NoteOwnerUsername = "Chat Dosyası",
                        NoteCategory = "Chat",
                        NotePdfFilePath = $"/chat-files/{noteId}",
                        NotePage = 0,
                        NoteContent = null
                    };
                    await _downloadedNoteRepository.AddAsync(downloadedNote);
                }
                return Json(new { success = true });
            }

            var userId2 = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!ObjectId.TryParse(userId2, out var parsedUserId2))
            {
                return Json(new { success = false, message = "Geçersiz kullanıcı kimliği." });
            }

            var hasDownloaded2 = await _downloadedNoteRepository.HasUserDownloadedNote(parsedUserId2, parsedNoteId);
            if (!hasDownloaded2)
            {
                var note = await _database.Notes.Find(n => n.NoteId == parsedNoteId).FirstOrDefaultAsync();
                if (note == null)
                {
                    var downloadedNote = new DownloadedNote
                    {
                        UserId = parsedUserId2,
                        NoteId = parsedNoteId,
                        Source = source,
                        DownloadedAt = DateTime.UtcNow,
                        NoteTitle = noteId,
                        NoteOwnerId = ObjectId.Empty,
                        NoteOwnerUsername = "Bilinmiyor",
                        NoteCategory = "Bilinmiyor",
                        NotePdfFilePath = null,
                        NotePage = 0,
                        NoteContent = null
                    };
                    await _downloadedNoteRepository.AddAsync(downloadedNote);
                }
                else
                {
                    var downloadedNote2 = new DownloadedNote
                    {
                        UserId = parsedUserId2,
                        NoteId = parsedNoteId,
                        Source = source,
                        DownloadedAt = DateTime.UtcNow,
                        NoteTitle = note.Title,
                        NoteOwnerId = note.OwnerId,
                        NoteOwnerUsername = note.OwnerUsername,
                        NoteCategory = note.Category,
                        NotePdfFilePath = note.PdfFilePath,
                        NotePage = note.Page,
                        NoteContent = note.Content
                    };
                    await _downloadedNoteRepository.AddAsync(downloadedNote2);

                    // Increment the download count for the note
                    await _database.Notes.UpdateOneAsync(
                        n => n.NoteId == parsedNoteId,
                        Builders<Note>.Update.Inc(n => n.DownloadCount, 1)
                    );
                }

                // Update user's ReceivedNotes list
                var user = await _database.Users.Find(u => u.UserId == parsedUserId2).FirstOrDefaultAsync();
                if (user != null && !user.ReceivedNotes.Contains(parsedNoteId))
                {
                    user.ReceivedNotes.Add(parsedNoteId);
                    user.ReceivedNotesCount = user.ReceivedNotes.Count;
                    await _database.Users.UpdateOneAsync(
                        u => u.UserId == parsedUserId2,
                        Builders<User>.Update
                            .Set(u => u.ReceivedNotes, user.ReceivedNotes)
                            .Set(u => u.ReceivedNotesCount, user.ReceivedNotesCount));
                }
            }

            return Json(new { success = true });
        }

        // İndirilen notları listeler
        [HttpGet]
        public async Task<IActionResult> GetDownloadedNotes()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Json(new { success = false, message = "Bu işlem için giriş yapmalısınız." });
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!MongoDB.Bson.ObjectId.TryParse(userId, out var parsedUserId))
            {
                return Json(new { success = false, message = "Geçersiz kullanıcı kimliği." });
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

            return Json(new { success = true, notes = notesDto });
        }

        // Not indirme geçmişini temizler
        [HttpPost]
        public async Task<IActionResult> ClearHistory()
        {
            // ... existing code ...
            return View();
        }
    }
} 