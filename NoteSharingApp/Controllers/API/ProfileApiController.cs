using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using NoteSharingApp.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace NoteSharingApp.Controllers
{
    [ApiController]
    [Route("api/profile")]
    [Authorize(AuthenticationSchemes = "Bearer")]

    public class ProfileApiController : ControllerBase
    {
        private readonly IMongoCollection<User> _users;
        private readonly IMongoCollection<Note> _notes;

        public ProfileApiController(IMongoClient mongoClient)
        {
            var database = mongoClient.GetDatabase("NoteSharingApp");
            _users = database.GetCollection<User>("Users");
            _notes = database.GetCollection<Note>("Notes");
        }

        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Console.WriteLine($"GELEN USERID: {userId}");
            if (string.IsNullOrEmpty(userId))
            {
                Console.WriteLine("USERID YOK, UNAUTHORIZED");
                return Unauthorized();
            }

            var user = await _users.Find(u => u.UserId == ObjectId.Parse(userId)).FirstOrDefaultAsync();
            if (user == null)
            {
                Console.WriteLine("KULLANICI BULUNAMADI, 404");
                return NotFound();
            }

            // Get user's shared notes
            var sharedNotesIds = user.SharedNotes;
            var sharedNotes = await _notes.Find(n => sharedNotesIds.Contains(n.NoteId))
                .SortByDescending(n => n.CreatedAt)
                .ToListAsync();

            // Get user's received notes
            var receivedNotesIds = user.ReceivedNotes;
            var receivedNotes = await _notes.Find(n => receivedNotesIds.Contains(n.NoteId))
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

            var response = new
            {
                User = user,
                SharedNotes = sharedNotes,
                ReceivedNotes = receivedNotes
            };

            return Ok(response);
        }

        [HttpPost("update")]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileUpdateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
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

            var result = await _users.UpdateOneAsync(
                u => u.UserId == ObjectId.Parse(userId),
                update);

            if (result.ModifiedCount == 0)
            {
                return NotFound();
            }

            return Ok(new { message = "Profile updated successfully" });
        }

        [HttpGet("shared-notes")]
        public async Task<IActionResult> GetSharedNotes()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var user = await _users.Find(u => u.UserId == ObjectId.Parse(userId)).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound();
            }

            var sharedNotes = await _notes.Find(n => user.SharedNotes.Contains(n.NoteId))
                .SortByDescending(n => n.CreatedAt)
                .ToListAsync();

            return Ok(sharedNotes);
        }
        [HttpGet("received-notes")]
        public async Task<IActionResult> GetReceivedNotes()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var user = await _users.Find(u => u.UserId == ObjectId.Parse(userId)).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound();
            }

            var receivedNotes = await _notes.Find(n => user.ReceivedNotes.Contains(n.NoteId))
                .SortByDescending(n => n.CreatedAt)
                .ToListAsync();

            return Ok(receivedNotes);
        }

        [HttpPost("share-note/{noteId}")]
        public async Task<IActionResult> ShareNote(string noteId)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Ok(new { success = false, message = "Bu işlem için giriş yapmalısınız." });
            }

            if (!ObjectId.TryParse(noteId, out var parsedNoteId))
            {
                return Ok(new { success = false, message = "Geçersiz not kimliği." });
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!ObjectId.TryParse(userId, out var parsedUserId))
            {
                return Ok(new { success = false, message = "Geçersiz kullanıcı kimliği." });
            }

            var user = await _users.Find(u => u.UserId == parsedUserId).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound("Kullanıcı bulunamadı.");
            }

            var note = await _notes.Find(n => n.NoteId == parsedNoteId).FirstOrDefaultAsync();
            if (note == null || note.OwnerUsername != user.UserName) // Sadece kendi notlarını paylaşabilir
            {
                return BadRequest("Not bulunamadı veya paylaşım yetkiniz yok.");
            }

            if (!user.SharedNotes.Contains(parsedNoteId))
            {
                user.SharedNotes.Add(parsedNoteId);
                user.SharedNotesCount = user.SharedNotes.Count;
                await _users.UpdateOneAsync(
                    u => u.UserId == parsedUserId,
                    Builders<User>.Update
                        .Set(u => u.SharedNotes, user.SharedNotes)
                        .Set(u => u.SharedNotesCount, user.SharedNotesCount));
            }

            return Ok(new { success = true, message = "Not paylaşıma açıldı." });
        }
    }

}