using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig;
using System.Text;
using System.Text.Json;


namespace NoteSharingApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PDFSummaryController : ControllerBase
    {
        private readonly string anthropicApiKey = "";//""; // Buraya kendi Anthropic API anahtarınızı ekleyin

       [HttpGet("{fileName}")]
        public async Task<IActionResult> GetSummary(string fileName)
        {
            try
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", fileName);

                if (!System.IO.File.Exists(filePath))
                    return NotFound("PDF not found.");

                string text = "";
                using (var pdf = PdfDocument.Open(filePath))
                {
                    foreach (var page in pdf.GetPages())
                    {
                        text += page.Text + " ";
                    }
                }

                // Metni temizle ve kısalt (Claude için token limiti)
                text = text.Replace("\n", " ").Replace("\r", " ").Replace("  ", " ").Trim();
                string inputText = text.Length > 3500 ? text.Substring(0, 3500) : text;

                var summary = await SummarizeWithAnthropic(inputText);

                return Ok(new { summary });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
            }
        }

        private async Task<string> SummarizeWithAnthropic(string input)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("x-api-key", anthropicApiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                var requestBody = new
                {
                    model = "claude-3-haiku-20240307", // veya claude-3-opus-20240229, claude-3-sonnet-20240229
                    max_tokens = 512,
                    temperature = 0.5,
                    messages = new[]
                    {
                        new {
                            role = "user",
                            content = $"Aşağıdaki metni Türkçe olarak kısa ve anlamlı bir şekilde özetle:\n\n{input}"
                        }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.anthropic.com/v1/messages", content);

                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return "Özetleme başarısız: " + responseString;

                using var doc = JsonDocument.Parse(responseString);
                // Claude API yanıtı: { "content": [ { "type": "text", "text": "Özet..." } ], ... }
                var summary = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
                return summary ?? "Özet bulunamadı.";
            }
            catch (Exception ex)
            {
                return $"Exception: {ex.Message}";
            }
        }
    }
}