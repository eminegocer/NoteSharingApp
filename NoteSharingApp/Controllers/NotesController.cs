using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NoteSharingApp.Models;
using NoteSharingApp.Repository;
using System.Threading.Tasks;

namespace NoteSharingApp.Controllers
{
    public class NotesController : Controller
    {
        private readonly DatabaseContext _database;

        public NotesController( DatabaseContext database)
        {
            _database = database;
        }

        public IActionResult HomePage()
        {
            return View();
        }

        public async Task<IActionResult> AddNote()
        {
            var categories = await _database.Categories.Find(_ => true).ToListAsync();
            ViewBag.Categories = categories; 
            return View();
        }


        [HttpPost]
        public IActionResult AddNote(Note note)
        {
            if(note != null)
            {
                if(ModelState.IsValid)
                {
                    var _note = new Note();
                    _note.Category = note.Category;
                    _note.Content = note.Content;
                    _note.CreatedAt = note.CreatedAt;
                    _note.Owner = note.Owner;
                    _note.Page = note.Page;
                    _note.PdfFilePath = note.PdfFilePath;
                    _note.Title = note.Title;

                    _database.Notes.InsertOneAsync(_note);
                }
                return View(note);
            }
            return View(note);
        }
    }
}
