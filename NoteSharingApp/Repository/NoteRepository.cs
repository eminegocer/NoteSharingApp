using MongoDB.Driver;
using NoteSharingApp.Models;

namespace NoteSharingApp.Repository
{
    public class NoteRepository : GenericRepository<Note>
    {
        public NoteRepository(DatabaseContext context)
            : base(context, nameof(DatabaseContext.Notes)) { }
    }
}
