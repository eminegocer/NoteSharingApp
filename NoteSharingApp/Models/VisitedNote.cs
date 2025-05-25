using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

public class VisitedNote
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

    [BsonElement("VisitedAt")]
    [BsonRepresentation(BsonType.DateTime)]
    public DateTime VisitedAt { get; set; } = DateTime.UtcNow;
} 