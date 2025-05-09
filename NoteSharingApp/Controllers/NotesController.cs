using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NoteSharingApp.Models;
using NoteSharingApp.Repository;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace NoteSharingApp.Controllers
{
    public class NotesController : Controller
    {
        private readonly DatabaseContext _database;

        public NotesController(DatabaseContext database)
        {
            _database = database;
        }

        public async Task<IActionResult> HomePage()
        {
            var notes = _database.Notes
                .Find(x => true)
                .SortByDescending(x => x.CreatedAt)
                .ToList();

            return View(notes);
        }

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

            // Veritabanına kaydetS
            try
            {
                var notesCollection = _database.Notes;
                await notesCollection.InsertOneAsync(note);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Veritabanına kaydedilirken bir hata oluştu: " + ex.Message);
                return View(note);
            }

            return RedirectToAction("HomePage");
        }

        public async Task<IActionResult> NoteDetail(string id, string returnUrl)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            if (!ObjectId.TryParse(id, out var objectId))
                return NotFound();

            var note = await _database.Notes.Find(x => x.NoteId == objectId).FirstOrDefaultAsync();
            if (note == null)
                return NotFound();

            ViewBag.ReturnUrl = returnUrl ?? "/Profile";
            return View(note);
        }

        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] GroupCreationModel model)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Json(new { success = false, message = "Grup oluşturmak için giriş yapmalısınız." });
            }

            var currentUsername = User.Identity.Name;
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(model.GroupName))
            {
                return Json(new { success = false, message = "Grup adı boş olamaz." });
            }

            if (model.Users == null || model.Users.Count == 0)
            {
                return Json(new { success = false, message = "En az bir kullanıcı seçmelisiniz." });
            }

            try
            {
                // Kullanıcı ID'lerini al
                var userIds = new List<string>();
                foreach (var username in model.Users)
                {
                    var user = await _database.Users.Find(u => u.UserName == username).FirstOrDefaultAsync();
                    if (user != null)
                    {
                        userIds.Add(user.UserId.ToString());
                    }
                }

                // Mevcut kullanıcıyı da ekle
                if (!string.IsNullOrEmpty(currentUserId))
                {
                    userIds.Add(currentUserId);
                }

                // Yeni grup oluştur
                var group = new Group
                {
                    GroupName = model.GroupName,
                    UserIds = userIds,
                    CreatedBy = currentUsername,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    Messages = new List<GroupMessage>()
                };

                await _database.Groups.InsertOneAsync(group);

                return Json(new { success = true, message = "Grup başarıyla oluşturuldu." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Grup oluşturulurken bir hata oluştu." });
            }
        }
    }

    public class GroupCreationModel
    {
        public string GroupName { get; set; }
        public List<string> Users { get; set; }
    }
}
