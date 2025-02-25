using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NoteSharingApp.Models
{
    public class Category
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId CategoryId{ get; set; }

        [BsonElement("CategoryName")]
        public string CategoryName { get; set; }
    }
}
