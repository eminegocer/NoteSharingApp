using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

namespace NoteSharingApp.Controllers.API
{
    [Route("api/chat")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer,Cookies")]
    public class ChatApiController : ControllerBase
    {
        private readonly DatabaseContext _database;

        public ChatApiController(DatabaseContext database)
        {
            _database = database;
        }

        [HttpPost("start-chat")]
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

        [HttpPost("add-chat")]
        public async Task<IActionResult> AddChatDb([FromBody] string userName)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!ObjectId.TryParse(userId, out var parsedOwnerId))
            {
                return BadRequest(new { success = false, message = "Geçersiz kullanıcı kimliği." });
            }

            var user2 = await _database.Users.Find(u => u.UserId == parsedOwnerId).FirstOrDefaultAsync();
            if (user2 == null)
            {
                return NotFound(new { success = false, message = "Kullanıcı bulunamadı." });
            }

            var user = await _database.Users.Find(x => x.UserName == userName).FirstOrDefaultAsync();
            if (user == null)
            {
                return NotFound(new { success = false, message = "Hedef kullanıcı bulunamadı." });
            }

            ObjectId id1 = user.UserId;
            ObjectId id2 = user2.UserId;

            var chat = await _database.Chats
                .Find(x => x.UsersId.Contains(id1) && x.UsersId.Contains(id2))
                .FirstOrDefaultAsync();

            if (chat == null)
            {
                chat = new Chat
                {
                    UsersId = new List<ObjectId> { id1, id2 },
                    SenderUsername = user2.UserName,
                    ReceiverUsername = user.UserName,
                };

                await _database.Chats.InsertOneAsync(chat);
            }

            return Ok(new
            {
                success = true,
                chatId = chat.Id.ToString(),
                targetUsername = user.UserName,
                senderUsername = user2.UserName
            });
        }
        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromBody] MessageDto messageDto)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!ObjectId.TryParse(userId, out var parsedOwnerId))
            {
                return BadRequest(new { success = false, message = "Geçersiz kullanıcı kimliği." });
            }

            var sender = await _database.Users.Find(u => u.UserId == parsedOwnerId).FirstOrDefaultAsync();
            if (sender == null)
            {
                return NotFound(new { success = false, message = "Gönderen kullanıcı bulunamadı." });
            }

            var receiver = await _database.Users.Find(x => x.UserName == messageDto.TargetUsername).FirstOrDefaultAsync();
            if (receiver == null)
            {
                return NotFound(new { success = false, message = "Hedef kullanıcı bulunamadı." });
            }

            var chat = await _database.Chats
                .Find(x => x.UsersId.Contains(sender.UserId) && x.UsersId.Contains(receiver.UserId))
                .FirstOrDefaultAsync();

            if (chat == null)
            {
                return BadRequest(new { success = false, message = "Sohbet bulunamadı." });
            }

            // Yeni mesaj oluştur
            var message = new Message(sender.UserName, messageDto.Content);

            // Chat modelinde Messages listesini güncelle
            if (chat.Messages == null)
            {
                chat.Messages = new List<Message>();
            }
            chat.Messages.Add(message);

            // Chat'i güncelle
            await _database.Chats.ReplaceOneAsync(x => x.Id == chat.Id, chat);

            return Ok(new
            {
                success = true,
                message = "Mesaj başarıyla gönderildi.",
                data = new
                {
                    senderUsername = message.SenderUsername,
                    content = message.Content,
                    createdAt = message.CreatedAt
                }
            });
        }

        public class MessageDto
        {
            public string TargetUsername { get; set; }
            public string Content { get; set; }
        }

        [HttpGet("search-users")]
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

        [HttpPost("upload-file")]
        public async Task<IActionResult> UploadChatFile(IFormFile file)
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
                    fileUrl = fileUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Dosya yüklenirken bir hata oluştu: " + ex.Message });
            }
        }

        [HttpGet("chat-history")]
        public async Task<IActionResult> GetChatHistory([FromQuery] string targetUsername)
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
                        (c.SenderUsername == currentUsername && c.ReceiverUsername == targetUsername) ||
                        (c.SenderUsername == targetUsername && c.ReceiverUsername == currentUsername))
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

                return Ok(new { chatHistory });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Sohbet yüklenirken bir hata oluştu." });
            }
        }

        [HttpGet("chat-users")]
        public async Task<IActionResult> GetChatUsersForHomePage()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Ok(new List<string>());
            }

            var currentUsername = User.Identity.Name;

            var chatList = await _database.Chats
                .Find(c => c.SenderUsername == currentUsername || c.ReceiverUsername == currentUsername)
                .SortByDescending(c => c.CreatedAt)
                .ToListAsync();

            var uniqueChats = chatList
                .Select(c => c.SenderUsername == currentUsername ? c.ReceiverUsername : c.SenderUsername)
                .Distinct()
                .ToList();

            return Ok(uniqueChats);
        }

        [HttpPost("join-group")]
        public async Task<IActionResult> JoinGroup([FromBody] string groupId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!ObjectId.TryParse(userId, out var parsedUserId))
            {
                return BadRequest(new { success = false, message = "Geçersiz kullanıcı kimliği." });
            }

            var chat = await _database.Chats.Find(c => c.Id == ObjectId.Parse(groupId)).FirstOrDefaultAsync();
            if (chat == null)
            {
                return NotFound(new { success = false, message = "Grup bulunamadı." });
            }

            if (!chat.UsersId.Contains(parsedUserId))
            {
                chat.UsersId.Add(parsedUserId);
                await _database.Chats.ReplaceOneAsync(c => c.Id == chat.Id, chat);
            }

            return Ok(new { success = true, message = "Gruba başarıyla katıldınız." });
        }

        [HttpGet("search-groups")]
        public async Task<IActionResult> GetGroup([FromQuery] string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return Ok(new List<object>());

            var schoolGroups = await _database.SchoolGroups
                .Find(g => g.GroupName.ToLower().Contains(searchTerm.ToLower()) ||
                           g.SchoolName.ToLower().Contains(searchTerm.ToLower()) ||
                           g.DepartmentName.ToLower().Contains(searchTerm.ToLower()))
                .Limit(5)
                .ToListAsync();

            var result = schoolGroups.Select(g => new
            {
                id = g.Id.ToString(),
                groupName = g.GroupName,
                schoolName = g.SchoolName,
                departmentName = g.DepartmentName
            });

            return Ok(result);
        }

        [HttpPost("join-school-group")]
        public async Task<IActionResult> JoinSchoolGroup([FromBody] string groupId)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var username = User.Identity.Name;

                if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out var userObjectId))
                {
                    return BadRequest(new { success = false, message = "Geçersiz kullanıcı kimliği." });
                }

                if (string.IsNullOrEmpty(groupId) || !ObjectId.TryParse(groupId, out var groupObjectId))
                {
                    return BadRequest(new { success = false, message = "Geçersiz grup kimliği." });
                }

                var group = await _database.SchoolGroups.Find(g => g.Id == groupObjectId).FirstOrDefaultAsync();
                if (group == null)
                {
                    return NotFound(new { success = false, message = "Grup bulunamadı." });
                }

                if (group.ParticipantIds.Contains(userObjectId))
                {
                    return Ok(new { success = true, message = "Zaten bu gruba katılmışsınız." });
                }

                group.ParticipantIds.Add(userObjectId);
                var updateResult = await _database.SchoolGroups.ReplaceOneAsync(g => g.Id == groupObjectId, group);

                if (updateResult.ModifiedCount > 0)
                {
                    return Ok(new
                    {
                        success = true,
                        groupId = group.Id.ToString(),
                        groupName = group.GroupName
                    });
                }
                else
                {
                    return StatusCode(500, new { success = false, message = "Gruba katılırken bir hata oluştu." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Bir hata oluştu: {ex.Message}" });
            }
        }

        [HttpGet("user-school-groups")]
        public async Task<IActionResult> GetUserSchoolGroups()
        {
            try
            {
                if (!User.Identity.IsAuthenticated)
                {
                    return Ok(new List<object>());
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out var userObjectId))
                {
                    return Ok(new List<object>());
                }

                var userGroups = await _database.SchoolGroups
                    .Find(g => g.ParticipantIds.Contains(userObjectId))
                    .ToListAsync();

                var result = userGroups.Select(g => new
                {
                    id = g.Id.ToString(),
                    groupName = g.GroupName,
                    schoolName = g.SchoolName,
                    departmentName = g.DepartmentName
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [Authorize]
        [HttpPost("send-group-message")]
        public async Task<IActionResult> SendGroupMessage([FromBody] GroupMessageRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.GroupId) || !ObjectId.TryParse(request.GroupId, out var groupObjectId))
                {
                    return BadRequest(new { success = false, message = "Geçersiz grup kimliği." });
                }

                if (string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.SenderUsername))
                {
                    return BadRequest(new { success = false, message = "Mesaj içeriği ve kullanıcı adı zorunludur." });
                }

                var group = await _database.SchoolGroups
                    .Find(g => g.Id == groupObjectId)
                    .FirstOrDefaultAsync();

                if (group == null)
                {
                    return NotFound(new { success = false, message = "Grup bulunamadı." });
                }

                var newMessage = new Message
                {
                    SenderUsername = request.SenderUsername,
                    Content = request.Content,
                    CreatedAt = DateTime.UtcNow,
                    FileUrl = request.FileUrl
                };
                group.Messages.Add(newMessage);

                await _database.SchoolGroups.ReplaceOneAsync(g => g.Id == groupObjectId, group);

                return Ok(new { success = true, message = "Mesaj başarıyla gönderildi." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Request modeli:
        public class GroupMessageRequest
        {
            public string GroupId { get; set; }
            public string SenderUsername { get; set; }
            public string Content { get; set; }
            public string FileUrl { get; set; } // opsiyonel
        }

        // Mesaj modeli:
        public class GroupMessage
        {
            public string SenderUsername { get; set; }
            public string Content { get; set; }
            public DateTime CreatedAt { get; set; }
            public string FileUrl { get; set; }
        }
        [HttpGet("group-messages")]
        public async Task<IActionResult> GetGroupMessages([FromQuery] string groupId)
        {
            if (string.IsNullOrEmpty(groupId) || !ObjectId.TryParse(groupId, out var groupObjectId))
            {
                return BadRequest(new { success = false, message = "Geçersiz grup kimliği." });
            }

            // Önce Groups koleksiyonunda ara
            var group = await _database.Groups
                .Find(g => g.Id == groupObjectId.ToString())
                .FirstOrDefaultAsync();

            if (group != null)
            {
                var messages = group.Messages
                    .OrderBy(m => m.SentAt)
                    .Select(m => new
                    {
                        senderUsername = m.SenderUsername,
                        content = m.Content,
                        createdAt = m.SentAt,
                        fileUrl = m.FileUrl,
                    });
                return Ok(messages);
            }

            // Sonra SchoolGroups koleksiyonunda ara
            var schoolGroup = await _database.SchoolGroups
                .Find(g => g.Id == groupObjectId)
                .FirstOrDefaultAsync();

            if (schoolGroup != null)
            {
                var messages = schoolGroup.Messages
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => new
                    {
                        senderUsername = m.SenderUsername,
                        content = m.Content,
                        createdAt = m.CreatedAt,
                        fileUrl = m.FileUrl,
                    });
                return Ok(messages);
            }

            return NotFound(new { success = false, message = "Grup bulunamadı." });
        }

        [HttpGet("user-groups")]
        public async Task<IActionResult> GetUserGroups()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out var userObjectId))
                return Ok(new List<object>());

            var filter = Builders<Group>.Filter.AnyEq(g => g.UserIds, userObjectId.ToString());

            var userGroups = await _database.Groups.Find(filter).ToListAsync();

            var result = userGroups.Select(g => new
            {
                id = g.Id.ToString(),
                groupName = g.GroupName,
                memberCount = g.UserIds.Count,
                type = "group", // Bunu ayırt etmek için ekle
                                // diğer alanlar...
            });

            return Ok(result);
        }

        [HttpGet("/api/user/current")]
        public IActionResult GetCurrentUser()
        {
            if (!User.Identity.IsAuthenticated)
                return Unauthorized();

            var username = User.Identity.Name;
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Ok(new
            {
                userName = username,
                userId = userId
            });
        }
    }



    public class StartChatRequest
    {
        public string Username { get; set; }
    }
}
