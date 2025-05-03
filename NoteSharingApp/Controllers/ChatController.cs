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

namespace NoteSharingApp.Controllers
{
    public class ChatController : Controller
    {
        private readonly DatabaseContext _database;

        public ChatController(DatabaseContext database)
        {
            _database = database;
        }

        [HttpPost]
        public IActionResult AddChatDb(string userName)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // String userId'yi ObjectId'ye çevir
            if (!ObjectId.TryParse(userId, out var parsedOwnerId))
            {
                return Json(new { success = false, message = "Geçersiz kullanıcı kimliği." });
            }

            // MongoDB'den kullanıcıyı bul
            User user2 = _database.Users.Find(u => u.UserId == parsedOwnerId).FirstOrDefault();
            if (user2 == null)
            {
                return Json(new { success = false, message = "Kullanıcı bulunamadı." });
            }

            User user = _database.Users.Find(x => x.UserName == userName).FirstOrDefault();
            if (user == null)
            {
                return Json(new { success = false, message = "Hedef kullanıcı bulunamadı." });
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

            return Json(new
            {
                success = true,
                chatId = _chat.Id.ToString(),
                targetUsername = user.UserName,
                senderUsername = user2.UserName
            });
        }

        [HttpGet]
        public async Task<IActionResult> SearchUsers(string searchTerm)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Json(new List<object>());
            }

            var currentUsername = User.Identity.Name;

            // Kullanıcıları ara (mevcut kullanıcı hariç)
            var users = await _database.Users
                .Find(u => u.UserName != currentUsername && u.UserName.Contains(searchTerm))
                .Project(u => new { username = u.UserName })
                .Limit(10)
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

            return Json(new
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

        [HttpPost]
        public async Task<IActionResult> UploadChatFile(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Json(new { success = false, message = "Dosya seçilmedi." });
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
                return Json(new
                {
                    success = true,
                    fileName = file.FileName,
                    fileUrl = fileUrl
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Dosya yüklenirken bir hata oluştu: " + ex.Message });
            }
        }

        public async Task<IActionResult> ChatView(string targetUsername)
        {
            try
            {
                var currentUsername = User.Identity.Name;

                // Hedef kullanıcının varlığını kontrol et
                var targetUser = await _database.Users.Find(u => u.UserName == targetUsername).FirstOrDefaultAsync();
                if (targetUser == null)
                {
                    return Json(new { success = false, message = "Kullanıcı bulunamadı." });
                }

                // Get chat history
                var chatHistory = await _database.Chats
                    .Find(c =>
                        (c.SenderUsername == currentUsername && c.ReceiverUsername == targetUsername) ||
                        (c.SenderUsername == targetUsername && c.ReceiverUsername == currentUsername))
                    .SortBy(c => c.CreatedAt)
                    .FirstOrDefaultAsync();

                // Eğer chat yoksa yeni bir chat oluştur
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

                ViewBag.TargetUsername = targetUsername;
                ViewBag.ChatHistory = new List<Chat> { chatHistory };
                return PartialView("_ChatPartial");
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Sohbet yüklenirken bir hata oluştu." });
            }
        }

        // Yeni metot: Ana sayfa için sohbet kullanıcılarını yükle
        public async Task<IActionResult> GetChatUsersForHomePage()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Json(new List<string>());
            }

            var currentUsername = User.Identity.Name;

            // Kullanıcının sohbet listesini al
            var chatList = await _database.Chats
                .Find(c => c.SenderUsername == currentUsername || c.ReceiverUsername == currentUsername)
                .SortByDescending(c => c.CreatedAt)
                .ToListAsync();

            // Benzersiz kullanıcı adlarını çıkar
            var uniqueChats = chatList
                .Select(c => c.SenderUsername == currentUsername ? c.ReceiverUsername : c.SenderUsername)
                .Distinct()
                .ToList();

            return Json(uniqueChats);
        }

        [HttpPost]
        public async Task<IActionResult> JoinGroup(string groupId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!ObjectId.TryParse(userId, out var parsedUserId))
            {
                return Json(new { success = false, message = "Geçersiz kullanıcı kimliği." });
            }

            var chat = await _database.Chats.Find(c => c.Id == ObjectId.Parse(groupId)).FirstOrDefaultAsync();
            if (chat == null)
            {
                return Json(new { success = false, message = "Grup bulunamadı." });
            }

            if (!chat.UsersId.Contains(parsedUserId))
            {
                chat.UsersId.Add(parsedUserId);
                await _database.Chats.ReplaceOneAsync(c => c.Id == chat.Id, chat);
            }

            return Json(new { success = true, message = "Gruba başarıyla katıldınız." });
        }

        [HttpGet]
        public async Task<IActionResult> GetGroup(string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return Json(new List<object>());

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

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> JoinSchoolGroup(string groupId)
        {
            try
            {
                // Kullanıcı kimliğini al
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var username = User.Identity.Name;

                if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out var userObjectId))
                {
                    return Json(new { success = false, message = "Geçersiz kullanıcı kimliği." });
                }

                if (string.IsNullOrEmpty(groupId) || !ObjectId.TryParse(groupId, out var groupObjectId))
                {
                    return Json(new { success = false, message = "Geçersiz grup kimliği." });
                }

                // Grubu bul
                var group = await _database.SchoolGroups.Find(g => g.Id == groupObjectId).FirstOrDefaultAsync();
                if (group == null)
                {
                    return Json(new { success = false, message = "Grup bulunamadı." });
                }

                // Kullanıcı zaten grupta mı kontrol et
                if (group.ParticipantIds.Contains(userObjectId))
                {
                    return Json(new { success = true, message = "Zaten bu gruba katılmışsınız." });
                }

                // Kullanıcıyı gruba ekle
                group.ParticipantIds.Add(userObjectId);
                var updateResult = await _database.SchoolGroups.ReplaceOneAsync(g => g.Id == groupObjectId, group);

                if (updateResult.ModifiedCount > 0)
                {
                    return Json(new
                    {
                        success = true,
                        groupId = group.Id.ToString(),
                        groupName = group.GroupName
                    });
                }
                else
                {
                    return Json(new { success = false, message = "Gruba katılırken bir hata oluştu." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Bir hata oluştu: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUserSchoolGroups()
        {
            try
            {
                if (!User.Identity.IsAuthenticated)
                {
                    return Json(new List<object>());
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out var userObjectId))
                {
                    return Json(new List<object>());
                }

                // Kullanıcının katıldığı grupları bul
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

                return Json(result);
            }
            catch (Exception ex)
            {
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetGroupMessages(string groupId)
        {
            try
            {
                if (string.IsNullOrEmpty(groupId) || !ObjectId.TryParse(groupId, out var groupObjectId))
                {
                    return Json(new List<object>());
                }

                // Önce normal gruplarda ara
                var group = await _database.Groups
                    .Find(g => g.Id == groupId)
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

                    return Json(messages);
                }

                // Normal grupta bulunamazsa okul gruplarında ara
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

                    return Json(messages);
                }

                return Json(new List<object>());
            }
            catch (Exception ex)
            {
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUserGroups()
        {
            try
            {
                if (!User.Identity.IsAuthenticated)
                {
                    return Json(new List<object>());
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Json(new List<object>());
                }

                // Kullanıcının katıldığı grupları bul
                var userGroups = await _database.Groups
                    .Find(g => g.UserIds.Contains(userId))
                    .ToListAsync();

                var result = userGroups.Select(g => new
                {
                    id = g.Id.ToString(),
                    groupName = g.GroupName
                });

                return Json(result);
            }
            catch (Exception ex)
            {
                return Json(new List<object>());
            }
        }
    }
}