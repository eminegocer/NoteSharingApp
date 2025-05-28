using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using NoteSharingApp.Models;
using System.Security.Claims;
using NoteSharingApp.Repository;

namespace NoteSharingApp.Controllers
{
    // Kullanıcı profil işlemlerini yöneten controller
    public class ProfileController : Controller
    {
        private readonly IMongoCollection<User> _users;
        private readonly IMongoCollection<Note> _notes;
        private readonly DownloadedNoteRepository _downloadedNoteRepository;
        private readonly DatabaseContext _database;

        public ProfileController(IMongoClient mongoClient, DatabaseContext database)
        {
            var db = mongoClient.GetDatabase("NoteSharingApp");
            _users = db.GetCollection<User>("Users");
            _notes = db.GetCollection<Note>("Notes");
            _database = database;
            _downloadedNoteRepository = new DownloadedNoteRepository(database);
        }

        // Profil sayfasını görüntüler
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _users.Find(u => u.UserId == ObjectId.Parse(userId)).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound();
            }

            // Get user's shared notes using SharedNotes list
            var sharedNotes = await _notes.Find(n => user.SharedNotes.Contains(n.NoteId))
                .SortByDescending(n => n.CreatedAt)
                .ToListAsync();

            // Get user's received notes using ReceivedNotes list
            var receivedNotes = await _notes.Find(n => user.ReceivedNotes.Contains(n.NoteId))
                .SortByDescending(n => n.CreatedAt)
                .ToListAsync();

            // Update counts
            user.SharedNotesCount = sharedNotes.Count;
            user.ReceivedNotesCount = receivedNotes.Count;

            // Update the user document in the database
            await _users.UpdateOneAsync(
                u => u.UserId == user.UserId,
                Builders<User>.Update
                    .Set(u => u.SharedNotesCount, user.SharedNotesCount)
                    .Set(u => u.ReceivedNotesCount, user.ReceivedNotesCount));

            var viewModel = new ProfileViewModel
            {
                User = user,
                SharedNotes = sharedNotes,
                ReceivedNotes = receivedNotes
            };

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile(ProfileUpdateModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("Index", model);
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var update = Builders<User>.Update
                .Set(u => u.Bio, model.Bio)
                .Set(u => u.Department, model.Department)
                .Set(u => u.Year, model.Year)
                .Set(u => u.SchoolName, model.SchoolName);

            if (!string.IsNullOrEmpty(model.ProfilePicture))
            {
                update = update.Set(u => u.ProfilePicture, model.ProfilePicture);
            }

            await _users.UpdateOneAsync(
                u => u.UserId == ObjectId.Parse(userId),
                update);

            return RedirectToAction("Index");
        }
    }

    public class ProfileViewModel
    {
        public User User { get; set; }
        public List<Note> SharedNotes { get; set; }
        public List<Note> ReceivedNotes { get; set; }
    }

    public class ProfileUpdateModel
    {
        public string? Bio { get; set; }
        public string? Department { get; set; }
        public int? Year { get; set; }
        public string? SchoolName { get; set; }
        public string? ProfilePicture { get; set; }
    }
} 