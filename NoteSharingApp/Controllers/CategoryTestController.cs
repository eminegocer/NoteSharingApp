using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteSharingApp.Models;
using MongoDB.Driver;
using Microsoft.Extensions.Configuration;

public class CategoryTestController : Controller
{
    private readonly DatabaseContext _database;
    private readonly AnthropicService _anthropic;

    public CategoryTestController(DatabaseContext database, IConfiguration configuration)
    {
        _database = database;
        try
        {
            var apiKey = configuration["Anthropic:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                // API key bulunamazsa dummy moda geç
                _anthropic = new AnthropicService("dummy", true);
            }
            else
            {
                _anthropic = new AnthropicService(apiKey, false);
            }
        }
        catch (Exception ex)
        {
            // Hata durumunda dummy moda geç
            _anthropic = new AnthropicService("dummy", true);
        }
    }

    [HttpPost]
    public async Task<IActionResult> GenerateTestQuestions()
    {
        try
        {
            var categories = await _database.Categories.Find(_ => true).ToListAsync();
            if (categories == null || categories.Count == 0)
                return StatusCode(500, new { error = "No categories found in database." });
            var rnd = new System.Random();
            var tasks = new List<Task<(string Category, string QuestionJson)>>();
            for (int i = 0; i < 3; i++)
            {
                var cat = categories[rnd.Next(categories.Count)];
                var task = _anthropic.GenerateQuestionAsync(cat.CategoryName)
                    .ContinueWith(t => (cat.CategoryName, t.Result));
                tasks.Add(task);
            }
            var results = await Task.WhenAll(tasks);
            var questions = results.Select(r => new { Category = r.Category, QuestionJson = r.QuestionJson }).ToList();
            // Hata kontrolü
            foreach (var q in questions)
            {
                if (q.QuestionJson.StartsWith("Exception") || q.QuestionJson.StartsWith("API Error") || q.QuestionJson.StartsWith("API response format error"))
                {
                    return StatusCode(500, new { error = q.QuestionJson });
                }
            }
            return Json(questions);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SubmitTest([FromBody] List<UserTestAnswer> answers)
    {
        try
        {
            var categoryStats = answers
                .GroupBy(a => a.Category)
                .Select(g => new {
                    Category = g.Key,
                    Total = g.Count(),
                    Correct = g.Count(x => x.UserAnswer == x.CorrectAnswer),
                    SuccessRate = (double)g.Count(x => x.UserAnswer == x.CorrectAnswer) / g.Count() * 100
                })
                .ToList();
            var weakCategories = categoryStats.OrderBy(x => x.SuccessRate).Take(2).Select(x => x.Category).ToList();
            var filter = Builders<Note>.Filter.In(n => n.Category, weakCategories);
            var recommendedNotes = await _database.Notes.Find(filter).Limit(9).ToListAsync();
            
            // NoteId'yi string olarak dön
            var recommendedNotesDto = recommendedNotes.Select(n => new {
                NoteId = n.NoteId.ToString(),
                Title = n.Title,
                Content = n.Content,
                Category = n.Category,
                OwnerUsername = n.OwnerUsername,
                Page = n.Page,
                CreatedAt = n.CreatedAt,
                PdfFilePath = n.PdfFilePath
            }).ToList();

            return Json(new { categoryStats, recommendedNotes = recommendedNotesDto });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ExplainAnswer([FromBody] ExplainAnswerRequest req)
    {
        try
        {
            var explanation = await _anthropic.ExplainAnswerAsync(req.Question, req.CorrectAnswer);
            return Json(new { explanation });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
        }
    }

    public class ExplainAnswerRequest
    {
        public string Question { get; set; }
        public string CorrectAnswer { get; set; }
    }
}

public class UserTestAnswer
{
    public string Category { get; set; }
    public string Question { get; set; }
    public string UserAnswer { get; set; }
    public string CorrectAnswer { get; set; }
} 