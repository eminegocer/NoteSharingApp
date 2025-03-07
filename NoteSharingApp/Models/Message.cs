using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NoteSharingApp.Models
{
    public class Message
    {
        [BsonElement("Content")]
        public string Content { get; set; }

        [BsonElement("SenderUsername")]
        public string SenderUsername { get; set; }

        [BsonElement("CreatedAt")]
        public DateTime CreatedAt { get; set; }

        public Message()
        {
            CreatedAt = DateTime.UtcNow;
        }

        public Message(string senderUsername, string content)
        {
            SenderUsername = senderUsername;
            Content = content;
            CreatedAt = DateTime.UtcNow;
        }
    }
}
