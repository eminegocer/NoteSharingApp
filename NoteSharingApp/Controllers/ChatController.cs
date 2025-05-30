using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
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
    // Sohbet işlemlerini yöneten controller sınıfı
    [Authorize]
    public class ChatController : Controller
    {
        private readonly DatabaseContext _database;
        private readonly DownloadedNoteRepository _downloadedNoteRepository;

        public ChatController(DatabaseContext database)
        {
            _database = database;
            _downloadedNoteRepository = new DownloadedNoteRepository(database);
        }

        // İki kullanıcı arasında yeni bir sohbet oluşturur veya mevcut sohbeti döndürür
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

        // Kullanıcı arama işlemini gerçekleştirir
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

        // İki kullanıcı arasında özel sohbet başlatır
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

        // Sohbet dosyası yükleme işlemini gerçekleştirir
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

                // Dosya yüklendiğinde alınan notlar kısmına eklenmesi için TrackDownload metodunu çağır
                var noteId = uniqueFileName; // Dosya adını noteId olarak kullanıyoruz
                var source = "chat"; // Kaynağı "chat" olarak belirtiyoruz
                await TrackDownload(noteId, source);

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

        // Belirli bir kullanıcı ile olan sohbet görünümünü döndürür
        public async Task<IActionResult> ChatView(string targetUsername)
        {
            try
            {
                var currentUsername = User.Identity.Name;

                // Hedef kullanıcının varlığını kontrol et (case-insensitive)
                var targetUser = await _database.Users.Find(u => u.UserName.ToLower() == targetUsername.ToLower()).FirstOrDefaultAsync();
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

        // Ana sayfa için sohbet kullanıcılarını getirir
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

        // Kullanıcıyı bir gruba ekler
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

        // Grup arama işlemini gerçekleştirir
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

            // Katılımcı kullanıcı adlarını çek
            var allUserIds = schoolGroups.SelectMany(g => g.ParticipantIds ?? new List<MongoDB.Bson.ObjectId>()).Distinct().ToList();
            var users = await _database.Users.Find(u => allUserIds.Contains(u.UserId)).ToListAsync();

            var result = schoolGroups.Select(g => new
            {
                id = g.Id.ToString(),
                groupName = g.GroupName,
                schoolName = g.SchoolName,
                departmentName = g.DepartmentName,
                description = g.Description,
                createdAt = g.CreatedAt,
                participantIds = g.ParticipantIds?.Select(x => x.ToString()).ToList(),
                participants = users.Where(u => (g.ParticipantIds ?? new List<MongoDB.Bson.ObjectId>()).Contains(u.UserId)).Select(u => u.UserName).ToList()
            });

            return Json(result);
        }

        public class GroupJoinRequest
        {
            public string GroupId { get; set; }
        }

        // Kullanıcıyı bir okul grubuna ekler
        [HttpPost]
        public async Task<IActionResult> JoinSchoolGroup([FromBody] GroupJoinRequest model)
        {
            try
            {
                var groupId = model.GroupId;
                // Kullanıcı kimliğini al
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var username = User.Identity.Name;

                if (string.IsNullOrEmpty(userId) || !MongoDB.Bson.ObjectId.TryParse(userId, out var userObjectId))
                {
                    return Json(new { success = false, message = "Geçersiz kullanıcı kimliği." });
                }

                if (string.IsNullOrEmpty(groupId) || !MongoDB.Bson.ObjectId.TryParse(groupId, out var groupObjectId))
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

        // Kullanıcının katıldığı okul gruplarını getirir
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

        // İki kullanıcı arasında özel mesaj gönderir
        [HttpPost]
        public async Task<IActionResult> SendPersonalMessage([FromBody] PersonalMessageDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.SenderUsername) || string.IsNullOrWhiteSpace(dto.TargetUsername) || string.IsNullOrWhiteSpace(dto.Content))
                return Json(new { success = false, message = "Eksik parametre." });

            var sender = await _database.Users.Find(u => u.UserName == dto.SenderUsername).FirstOrDefaultAsync();
            var receiver = await _database.Users.Find(u => u.UserName == dto.TargetUsername).FirstOrDefaultAsync();

            if (sender == null || receiver == null)
                return Json(new { success = false, message = "Kullanıcı bulunamadı." });

            var chat = await _database.Chats
                .Find(c => (c.SenderUsername == dto.SenderUsername && c.ReceiverUsername == dto.TargetUsername) ||
                           (c.SenderUsername == dto.TargetUsername && c.ReceiverUsername == dto.SenderUsername))
                .FirstOrDefaultAsync();

            if (chat == null)
            {
                chat = new Chat
                {
                    UsersId = new List<ObjectId> { sender.UserId, receiver.UserId },
                    SenderUsername = dto.SenderUsername,
                    ReceiverUsername = dto.TargetUsername,
                    Messages = new List<Message>(),
                    CreatedAt = DateTime.UtcNow
                };
                await _database.Chats.InsertOneAsync(chat);
            }

            var message = new Message
            {
                SenderUsername = dto.SenderUsername,
                Content = dto.Content,
                CreatedAt = DateTime.UtcNow
            };

            if (chat.Messages == null)
                chat.Messages = new List<Message>();
            chat.Messages.Add(message);

            await _database.Chats.ReplaceOneAsync(c => c.Id == chat.Id, chat);

            return Json(new { success = true, message = "Mesaj gönderildi." });
        }

        public class PersonalMessageDto
        {
            public string SenderUsername { get; set; }
            public string TargetUsername { get; set; }
            public string Content { get; set; }
        }

        // Grup mesajlarını getirir
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

        // Kullanıcının katıldığı grupları getirir
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

        // Grup dosyası yükleme işlemini gerçekleştirir
        [HttpPost]
        public async Task<IActionResult> UploadGroupFile(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Json(new { success = false, message = "Dosya seçilmedi." });
                }

                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "group-files");
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

                var fileUrl = $"/group-files/{uniqueFileName}";
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

        // Not indirme işlemini takip eder ve kaydeder
        [HttpPost]
        public async Task<IActionResult> TrackDownload(string noteId, string source)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Json(new { success = false, message = "Bu işlem için giriş yapmalısınız." });
            }

            if (!ObjectId.TryParse(noteId, out var parsedNoteId))
            {
                return Json(new { success = false, message = "Geçersiz not kimliği." });
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!ObjectId.TryParse(userId, out var parsedUserId))
            {
                return Json(new { success = false, message = "Geçersiz kullanıcı kimliği." });
            }

            // Check if user has already downloaded this note
            var hasDownloaded = await _downloadedNoteRepository.HasUserDownloadedNote(parsedUserId, parsedNoteId);
            if (!hasDownloaded)
            {
                // Notun detaylarını al
                var note = await _database.Notes.Find(n => n.NoteId == parsedNoteId).FirstOrDefaultAsync();
                if (note == null)
                {
                    return Json(new { success = false, message = "Not bulunamadı." });
                }

                // Create new download record with details
                var downloadedNote = new DownloadedNote
                {
                    UserId = parsedUserId,
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

                await _downloadedNoteRepository.AddAsync(downloadedNote);

                // Update user's received notes count
                var user = await _database.Users.Find(u => u.UserId == parsedUserId).FirstOrDefaultAsync();
                if (user != null)
                {
                    user.ReceivedNotesCount++;
                    await _database.Users.UpdateOneAsync(
                        u => u.UserId == parsedUserId,
                        Builders<User>.Update.Set(u => u.ReceivedNotesCount, user.ReceivedNotesCount)
                    );
                }
            }

            return Json(new { success = true });
        }

        // Grup mesajı gönderme işlemini gerçekleştirir
        [HttpPost]
        public async Task<IActionResult> SendGroupMessage([FromBody] GroupMessageRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.GroupId) || string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.SenderUsername))
                {
                    return Json(new { success = false, message = "Eksik parametre." });
                }
                // Önce normal grupta ara
                var group = await _database.Groups.Find(g => g.Id == request.GroupId).FirstOrDefaultAsync();
                if (group != null)
                {
                    var newMessage = new GroupMessage
                    {
                        SenderUsername = request.SenderUsername,
                        Content = request.Content,
                        SentAt = DateTime.UtcNow,
                        FileUrl = request.FileUrl,
                        FileName = null // Eğer dosya adı varsa ekle
                    };
                    if (group.Messages == null) group.Messages = new List<GroupMessage>();
                    group.Messages.Add(newMessage);
                    await _database.Groups.ReplaceOneAsync(g => g.Id == request.GroupId, group);
                    return Json(new { success = true, message = "Mesaj gönderildi." });
                }
                // Okul grubu ise
                if (ObjectId.TryParse(request.GroupId, out var groupObjectId))
                {
                    var schoolGroup = await _database.SchoolGroups.Find(g => g.Id == groupObjectId).FirstOrDefaultAsync();
                    if (schoolGroup != null)
                    {
                        var newMessage = new Message
                        {
                            SenderUsername = request.SenderUsername,
                            Content = request.Content,
                            CreatedAt = DateTime.UtcNow,
                            FileUrl = request.FileUrl
                        };
                        if (schoolGroup.Messages == null) schoolGroup.Messages = new List<Message>();
                        schoolGroup.Messages.Add(newMessage);
                        await _database.SchoolGroups.ReplaceOneAsync(g => g.Id == groupObjectId, schoolGroup);
                        return Json(new { success = true, message = "Mesaj gönderildi." });
                    }
                }
                return Json(new { success = false, message = "Grup bulunamadı." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public class GroupMessageRequest
        {
            public string GroupId { get; set; }
            public string SenderUsername { get; set; }
            public string Content { get; set; }
            public string FileUrl { get; set; }
        }

        // Ana sayfa görünümünü döndürür
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        // Yeni bir grup oluşturur
        public class GroupCreationModel
        {
            public string GroupName { get; set; }
            public List<string> Users { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] GroupCreationModel model)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Json(new { success = false, message = "Grup oluşturmak için giriş yapmalısınız." });
            }

            var currentUsername = User.Identity.Name;
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

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

                return Json(new
                {
                    success = true,
                    message = "Grup başarıyla oluşturuldu.",
                    groupId = group.Id.ToString(),
                    groupName = group.GroupName
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Grup oluşturulurken bir hata oluştu." });
            }
        }

        // Mevcut kullanıcı bilgilerini döndürür
        [HttpGet]
        public IActionResult CurrentUser()
        {
            if (!User.Identity.IsAuthenticated)
                return Unauthorized();

            var username = User.Identity.Name;
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            return Json(new
            {
                userName = username,
                userId = userId
            });
        }
    }
}