using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NoteSharingApp.Models
{
    public class DownloadedNote
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        [BsonElement("UserId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId UserId { get; set; }

        [BsonElement("NoteId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId NoteId { get; set; }

        [BsonElement("DownloadedAt")]
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("Source")]
        public string Source { get; set; } // "note_detail" or "chat"

        [BsonElement("NoteTitle")]
        public string NoteTitle { get; set; }

        [BsonElement("NoteOwnerId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId NoteOwnerId { get; set; }

        [BsonElement("NoteOwnerUsername")]
        public string NoteOwnerUsername { get; set; }

        [BsonElement("NoteCategory")]
        public string NoteCategory { get; set; }

        [BsonElement("NotePdfFilePath")]
        public string NotePdfFilePath { get; set; }

        [BsonElement("NotePage")]
        public int NotePage { get; set; }

        [BsonElement("NoteContent")]
        public string NoteContent { get; set; }
    }
} 