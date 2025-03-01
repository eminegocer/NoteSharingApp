using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace NoteSharingApp.Models
{
    public class Chat
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        public string SenderUsername { get; set; }
        public string ReceiverUsername { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
