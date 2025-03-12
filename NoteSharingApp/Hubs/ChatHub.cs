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

        public async Task SendMessage(string senderUsername, string receiverUsername, string message)
        {
            try 
            {
                // Önce kullanıcıları kontrol et
                var sender = await _database.Users.Find(u => u.UserName == senderUsername).FirstOrDefaultAsync();
                var receiver = await _database.Users.Find(u => u.UserName == receiverUsername).FirstOrDefaultAsync();

                if (sender == null)
                {
                    throw new Exception($"Gönderen kullanıcı ({senderUsername}) bulunamadı");
                }

                if (receiver == null)
                {
                    throw new Exception($"Alıcı kullanıcı ({receiverUsername}) bulunamadı");
                }

                // Find existing chat or create new one
                var chat = await _database.Chats
                    .Find(c => 
                        (c.SenderUsername == senderUsername && c.ReceiverUsername == receiverUsername) ||
                        (c.SenderUsername == receiverUsername && c.ReceiverUsername == senderUsername))
                    .FirstOrDefaultAsync();

                if (chat == null)
                {
                    // Create new chat
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

                // Create new message
                var newMessage = new Message
                {
                    SenderUsername = senderUsername,
                    Content = message,
                    CreatedAt = DateTime.UtcNow
                };

                // Add message to chat
                if (chat.Messages == null)
                {
                    chat.Messages = new List<Message>();
                }

                chat.Messages.Add(newMessage);

                // Update chat in database
                var filter = Builders<Chat>.Filter.Eq(c => c.Id, chat.Id);
                var update = Builders<Chat>.Update.Set(c => c.Messages, chat.Messages);
                var result = await _database.Chats.UpdateOneAsync(filter, update);

                if (result.ModifiedCount == 0)
                {
                    throw new Exception("Mesaj veritabanına kaydedilemedi");
                }

                // Send message to sender
                await Clients.Caller.SendAsync("ReceiveMessage", senderUsername, message);

                // Send message to receiver if online
                if (UserConnections.TryGetValue(receiverUsername, out string receiverConnectionId))
                {
                    await Clients.Client(receiverConnectionId).SendAsync("ReceiveMessage", senderUsername, message);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendMessage Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Sender: {senderUsername}, Receiver: {receiverUsername}");
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
                    Content = $"[Dosya] {fileName}",
                    FileUrl = fileUrl,
                    IsFile = true,
                    CreatedAt = DateTime.UtcNow
                };

                if (chat.Messages == null)
                {
                    chat.Messages = new List<Message>();
                }

                chat.Messages.Add(newMessage);

                var filter = Builders<Chat>.Filter.Eq(c => c.Id, chat.Id);
                var update = Builders<Chat>.Update.Set(c => c.Messages, chat.Messages);
                var result = await _database.Chats.UpdateOneAsync(filter, update);

                if (result.ModifiedCount == 0)
                {
                    throw new Exception("Dosya mesajı veritabanına kaydedilemedi");
                }

                // Send file message to sender
                await Clients.Caller.SendAsync("ReceiveFile", senderUsername, fileName, fileUrl);

                // Send file message to receiver if online
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
    }
}