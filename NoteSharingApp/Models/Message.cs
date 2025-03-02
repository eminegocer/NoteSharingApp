using MongoDB.Bson;

public class Message
{
    public ObjectId Id { get; set; }  // Mesajın benzersiz kimliği
    public string SenderUsername { get; set; }  // Gönderen kullanıcının adı
    public string Content { get; set; }  // Mesaj içeriği
    public DateTime CreatedAt { get; set; }  // Mesajın gönderilme zamanı

    public Message(string senderUsername, string content)
    {
        SenderUsername = senderUsername;
        Content = content;
        CreatedAt = DateTime.UtcNow;  // UTC zamanı kullanıyoruz
    }
}
