using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace NoteSharingApp.Models
{
    public class VisitedNote
    {
        [BsonElement("NoteId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId NoteId { get; set; }

        [BsonElement("Title")]
        public string Title { get; set; }

        [BsonElement("VisitedAt")]
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime VisitedAt { get; set; } = DateTime.UtcNow;
    }
} 