using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using NoteSharingApp.Models;
using NoteSharingApp.Repository;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NoteSharingApp.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatApiController : ControllerBase
    {
        private readonly DatabaseContext _database;

        public ChatApiController(DatabaseContext database)
        {
            _database = database;
        }

        [HttpPost("AddChatDb")]
        public IActionResult AddChatDb([FromBody] string userName)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!ObjectId.TryParse(userId, out var parsedOwnerId))
            {
                return BadRequest(new { success = false, message = "Geçersiz kullanıcı kimliği." });
            }

            User user2 = _database.Users.Find(u => u.UserId == parsedOwnerId).FirstOrDefault();
            if (user2 == null)
            {
                return NotFound(new { success = false, message = "Kullanıcı bulunamadı." });
            }

            User user = _database.Users.Find(x => x.UserName == userName).FirstOrDefault();
            if (user == null)
            {
                return NotFound(new { success = false, message = "Hedef kullanıcı bulunamadı." });
            }

            ObjectId id1 = user.UserId;
            ObjectId id2 = user2.UserId;

            Chat _chat = _database.Chats.Find(x => x.UsersId.Contains(id1) && x.UsersId.Contains(id2)).FirstOrDefault();
            if (_chat == null)
            {
                var chat = new Chat
                {
                    UsersId = new List<ObjectId> { id1, id2 },
                    SenderUsername = user2.UserName,
                    ReceiverUsername = user.UserName,
                };

                _database.Chats.InsertOne(chat);
                _chat = chat;
            }

            return Ok(new
            {
                success = true,
                chatId = _chat.Id.ToString(),
                targetUsername = user.UserName,
                senderUsername = user2.UserName
            });
        }

        [HttpGet("SearchUsers")]
        public async Task<IActionResult> SearchUsers([FromQuery] string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return Ok(new List<string>());

            var users = await _database.Users
                .Find(u => u.UserName.ToLower().Contains(searchTerm.ToLower()))
                .Project(u => u.UserName)
                .Limit(5)
                .ToListAsync();

            return Ok(users);
        }

        [HttpPost("StartPersonalChat")]
        public async Task<IActionResult> StartPersonalChat([FromBody] StartChatRequest request)
        {
            if (string.IsNullOrEmpty(request.Username))
            {
                return BadRequest(new { success = false, message = "Kullanıcı adı boş olamaz." });
            }

            var targetUser = await _database.Users
                .Find(u => u.UserName == request.Username)
                .FirstOrDefaultAsync();

            if (targetUser == null)
            {
                return NotFound(new { success = false, message = "Bu kullanıcı adında bir kayıt bulunamadı." });
            }

            return Ok(new
            {
                success = true,
                targetUserId = targetUser.UserId,
                targetUsername = targetUser.UserName
            });
        }

        public class StartChatRequest
        {
            public string Username { get; set; }
        }

        [HttpPost("UploadChatFile")]
        public async Task<IActionResult> UploadChatFile([FromForm] IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { success = false, message = "Dosya seçilmedi." });
                }

                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "chat-files");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var fileUrl = $"/chat-files/{uniqueFileName}";
                return Ok(new
                {
                    success = true,
                    fileName = file.FileName,
                    fileUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Dosya yüklenirken bir hata oluştu: " + ex.Message });
            }
        }

        [HttpGet("ChatView")]
        public async Task<IActionResult> ChatView([FromQuery] string targetUsername)
        {
            try
            {
                var currentUsername = User.Identity.Name;

                var targetUser = await _database.Users.Find(u => u.UserName == targetUsername).FirstOrDefaultAsync();
                if (targetUser == null)
                {
                    return NotFound(new { success = false, message = "Kullanıcı bulunamadı." });
                }

                var chatHistory = await _database.Chats
                    .Find(c =>
                        c.SenderUsername == currentUsername && c.ReceiverUsername == targetUsername ||
                        c.SenderUsername == targetUsername && c.ReceiverUsername == currentUsername)
                    .SortBy(c => c.CreatedAt)
                    .FirstOrDefaultAsync();

                if (chatHistory == null)
                {
                    var currentUser = await _database.Users.Find(u => u.UserName == currentUsername).FirstOrDefaultAsync();

                    chatHistory = new Chat
                    {
                        UsersId = new List<ObjectId> { currentUser.UserId, targetUser.UserId },
                        SenderUsername = currentUsername,
                        ReceiverUsername = targetUsername,
                        Messages = new List<Message>(),
                        CreatedAt = DateTime.UtcNow
                    };

                    await _database.Chats.InsertOneAsync(chatHistory);
                }

                return Ok(new { success = true, chatHistory });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Bir hata oluştu: " + ex.Message });
            }
        }
    }
}
