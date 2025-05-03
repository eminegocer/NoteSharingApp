using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace NoteSharingApp.Models
{
    public class Group
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        public string GroupName { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public List<string> UserIds { get; set; } = new List<string>();

        public string CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public bool IsActive { get; set; } = true;

        public string Description { get; set; }

        public List<GroupMessage> Messages { get; set; } = new List<GroupMessage>();
    }

    public class GroupMessage
    {
        public string SenderId { get; set; }

        public string SenderUsername { get; set; }

        public string Content { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public string FileUrl { get; set; }

        public string FileName { get; set; }
    }
} 