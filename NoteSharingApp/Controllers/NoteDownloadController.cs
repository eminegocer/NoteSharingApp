using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NoteSharingApp.Models;
using NoteSharingApp.Repository;
using System.Security.Claims;

namespace NoteSharingApp.Controllers
{
    public class NoteDownloadController : Controller
    {
        private readonly DatabaseContext _database;
        private readonly DownloadedNoteRepository _downloadedNoteRepository;

        public NoteDownloadController(DatabaseContext database)
        {
            _database = database;
            _downloadedNoteRepository = new DownloadedNoteRepository(database);
        }

        [HttpPost]
        public async Task<IActionResult> TrackDownload(string noteId, string source)
        {
            // Sohbet dosyaları alınan notlara eklenmesin
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
                // Eğer noteId bir ObjectId değilse (ör: chat dosyası), yine de kayıt oluştur
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!ObjectId.TryParse(userId, out var parsedUserId))
                {
                    return Json(new { success = false, message = "Geçersiz kullanıcı kimliği." });
                }

                // Aynı dosya için tekrar kayıt olmasın diye kontrol
                var hasDownloaded = await _downloadedNoteRepository.GetUserDownloadedNotes(parsedUserId);
                bool alreadyDownloaded = hasDownloaded.Any(x => x.NotePdfFilePath != null && x.NotePdfFilePath.Contains(noteId));
                if (!alreadyDownloaded)
                {
                    var downloadedNote = new DownloadedNote
                    {
                        UserId = parsedUserId,
                        NoteId = ObjectId.Empty, // Not yok, boş bırak
                        Source = source ?? "chat",
                        DownloadedAt = DateTime.UtcNow,
                        NoteTitle = noteId, // Dosya adı
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

            // Check if user has already downloaded this note
            var hasDownloaded2 = await _downloadedNoteRepository.HasUserDownloadedNote(parsedUserId2, parsedNoteId);
            if (!hasDownloaded2)
            {
                // Notun detaylarını al
                var note = await _database.Notes.Find(n => n.NoteId == parsedNoteId).FirstOrDefaultAsync();
                if (note == null)
                {
                    // Not yoksa, yine de kayıt oluştur (ör: silinmiş not)
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
                    return Json(new { success = true });
                }

                // Create new download record with details
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

                // Update user's received notes count
                var user = await _database.Users.Find(u => u.UserId == parsedUserId2).FirstOrDefaultAsync();
                if (user != null)
                {
                    user.ReceivedNotesCount++;
                    await _database.Users.UpdateOneAsync(
                        u => u.UserId == parsedUserId2,
                        Builders<User>.Update.Set(u => u.ReceivedNotesCount, user.ReceivedNotesCount)
                    );
                }
            }

            return Json(new { success = true });
        }

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

            // DownloadedNote içindeki detayları frontend ile uyumlu alan adlarıyla döndür
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
    }
} 