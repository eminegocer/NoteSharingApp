using MongoDB.Driver;
using Microsoft.Extensions.Configuration;
using NoteSharingApp.Models;

public class DatabaseContext
{
    private readonly IMongoDatabase _database;

    public DatabaseContext(IConfiguration config)
    {
        var client = new MongoClient(config.GetConnectionString("MongoDb"));
        _database = client.GetDatabase("NoteSharingApp");
    }

    public IMongoCollection<User> Users => _database.GetCollection<User>("Users");
}
