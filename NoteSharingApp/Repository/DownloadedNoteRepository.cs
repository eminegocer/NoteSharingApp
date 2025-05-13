using MongoDB.Driver;
using NoteSharingApp.Models;
using MongoDB.Bson;

namespace NoteSharingApp.Repository
{
    public class DownloadedNoteRepository : GenericRepository<DownloadedNote>
    {
        public DownloadedNoteRepository(DatabaseContext context)
            : base(context, nameof(DatabaseContext.DownloadedNotes)) { }

        public async Task<List<DownloadedNote>> GetUserDownloadedNotes(ObjectId userId)
        {
            var filter = Builders<DownloadedNote>.Filter.Eq(x => x.UserId, userId);
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<bool> HasUserDownloadedNote(ObjectId userId, ObjectId noteId)
        {
            var filter = Builders<DownloadedNote>.Filter.And(
                Builders<DownloadedNote>.Filter.Eq(x => x.UserId, userId),
                Builders<DownloadedNote>.Filter.Eq(x => x.NoteId, noteId)
            );
            return await _collection.Find(filter).AnyAsync();
        }
    }
} 