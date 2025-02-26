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
            var notes = _database.Notes
                .Find(x => true)
                .SortByDescending(x => x.CreatedAt)
                .ToList();
            return View(notes);
        }

        public async Task<IActionResult> AddNote()
        {
            ViewBag.UserName = User.Identity.Name;

            var categories = await _database.Categories.Find(_ => true).ToListAsync();
            ViewBag.Categories = categories; 
            return View();
        }


        [HttpPost]
        public async Task<IActionResult> AddNote(Note note, IFormFile PdfFile)
        {
            var categories = await _database.Categories.Find(_ => true).ToListAsync();
            ViewBag.Categories = categories;

            // PDF dosyası kontrolü (formdan gelen dosya)
            if (PdfFile == null || PdfFile.Length == 0)
            {
                ModelState.AddModelError("PdfFile", "Lütfen bir PDF dosyası seçin.");
                return View(note);
            }

            ModelState.Remove("PdfFilePath");

            // Modelin kalan alanlarını doğrula
            if (!ModelState.IsValid)
            {
                return View(note);
            }

            // Dosya yükleme işlemi
            try
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(PdfFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await PdfFile.CopyToAsync(stream);
                }

                // PDF yolunu modele ata
                note.PdfFilePath = "/uploads/" + fileName;
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Dosya yüklenirken bir hata oluştu: " + ex.Message);
                return View(note);
            }

            // Veritabanına kaydet
            try
            {
                var notesCollection = _database.Notes;
                await notesCollection.InsertOneAsync(note);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Veritabanına kaydedilirken bir hata oluştu: " + ex.Message);
                return View(note);
            }

            return RedirectToAction("HomePage");
        }



    }
}
