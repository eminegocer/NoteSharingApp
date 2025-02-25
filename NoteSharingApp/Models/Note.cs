using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace NoteSharingApp.Models
{
    public class Note
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId NoteId{ get; set; }

        [BsonElement("Page")]
        public int Page { get; set; }

        [BsonElement("Owner")]
        public User Owner { get; set; }

        [BsonElement("Title")]
        [Required(ErrorMessage = "Not başlığı zorunludur.")]
        public string Title { get; set; }

        [BsonElement("Content")]
        [MinLength(75, ErrorMessage = "Not içeriği en az 75 karakter olmalıdır.")]
        public string Content { get; set; }

        [BsonElement("Category")]
        [Required(ErrorMessage = "Kategori alanı zorunludur.")]
        public string Category { get; set; }


        [BsonElement("PdfFilePath")]

        public string PdfFilePath { get; set; }

        [BsonRepresentation(BsonType.DateTime)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
