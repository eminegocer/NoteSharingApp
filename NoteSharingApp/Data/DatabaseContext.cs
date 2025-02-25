using MongoDB.Driver;
using Microsoft.Extensions.Configuration;
using NoteSharingApp.Models;
using Microsoft.EntityFrameworkCore;

public class DatabaseContext:DbContext
{
    private readonly IMongoDatabase _database;

    public DatabaseContext(IConfiguration config)
    {
        var client = new MongoClient(config.GetConnectionString("MongoDb"));
        _database = client.GetDatabase("NoteSharingApp");
    }

    public IMongoCollection<User> Users => _database.GetCollection<User>("Users");
    public IMongoCollection<Category> Categories => _database.GetCollection<Category>("Categories");
    public IMongoCollection<Note> Notes => _database.GetCollection<Note>("Notes");
}
