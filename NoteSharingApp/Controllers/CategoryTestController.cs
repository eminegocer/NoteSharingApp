using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NoteSharingApp.Models;
using MongoDB.Driver;
using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace NoteSharingApp.Controllers
{
    // Kategori test işlemlerini yöneten controller
    [ApiController]
    [Route("api/[controller]")]
    public class CategoryTestController : ControllerBase
    {
        private readonly DatabaseContext _database;
        private readonly AnthropicService _anthropic;

        public CategoryTestController(DatabaseContext database, IConfiguration configuration)
        {
            _database = database;
            var useDummy = configuration.GetValue<bool>("Anthropic:UseDummy");
            var apiKey = configuration.GetValue<string>("Anthropic:ApiKey");
            _anthropic = new AnthropicService(apiKey, useDummy: useDummy);
        }

        // Kategori test işlemlerini yöneten controller
        [HttpPost("generate-questions")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GenerateTestQuestions()
        {
            try
            {
                var categories = await _database.Categories.Find(_ => true).ToListAsync();
                if (categories == null || !categories.Any())
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Error = "No categories found in database."
                    });
                }
                // Rastgele kategori seç (örneğin 5 kategori)
                var rnd = new Random();
                var selectedCategories = categories.OrderBy(x => rnd.Next()).Take(5).Select(c => c.CategoryName).ToList();
                // Anthropic API'ye tek seferde 10 soru isteği at
                var questions = await _anthropic.GenerateQuestionsWithExplanationsAsync(selectedCategories, 10);
                return Ok(new ApiResponse<List<CategoryQuestion>>
                {
                    Success = true,
                    Data = questions
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>
                {
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        // Kategori test sonuçlarını görüntüler
        [HttpPost("submit-test")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SubmitTest([FromBody] List<UserTestAnswerDto> answers)
        {
            if (answers == null || !answers.Any())
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Error = "Answers cannot be null or empty."
                });
            }

            try
            {
                var categoryStats = answers
                    .GroupBy(a => a.Category)
                    .Select(g => new CategoryStatsDto
                    {
                        Category = g.Key,
                        Total = g.Count(),
                        Correct = g.Count(x => !string.IsNullOrEmpty(x.UserAnswer) && x.UserAnswer == x.CorrectAnswer),
                        SuccessRate = g.Count(x => !string.IsNullOrEmpty(x.UserAnswer) && x.UserAnswer == x.CorrectAnswer) / (double)g.Count() * 100
                    })
                    .ToList();

                // En başarısız 3 kategoriye bak
                var weakCategories = categoryStats.OrderBy(x => x.SuccessRate).Take(3).Select(x => x.Category).ToList();

                // Her kategoriden 3 not çek
                var recommendedNotes = new List<Note>();
                foreach (var category in weakCategories)
                {
                    var notesInCategory = await _database.Notes
                        .Find(n => n.Category == category)
                        .Limit(3)
                        .ToListAsync();

                    recommendedNotes.AddRange(notesInCategory);
                }

                // Toplamda 9'dan fazla not varsa, ilk 9 tanesini al
                recommendedNotes = recommendedNotes.Take(9).ToList();

                var recommendedNotesDto = recommendedNotes.Select(n => new NoteDto
                {
                    NoteId = n.NoteId.ToString(),
                    Title = n.Title,
                    Content = n.Content,
                    Category = n.Category,
                    OwnerUsername = n.OwnerUsername,
                    Page = n.Page,
                    CreatedAt = n.CreatedAt,
                    PdfFilePath = n.PdfFilePath
                }).ToList();

                return Ok(new ApiResponse<SubmitTestResponseDto>
                {
                    Success = true,
                    Data = new SubmitTestResponseDto
                    {
                        CategoryStats = categoryStats,
                        RecommendedNotes = recommendedNotesDto
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>
                {
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        // Kategori test sonuçlarını kaydeder
        [HttpPost("explain-answer")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExplainAnswer([FromBody] ExplainAnswerRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Error = "Invalid request data."
                });
            }

            try
            {
                var explanation = await _anthropic.ExplainAnswerAsync(request.Question, request.CorrectAnswer);
                return Ok(new ApiResponse<ExplainAnswerResponseDto>
                {
                    Success = true,
                    Data = new ExplainAnswerResponseDto { Explanation = explanation }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>
                {
                    Success = false,
                    Error = ex.Message
                });
            }
        }

       
    }

    // DTOs and Response Models
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string Error { get; set; }
    }

    public class QuestionResponseDto
    {
        public string Category { get; set; }
        public string QuestionJson { get; set; }
    }

    public class UserTestAnswerDto
    {
        [Required]
        public string Category { get; set; }
        [Required]
        public string Question { get; set; }
        // [Required] kaldırıldı, nullable yapıldı
        public string? UserAnswer { get; set; }
        [Required]
        public string CorrectAnswer { get; set; }
    }

    public class CategoryStatsDto
    {
        public string Category { get; set; }
        public int Total { get; set; }
        public int Correct { get; set; }
        public double SuccessRate { get; set; }
    }

    public class NoteDto
    {
        public string NoteId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Category { get; set; }
        public string OwnerUsername { get; set; }
        public int Page { get; set; }
        public DateTime CreatedAt { get; set; }
        public string PdfFilePath { get; set; }
    }

    public class SubmitTestResponseDto
    {
        public List<CategoryStatsDto> CategoryStats { get; set; }
        public List<NoteDto> RecommendedNotes { get; set; }
    }

    public class ExplainAnswerRequestDto
    {
        [Required]
        public string Question { get; set; }
        [Required]
        public string CorrectAnswer { get; set; }
    }

    public class ExplainAnswerResponseDto
    {
        public string Explanation { get; set; }
    }
}