using Microsoft.AspNetCore.SignalR;
using NoteSharingApp.Models;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace NoteSharingApp.Hubs
{
    public class ChatHub : Hub
    {
        private static Dictionary<string, string> UserConnections = new Dictionary<string, string>();
        private readonly DatabaseContext _database;

        public ChatHub(DatabaseContext database)
        {
            _database = database;
        }

        public async Task JoinChat(string username)
        {
            UserConnections[username] = Context.ConnectionId;
            System.Diagnostics.Debug.WriteLine($"User {username} joined chat with connection {Context.ConnectionId}");
        }

        public async Task SendMessage(string senderUsername, string receiverUsername, string message, string chatType = "personal")
        {
            try
            {
                if (string.IsNullOrEmpty(senderUsername) || string.IsNullOrEmpty(receiverUsername))
                {
                    throw new Exception("Gönderen veya alıcı kullanıcı adı boş olamaz");
                }

                System.Diagnostics.Debug.WriteLine($"SendMessage called: Sender={senderUsername}, Receiver={receiverUsername}, Type={chatType}, Message={message}");

                if (chatType == "personal")
                {
                    // Personal chat message handling
                    var sender = await _database.Users.Find(u => u.UserName == senderUsername).FirstOrDefaultAsync();
                    var receiver = await _database.Users.Find(u => u.UserName == receiverUsername).FirstOrDefaultAsync();

                    if (sender == null)
                    {
                        throw new Exception($"Gönderen kullanıcı bulunamadı: {senderUsername}");
                    }
                    if (receiver == null)
                    {
                        throw new Exception($"Alıcı kullanıcı bulunamadı: {receiverUsername}");
                    }

                    // Find or create chat
                    var chat = await _database.Chats
                        .Find(c => 
                            (c.SenderUsername == senderUsername && c.ReceiverUsername == receiverUsername) ||
                            (c.SenderUsername == receiverUsername && c.ReceiverUsername == senderUsername))
                        .FirstOrDefaultAsync();

                    if (chat == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Creating new chat between {senderUsername} and {receiverUsername}");
                        chat = new Chat
                        {
                            UsersId = new List<ObjectId> { sender.UserId, receiver.UserId },
                            SenderUsername = senderUsername,
                            ReceiverUsername = receiverUsername,
                            Messages = new List<Message>(),
                            CreatedAt = DateTime.UtcNow
                        };
                        await _database.Chats.InsertOneAsync(chat);
                        System.Diagnostics.Debug.WriteLine($"New chat created with ID: {chat.Id}");
                    }

                    // Create new message
                    var newMessage = new Message
                    {
                        SenderUsername = senderUsername,
                        Content = message,
                        CreatedAt = DateTime.UtcNow
                    };

                    System.Diagnostics.Debug.WriteLine($"Creating new message: {message} from {senderUsername}");

                    // Update chat with new message
                    var update = Builders<Chat>.Update.Push(c => c.Messages, newMessage);
                    var result = await _database.Chats.UpdateOneAsync(
                        c => c.Id == chat.Id,
                        update
                    );

                    if (result.ModifiedCount == 0)
                    {
                        throw new Exception($"Mesaj veritabanına kaydedilemedi. ChatId: {chat.Id}");
                    }

                    System.Diagnostics.Debug.WriteLine($"Message saved to database. ChatId={chat.Id}, MessageId={newMessage.Id}");

                    // Send message to sender
                    await Clients.Caller.SendAsync("ReceiveMessage", senderUsername, message);
                    System.Diagnostics.Debug.WriteLine($"Message sent to sender: {senderUsername}");

                    // Send message to receiver if online
                    if (UserConnections.TryGetValue(receiverUsername, out string receiverConnectionId))
                    {
                        await Clients.Client(receiverConnectionId).SendAsync("ReceiveMessage", senderUsername, message);
                        System.Diagnostics.Debug.WriteLine($"Message sent to receiver: {receiverUsername}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Receiver {receiverUsername} is not online");
                    }
                }
                else if (chatType == "group")
                {
                    // Group chat message handling
                    if (!ObjectId.TryParse(receiverUsername, out var groupId))
                    {
                        throw new Exception("Geçersiz grup kimliği");
                    }

                    // Try normal groups first
                    var group = await _database.Groups
                        .Find(g => g.Id == groupId.ToString())
                        .FirstOrDefaultAsync();

                    if (group != null)
                    {
                        var newMessage = new GroupMessage
                        {
                            SenderId = Context.User.FindFirst("sub")?.Value,
                            SenderUsername = senderUsername,
                            Content = message,
                            SentAt = DateTime.UtcNow
                        };

                        var update = Builders<Group>.Update.Push(g => g.Messages, newMessage);
                        var result = await _database.Groups.UpdateOneAsync(
                            g => g.Id == groupId.ToString(),
                            update
                        );

                        if (result.ModifiedCount == 0)
                        {
                            throw new Exception("Mesaj kaydedilemedi");
                        }

                        await Clients.Group(groupId.ToString()).SendAsync("ReceiveGroupMessage", groupId.ToString(), senderUsername, message);
                        return;
                    }

                    // Try school groups if not found in normal groups
                    var schoolGroup = await _database.SchoolGroups
                        .Find(g => g.Id == groupId)
                        .FirstOrDefaultAsync();

                    if (schoolGroup != null)
                    {
                        var newMessage = new Message
                        {
                            SenderUsername = senderUsername,
                            Content = message,
                            CreatedAt = DateTime.UtcNow
                        };

                        var update = Builders<SchoolGroup>.Update.Push(g => g.Messages, newMessage);
                        var result = await _database.SchoolGroups.UpdateOneAsync(
                            g => g.Id == groupId,
                            update
                        );

                        if (result.ModifiedCount == 0)
                        {
                            throw new Exception("Mesaj kaydedilemedi");
                        }

                        await Clients.Group(groupId.ToString()).SendAsync("ReceiveGroupMessage", groupId.ToString(), senderUsername, message);
                        return;
                    }

                    throw new Exception("Grup bulunamadı");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendMessage Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"Sender: {senderUsername}, Receiver: {receiverUsername}, Type: {chatType}");
                throw;
            }
        }

        public async Task SendFile(string senderUsername, string receiverUsername, string fileName, string fileUrl)
        {
            try
            {
                var sender = await _database.Users.Find(u => u.UserName == senderUsername).FirstOrDefaultAsync();
                var receiver = await _database.Users.Find(u => u.UserName == receiverUsername).FirstOrDefaultAsync();

                if (sender == null || receiver == null)
                {
                    throw new Exception("Kullanıcı bulunamadı");
                }

                var chat = await _database.Chats
                    .Find(c => 
                        (c.SenderUsername == senderUsername && c.ReceiverUsername == receiverUsername) ||
                        (c.SenderUsername == receiverUsername && c.ReceiverUsername == senderUsername))
                    .FirstOrDefaultAsync();

                if (chat == null)
                {
                    chat = new Chat
                    {
                        UsersId = new List<ObjectId> { sender.UserId, receiver.UserId },
                        SenderUsername = senderUsername,
                        ReceiverUsername = receiverUsername,
                        Messages = new List<Message>(),
                        CreatedAt = DateTime.UtcNow
                    };
                    await _database.Chats.InsertOneAsync(chat);
                }

                var newMessage = new Message
                {
                    SenderUsername = senderUsername,
                    Content = fileName,
                    FileUrl = fileUrl,
                    IsFile = true,
                    CreatedAt = DateTime.UtcNow
                };

                var update = Builders<Chat>.Update.Push(c => c.Messages, newMessage);
                var result = await _database.Chats.UpdateOneAsync(
                    c => c.Id == chat.Id,
                    update
                );

                if (result.ModifiedCount == 0)
                {
                    throw new Exception("Dosya mesajı veritabanına kaydedilemedi");
                }

                // Send to sender
                await Clients.Caller.SendAsync("ReceiveFile", senderUsername, fileName, fileUrl);

                // Send to receiver if online
                if (UserConnections.TryGetValue(receiverUsername, out string receiverConnectionId))
                {
                    await Clients.Client(receiverConnectionId).SendAsync("ReceiveFile", senderUsername, fileName, fileUrl);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendFile Error: {ex.Message}");
                throw;
            }
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            var username = UserConnections.FirstOrDefault(x => x.Value == Context.ConnectionId).Key;
            if (username != null)
            {
                UserConnections.Remove(username);
                System.Diagnostics.Debug.WriteLine($"User {username} disconnected");
            }
            return base.OnDisconnectedAsync(exception);
        }

        // YENİ: Gruba katılma metodu
        public async Task JoinGroup(string groupId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
            System.Diagnostics.Debug.WriteLine($"User {Context.User.Identity.Name} joined group {groupId}");
        }

        // YENİ: Gruptan ayrılma metodu
        public async Task LeaveGroup(string groupId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
            System.Diagnostics.Debug.WriteLine($"User {Context.User.Identity.Name} left group {groupId}");
        }

        // Grup mesajı gönderme metodu
        public async Task SendGroupMessage(string groupId, string message)
        {
            try
            {
                var senderUsername = Context.User.Identity.Name;
                if (string.IsNullOrEmpty(senderUsername))
                {
                    throw new Exception("Kullanıcı kimliği bulunamadı");
                }

                // Normal grup koleksiyonunda ara
                var group = await _database.Groups
                    .Find(g => g.Id == groupId)
                    .FirstOrDefaultAsync();

                if (group != null)
                {
                    // Grup mesajı oluştur
                    var newMessage = new GroupMessage
                    {
                        SenderId = Context.User.FindFirst("sub")?.Value,
                        SenderUsername = senderUsername,
                        Content = message,
                        SentAt = DateTime.UtcNow
                    };

                    // Mesajı gruba ekle
                    var update = Builders<Group>.Update.Push(g => g.Messages, newMessage);
                    var result = await _database.Groups.UpdateOneAsync(
                        g => g.Id == groupId,
                        update
                    );

                    if (result.ModifiedCount == 0)
                    {
                        throw new Exception("Mesaj kaydedilemedi");
                    }

                    // Mesajı gruptaki herkese gönder
                    await Clients.Group(groupId).SendAsync("ReceiveGroupMessage", groupId, senderUsername, message);
                    return;
                }

                // Eğer normal grupta bulunamazsa, okul grubunda ara
                var schoolGroup = await _database.SchoolGroups
                    .Find(g => g.Id == ObjectId.Parse(groupId))
                    .FirstOrDefaultAsync();

                if (schoolGroup != null)
                {
                    // Okul grubu için mesaj oluştur
                    var newMessage = new Message
                    {
                        SenderUsername = senderUsername,
                        Content = message,
                        CreatedAt = DateTime.UtcNow
                    };

                    // Mesajı SchoolGroup'a ekle
                    var update = Builders<SchoolGroup>.Update.Push(g => g.Messages, newMessage);
                    var result = await _database.SchoolGroups.UpdateOneAsync(
                        g => g.Id == schoolGroup.Id,
                        update
                    );

                    if (result.ModifiedCount == 0)
                    {
                        throw new Exception("Mesaj kaydedilemedi");
                    }

                    // Mesajı gruptaki herkese gönder
                    await Clients.Group(groupId).SendAsync("ReceiveGroupMessage", groupId, senderUsername, message);
                    return;
                }

                throw new Exception("Grup bulunamadı");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendGroupMessage Error: {ex.Message}");
                throw;
            }
        }

        public async Task SendGroupFile(string groupId, string fileName, string fileUrl)
        {
            try
            {
                var senderUsername = Context.User.Identity.Name;
                if (string.IsNullOrEmpty(senderUsername))
                {
                    throw new Exception("Kullanıcı kimliği bulunamadı");
                }

                // Normal grup koleksiyonunda ara
                var group = await _database.Groups
                    .Find(g => g.Id == groupId)
                    .FirstOrDefaultAsync();

                if (group != null)
                {
                    var newMessage = new GroupMessage
                    {
                        SenderId = Context.User.FindFirst("sub")?.Value,
                        SenderUsername = senderUsername,
                        Content = fileName,
                        FileUrl = fileUrl,
                        FileName = fileName,
                        SentAt = DateTime.UtcNow
                    };
                    var update = Builders<Group>.Update.Push(g => g.Messages, newMessage);
                    var result = await _database.Groups.UpdateOneAsync(
                        g => g.Id == groupId,
                        update
                    );
                    if (result.ModifiedCount == 0)
                    {
                        throw new Exception("Dosya mesajı kaydedilemedi");
                    }
                    await Clients.Group(groupId).SendAsync("ReceiveGroupFile", groupId, senderUsername, fileName, fileUrl);
                    return;
                }

                // Eğer normal grupta bulunamazsa, okul grubunda ara
                var schoolGroup = await _database.SchoolGroups
                    .Find(g => g.Id == ObjectId.Parse(groupId))
                    .FirstOrDefaultAsync();

                if (schoolGroup != null)
                {
                    var newMessage = new Message
                    {
                        SenderUsername = senderUsername,
                        Content = fileName,
                        FileUrl = fileUrl,
                        IsFile = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    if (schoolGroup.Messages == null)
                        schoolGroup.Messages = new List<Message>();
                    schoolGroup.Messages.Add(newMessage);
                    var update = Builders<SchoolGroup>.Update.Set(g => g.Messages, schoolGroup.Messages);
                    var result = await _database.SchoolGroups.UpdateOneAsync(
                        g => g.Id == schoolGroup.Id,
                        update
                    );
                    if (result.ModifiedCount == 0)
                    {
                        throw new Exception("Dosya mesajı kaydedilemedi");
                    }
                    await Clients.Group(groupId).SendAsync("ReceiveGroupFile", groupId, senderUsername, fileName, fileUrl);
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendGroupFile Error: {ex.Message}");
                throw;
            }
        }
    }
}