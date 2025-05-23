using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
namespace NoteSharingApp.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId UserId { get; set; }

        [BsonElement("UserName")]
        [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
        [StringLength(50, ErrorMessage = "Kullanıcı adı en fazla 50 karakter olabilir.")]
        public string UserName { get; set; }

        [BsonElement("Email")]
        [Required(ErrorMessage = "E-posta adresi zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
        public string Email { get; set; }

        [BsonElement("Password")] //MongoDb uzerınde saklanacak ısım
        [Required(ErrorMessage = "Şifre zorunludur.")]
        [MinLength(6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
        public string Password { get; set; }

        [BsonElement("SchoolName")]
        [StringLength(100, ErrorMessage = "Okul adı en fazla 100 karakter olabilir.")]
        public string? SchoolName { get; set; }

        [BsonElement("ProfilePicture")]
        public string? ProfilePicture { get; set; }

        [BsonElement("Bio")]
        [StringLength(500, ErrorMessage = "Biyografi en fazla 500 karakter olabilir.")]
        public string? Bio { get; set; }

        [BsonElement("Department")]
        [StringLength(100, ErrorMessage = "Bölüm adı en fazla 100 karakter olabilir.")]
        public string? Department { get; set; }

        [BsonElement("Year")]
        public int? Year { get; set; }

        [BsonElement("SharedNotesCount")]
        public int SharedNotesCount { get; set; } = 0;

        [BsonElement("ReceivedNotesCount")]
        public int ReceivedNotesCount { get; set; } = 0;

        [BsonElement("LastLogin")]
        [BsonRepresentation(BsonType.DateTime)]
        public DateTime? LastLogin { get; set; }

        [BsonElement("ReceivedNotes")]
        public List<ObjectId> ReceivedNotes { get; set; } = new List<ObjectId>(); // Yeni alan

        [BsonElement("SharedNotes")] // Yeni eklenen alan
        public List<ObjectId> SharedNotes { get; set; } = new List<ObjectId>(); // Kullanıcının paylaştığı notların ID'leri

    }

}
