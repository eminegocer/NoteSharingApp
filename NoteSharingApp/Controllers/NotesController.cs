using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NoteSharingApp.Models;
using NoteSharingApp.Repository;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NoteSharingApp.Controllers
{
    public class NotesController : Controller
    {
        private readonly DatabaseContext _database;
        private readonly IMongoCollection<SchoolGroup> _schoolGroups;

        public NotesController(DatabaseContext database)
        {
            _database = database;
        }

        public async Task<IActionResult> HomePage()
        {
            var currentUsername = User.Identity.Name;
            var notes = _database.Notes
                .Find(x => true)
                .SortByDescending(x => x.CreatedAt)
                .ToList();

            // Get user's chat list
            var chatList = await _database.Chats
                .Find(c => c.SenderUsername == currentUsername || c.ReceiverUsername == currentUsername)
                .SortByDescending(c => c.Timestamp)
                .ToListAsync();

            // Get unique usernames from chats
            var uniqueChats = chatList
                .Select(c => c.SenderUsername == currentUsername ? c.ReceiverUsername : c.SenderUsername)
                .Distinct()
                .ToList();

            ViewBag.ChatUsers = uniqueChats;
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
            note.OwnerUsername = user.UserName; // Artık sorunsuz şekilde alınır

            // Veritabanına kaydet
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

        [HttpGet]
        public async Task<IActionResult> SearchUsers(string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return Json(new List<string>());

            var users = await _database.Users
                .Find(u => u.UserName.ToLower().Contains(searchTerm.ToLower()))
                .Project(u => u.UserName)
                .Limit(5)
                .ToListAsync();

            return Json(users);
        }

        [HttpPost]
        public async Task<IActionResult> StartPersonalChat([FromBody] StartChatRequest request)
        {
            if (string.IsNullOrEmpty(request.Username))
            {
                return Json(new { success = false, message = "Kullanıcı adı boş olamaz." });
            }

            // Kullanıcı kontrolü
            var targetUser = await _database.Users
                .Find(u => u.UserName == request.Username)
                .FirstOrDefaultAsync();

            if (targetUser == null)
            {
                return Json(new { success = false, message = "Bu kullanıcı adında bir kayıt bulunamadı." });
            }

            return Json(new { 
                success = true, 
                targetUserId = targetUser.UserId,
                targetUsername = targetUser.UserName
            });
        }

        public class StartChatRequest
        {
            public string Username { get; set; }
        }

        public async Task<IActionResult> ChatView(string targetUsername)
        {
            var currentUsername = User.Identity.Name;
            
            // Get chat history
            var chatHistory = await _database.Chats
                .Find(c => 
                    (c.SenderUsername == currentUsername && c.ReceiverUsername == targetUsername) ||
                    (c.SenderUsername == targetUsername && c.ReceiverUsername == currentUsername))
                .SortBy(c => c.Timestamp)
                .ToListAsync();

            ViewBag.TargetUsername = targetUsername;
            ViewBag.ChatHistory = chatHistory;
            return PartialView("_ChatPartial");
        }

        [HttpGet]
        public async Task<IActionResult> SearchSchoolGroups(string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return Json(new List<string>());

            var schoolGroups = await _database.SchoolGroups
                .Find(g => g.GroupName.ToLower().Contains(searchTerm.ToLower()))
                .Project(g => g.GroupName)
                .Limit(5)
                .ToListAsync();

            return Json(schoolGroups);
        }
    }
}
