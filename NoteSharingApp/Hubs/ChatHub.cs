using Microsoft.AspNetCore.SignalR;

namespace NoteSharingApp.Hubs
{
    public class ChatHub : Hub
    {
        private static Dictionary<string, string> UserConnections = new Dictionary<string, string>();

        public async Task JoinChat(string username)
        {
            UserConnections[username] = Context.ConnectionId;
            System.Diagnostics.Debug.WriteLine($"User {username} joined chat with connection {Context.ConnectionId}");
        }

        public async Task SendMessage(string senderUsername, string receiverUsername, string message)
        {
            try 
            {
                System.Diagnostics.Debug.WriteLine($"Sending message from {senderUsername} to {receiverUsername}: {message}");
                
                // Göndericiye kendi mesajını gönder
                await Clients.Caller.SendAsync("ReceiveMessage", senderUsername, message);

                // Alıcıya mesajı gönder
                if (UserConnections.TryGetValue(receiverUsername, out string receiverConnectionId))
                {
                    await Clients.Client(receiverConnectionId).SendAsync("ReceiveMessage", senderUsername, message);
                    System.Diagnostics.Debug.WriteLine($"Message sent to receiver {receiverConnectionId}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Receiver {receiverUsername} not found in connections");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SendMessage: {ex.Message}");
                throw;
            }
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            var username = UserConnections.FirstOrDefault(x => x.Value == Context.ConnectionId).Key;
            if (username != null)
            {
                UserConnections.Remove(username);
            }
            return base.OnDisconnectedAsync(exception);
        }
    }
} 