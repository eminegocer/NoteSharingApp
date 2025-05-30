﻿using MongoDB.Bson;
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


        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId OwnerId { get; set; }  // Kullanıcının ObjectId'si

        [BsonElement("OwnerUsername")]
        public string OwnerUsername { get; set; }  //


        [BsonElement("Title")]
        [Required(ErrorMessage = "Not başlığı zorunludur.")]
        public string Title { get; set; }

        [BsonElement("Content")]
        [MinLength(10, ErrorMessage = "Not içeriği en az 10 karakter olmalıdır.")]
        public string Content { get; set; }

        [BsonElement("Category")]
        [Required(ErrorMessage = "Kategori alanı zorunludur.")]
        public string Category { get; set; }


        [Required(ErrorMessage = "Lütfen bir PDF dosyası yükleyin.")]
        [BsonElement("PdfFilePath")]
        public string PdfFilePath { get; set; }


        [BsonRepresentation(BsonType.DateTime)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MongoDB.Bson.Serialization.Attributes.BsonElement("DownloadCount")]
        public int DownloadCount { get; set; } = 0;

        [BsonElement("LastViewedAt")]
        public DateTime LastViewedAt { get; set; } = DateTime.UtcNow;
    }
}
