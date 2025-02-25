using MongoDB.Driver;
using NoteSharingApp.Models;

namespace NoteSharingApp.Repository
{
    public class CategoryRepository : GenericRepository<Category>
    {
        public CategoryRepository(DatabaseContext context) : base(context, "Categories")
        {
        }
    }
}
