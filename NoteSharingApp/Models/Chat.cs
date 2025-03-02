using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NoteSharingApp.Models
{
    public class Chat
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        [BsonElement("UsersId")]
        public List<ObjectId> UsersId { get; set; } // UserId'ler (Kiþisel sohbet için iki kiþi)

        public string SenderUsername { get; set; }
        public string ReceiverUsername { get; set; }
        [BsonElement("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("Messages")]
        public string Messages { get; set; } // Mesajlar
    }
}
