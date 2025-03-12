using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace NoteSharingApp.Models
{
    public class SchoolGroup
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        [BsonElement("GroupName")]
        public string GroupName { get; set; }

        [BsonElement("SchoolName")]
        public string  SchoolName { get; set; }

        [BsonElement("DepartmentName")]
        public string DepartmentName { get; set; }

        [BsonElement("Description")]
        public string Description { get; set; }

        [BsonElement("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [BsonElement("Participants")]
        public List<ObjectId> ParticipantIds { get; set; } = new List<ObjectId>();
    }
} 