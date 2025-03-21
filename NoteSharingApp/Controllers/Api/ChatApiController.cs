using Microsoft.AspNetCore.Authorization;
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

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class ChatApiController : ControllerBase
{
    private readonly DatabaseContext _database;

    public ChatApiController(DatabaseContext database)
    {
        _database = database;
    }

    [HttpPost("add-chat")]
    public async Task<ActionResult> AddChat([FromBody] string userName)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!ObjectId.TryParse(userId, out var parsedOwnerId))
            return BadRequest("Geçersiz kullanıcı kimliği.");

        var currentUser = await _database.Users.Find(u => u.UserId == parsedOwnerId).FirstOrDefaultAsync();
        if (currentUser == null)
            return NotFound("Kullanıcı bulunamadı.");

        var targetUser = await _database.Users.Find(u => u.UserName == userName).FirstOrDefaultAsync();
        if (targetUser == null)
            return NotFound("Hedef kullanıcı bulunamadı.");

        var existingChat = await _database.Chats.Find(x => x.UsersId.Contains(currentUser.UserId) && x.UsersId.Contains(targetUser.UserId)).FirstOrDefaultAsync();
        if (existingChat == null)
        {
            var chat = new Chat
            {
                UsersId = new List<ObjectId> { currentUser.UserId, targetUser.UserId },
                SenderUsername = currentUser.UserName,
                ReceiverUsername = targetUser.UserName,
                CreatedAt = DateTime.UtcNow
            };
            await _database.Chats.InsertOneAsync(chat);
            existingChat = chat;
        }

        return Ok(new { chatId = existingChat.Id.ToString(), targetUsername = targetUser.UserName });
    }

    [HttpGet("search-users")]
    public async Task<ActionResult<IEnumerable<string>>> SearchUsers([FromQuery] string searchTerm)
    {
        if (string.IsNullOrEmpty(searchTerm)) return Ok(new List<string>());

        var users = await _database.Users
            .Find(u => u.UserName.ToLower().Contains(searchTerm.ToLower()))
            .Project(u => u.UserName)
            .Limit(5)
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost("upload-file")]
    public async Task<ActionResult> UploadChatFile([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Dosya seçilmedi.");

        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "chat-files");
        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        return Ok(new { fileName = file.FileName, fileUrl = $"/chat-files/{uniqueFileName}" });
    }

    [HttpGet("chat-view")]
    public async Task<ActionResult> GetChatView([FromQuery] string targetUsername)
    {
        var currentUsername = User.Identity.Name;
        var targetUser = await _database.Users.Find(u => u.UserName == targetUsername).FirstOrDefaultAsync();
        if (targetUser == null)
            return NotFound("Kullanıcı bulunamadı.");

        var chatHistory = await _database.Chats
            .Find(c => (c.SenderUsername == currentUsername && c.ReceiverUsername == targetUsername) ||
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

        return Ok(chatHistory);
    }

    [HttpPost("join-group")]
    public async Task<ActionResult> JoinGroup([FromBody] string groupId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!ObjectId.TryParse(userId, out var parsedUserId) || !ObjectId.TryParse(groupId, out var parsedGroupId))
            return BadRequest("Geçersiz kimlik.");

        var chat = await _database.Chats.Find(c => c.Id == parsedGroupId).FirstOrDefaultAsync();
        if (chat == null)
            return NotFound("Grup bulunamadı.");

        if (!chat.UsersId.Contains(parsedUserId))
        {
            chat.UsersId.Add(parsedUserId);
            await _database.Chats.ReplaceOneAsync(c => c.Id == chat.Id, chat);
        }
        return Ok("Gruba başarıyla katıldınız.");
    }

    [HttpGet("get-user-groups")]
    public async Task<ActionResult<IEnumerable<object>>> GetUserSchoolGroups()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out var userObjectId))
            return Unauthorized();

        var userGroups = await _database.SchoolGroups
            .Find(g => g.ParticipantIds.Contains(userObjectId))
            .ToListAsync();

        var result = userGroups.Select(g => new { id = g.Id.ToString(), groupName = g.GroupName });
        return Ok(result);
    }
}
