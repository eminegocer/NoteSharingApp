using Microsoft.AspNetCore.Identity;
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
            var notes = _database.Notes.Find(x => true).ToList();
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
            // Kategorileri her durumda yükle
            var categories = await _database.Categories.Find(_ => true).ToListAsync();
            ViewBag.Categories = categories;

            // Giriş yapmış kullanıcının adını al
            note.User = User.Identity.Name;

            // PDF dosyası kontrolü
            if (PdfFile == null || PdfFile.Length == 0)
            {
                ModelState.AddModelError("PdfFile", "PDF dosyası yüklemek zorunludur.");
                return View(note);
            }

            try
            {
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                
                // Uploads klasörünü oluştur
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Benzersiz dosya adı oluştur
                string uniqueFileName = $"{Guid.NewGuid()}_{PdfFile.FileName}";
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                // Dosyayı kaydet
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await PdfFile.CopyToAsync(fileStream);
                }

                // Veritabanında saklanacak URL yolunu ayarla
                note.PdfFilePath = $"/uploads/{uniqueFileName}";
                note.CreatedAt = DateTime.UtcNow;

                // Notu veritabanına kaydet
                await _database.Notes.InsertOneAsync(note);

                TempData["SuccessMessage"] = "Not başarıyla kaydedildi!";
                return RedirectToAction("HomePage");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Not kaydedilirken bir hata oluştu: " + ex.Message);
                return View(note);
            }
        }



    }
}
