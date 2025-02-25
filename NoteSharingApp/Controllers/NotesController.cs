using Microsoft.AspNetCore.Mvc;

namespace NoteSharingApp.Controllers
{
    public class NotesController :Controller
    {
        public IActionResult HomePage()
        {
            return View();
        }
    }
}
