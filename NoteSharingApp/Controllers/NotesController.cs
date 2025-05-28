using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NoteSharingApp.Models;
using NoteSharingApp.Repository;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;
using static OpenAI.ObjectModels.SharedModels.IOpenAiModels;

namespace NoteSharingApp.Controllers
{
    // Not işlemlerini yöneten controller (yükleme, listeleme, silme vb.)
    public class NotesController : Controller
    {
        private readonly DatabaseContext _database;

        public NotesController(DatabaseContext database)
        {
            _database = database;
        }

        // Notları listeler
        public async Task<IActionResult> HomePage()
        {
            var notes = await _database.Notes
                .Find(x => true)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync();

            // Get most downloaded notes
            var topDownloadedNotes = await _database.Notes
                .Find(x => true)
                .SortByDescending(x => x.DownloadCount)
                .Limit(5)
                .ToListAsync();

            // Get recently viewed notes
            var recentlyViewedNotes = await _database.Notes
                .Find(x => true)
                .SortByDescending(x => x.LastViewedAt)
                .Limit(5)
                .ToListAsync();

            ViewBag.TopDownloadedNotes = topDownloadedNotes;
            ViewBag.RecentlyViewedNotes = recentlyViewedNotes;

            return View(notes);
        }

        // Yeni not yükleme sayfasını görüntüler
        public async Task<IActionResult> AddNote()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Account");
            }
            ViewBag.UserName = User.Identity.Name;
            var categories = await _database.Categories.Find(_ => true).ToListAsync();
            ViewBag.Categories = categories;
            return View();
        }

        // Not yükleme işlemini gerçekleştirir
        [HttpPost]
        public async Task<IActionResult> AddNote(Note note, IFormFile PdfFile)
        {
            var categories = await _database.Categories.Find(_ => true).ToListAsync();
            ViewBag.Categories = categories;

            // PDF dosyası kontrolü (formdan gelen dosya)
            if (PdfFile == null || PdfFile.Length == 0)
            {
                ModelState.AddModelError("PdfFile", "Lütfen bir PDF dosyası seçin.");
                return View(note);
            }

            ModelState.Remove("PdfFilePath");
            ModelState.Remove("OwnerUsername"); // Formdan gelmediği için doğrulamada hata oluşmasını engeller.

            // Modelin kalan alanlarını doğrula
            if (!ModelState.IsValid)
            {
                return View(note);
            }

            // Dosya yükleme işlemi
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

                // PDF yolunu modele ata
                note.PdfFilePath = "/uploads/" + fileName;
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Dosya yüklenirken bir hata oluştu: " + ex.Message);
                return View(note);
            }

            // Kullanıcının kimliğini al
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                ModelState.AddModelError("", "Kullanıcı kimliği alınamadı.");
                return View(note);
            }

            // String userId'yi ObjectId'ye çevir
            if (!ObjectId.TryParse(userId, out var parsedOwnerId))
            {
                ModelState.AddModelError("", "Geçersiz kullanıcı kimliği.");
                return View(note);
            }

            // MongoDB'den kullanıcıyı bul
            var user = await _database.Users.Find(u => u.UserId == parsedOwnerId).FirstOrDefaultAsync();
            if (user == null)
            {
                ModelState.AddModelError("", "Kullanıcı bulunamadı.");
                return View(note);
            }

            // Kullanıcı bilgilerini not nesnesine ekle
            note.OwnerId = parsedOwnerId;
            note.OwnerUsername = user.UserName;
            note.CreatedAt = DateTime.UtcNow; // CreatedAt'i manuel ekliyoruz (eğer modelde yoksa)

            // Veritabanına kaydet
            try
            {
                var notesCollection = _database.Notes;
                await notesCollection.InsertOneAsync(note);

                // Yeni eklenen notun NoteId'sini al
                var insertedNoteId = note.NoteId; // MongoDB otomatik olarak NoteId atar

                // Kullanıcının SharedNotes listesini güncelle
                if (!user.SharedNotes.Contains(insertedNoteId))
                {
                    user.SharedNotes.Add(insertedNoteId);
                    user.SharedNotesCount = user.SharedNotes.Count;
                    await _database.Users.UpdateOneAsync(
                        u => u.UserId == parsedOwnerId,
                        Builders<User>.Update
                            .Set(u => u.SharedNotes, user.SharedNotes)
                            .Set(u => u.SharedNotesCount, user.SharedNotesCount));
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Veritabanına kaydedilirken bir hata oluştu: " + ex.Message);
                return View(note);
            }

            return RedirectToAction("HomePage");
        }

        // Not detaylarını görüntüler
        public async Task<IActionResult> NoteDetail(string id, string returnUrl)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            if (!ObjectId.TryParse(id, out var objectId))
                return NotFound();

            var note = await _database.Notes.Find(x => x.NoteId == objectId).FirstOrDefaultAsync();
            if (note == null)
                return NotFound();

            // Update LastViewedAt timestamp
            await _database.Notes.UpdateOneAsync(
                n => n.NoteId == objectId,
                Builders<Note>.Update.Set(n => n.LastViewedAt, DateTime.UtcNow)
            );

            // Update user's visited notes if user is authenticated
            if (User.Identity.IsAuthenticated)
            {
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
            }

            ViewBag.ReturnUrl = returnUrl ?? "/Profile";
            return View(note);
        }
    }
}
