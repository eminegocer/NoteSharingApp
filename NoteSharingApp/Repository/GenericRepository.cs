using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

public class GenericRepository<T> where T : class
{
    protected readonly IMongoCollection<T> _collection;

    public GenericRepository(DatabaseContext context, string collectionName)
    {
        var property = context.GetType().GetProperty(collectionName);
        if (property == null)
        {
            throw new ArgumentException($"'{collectionName}' adlı koleksiyon DatabaseContext içinde bulunamadı.");
        }

        _collection = property.GetValue(context) as IMongoCollection<T>;

        if (_collection == null)
        {
            throw new ArgumentException($"'{collectionName}' koleksiyonu geçerli bir IMongoCollection<T> değil.");
        }
    }

    public async Task AddAsync(T entity)
    {
        await _collection.InsertOneAsync(entity);
    }

    public async Task RemoveAsync(FilterDefinition<T> filter)
    {
        await _collection.DeleteOneAsync(filter);
    }

    public async Task<List<T>> GetAllAsync()
    {
        return await _collection.Find(_ => true).ToListAsync();
    }

    public async Task<T> GetByIdAsync(FilterDefinition<T> filter)
    {
        return await _collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task UpdateAsync(FilterDefinition<T> filter, UpdateDefinition<T> update)
    {
        await _collection.UpdateOneAsync(filter, update);
    }
}
