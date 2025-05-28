using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public class AnthropicService
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private readonly bool _useDummy;

    public AnthropicService(string apiKey, bool useDummy = false)
    {
        _apiKey = apiKey;
        _useDummy = useDummy;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<string> GenerateQuestionAsync(string category)
    {
        if (_useDummy)
        {
            // Dummy JSON for development/testing
            return $"{{\"question\":\"{category} için örnek soru?\",\"choices\":[\"A\",\"B\",\"C\",\"D\"],\"answer\":\"A\"}}";
        }
        try
        {
            var prompt = $"Lütfen {category} kategorisinde, çoktan seçmeli, 4 şıklı ve tek doğru cevabı olan bir test sorusu üret. Soru ve şıkları JSON formatında dön: {{\\\"question\\\": \\\"...\\\", \\\"choices\\\": [\\\"A\\\", \\\"B\\\", \\\"C\\\", \\\"D\\\"], \\\"answer\\\": \\\"A\\\"}}";

            var requestBody = new
            {
                model = "claude-3-opus-20240229",
                max_tokens = 256,
                temperature = 0.7,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"API Error: {response.StatusCode} - {responseString}";
            }

            using var doc = JsonDocument.Parse(responseString);
            var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
            var jsonStart = text.IndexOf('{');
            var jsonEnd = text.LastIndexOf('}');
            if (jsonStart == -1 || jsonEnd == -1)
                return $"API response format error: {text}";
            var json = text.Substring(jsonStart, jsonEnd - jsonStart + 1);
            return json;
        }
        catch (Exception ex)
        {
            return $"Exception: {ex.Message}";
        }
    }

    public async Task<string> ExplainAnswerAsync(string question, string correctAnswer)
    {
        if (_useDummy)
        {
            return $"Doğru cevap: {correctAnswer}. Açıklama: Bu sadece örnek bir açıklamadır.";
        }
        try
        {
            var prompt = $"Aşağıdaki çoktan seçmeli sorunun doğru cevabını ve neden doğru olduğunu Türkçe olarak açıkla.\nSoru: {question}\nDoğru Cevap: {correctAnswer}";
            var requestBody = new
            {
                model = "claude-3-opus-20240229",
                max_tokens = 256,
                temperature = 0.7,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);
            var responseString = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return $"API Error: {response.StatusCode} - {responseString}";
            }
            using var doc = JsonDocument.Parse(responseString);
            var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
            return text;
        }
        catch (Exception ex)
        {
            return $"Exception: {ex.Message}";
        }
    }

    public async Task<List<CategoryQuestion>> GenerateQuestionsWithExplanationsAsync(List<string> categories, int totalQuestionCount = 3)
    {
        if (_useDummy)
        {
            // Dummy data for development/testing
            var questions = new List<CategoryQuestion>();
            int perCategory = totalQuestionCount / categories.Count;
            foreach (var cat in categories)
            {
                for (int i = 0; i < perCategory; i++)
                {
                    questions.Add(new CategoryQuestion
                    {
                        Category = cat,
                        Question = $"{cat} için örnek soru {i + 1}?",
                        Choices = new List<string> { "A", "B", "C", "D" },
                        Answer = "A",
                        Explanation = $"{cat} için açıklama {i + 1}."
                    });
                }
            }
            // Eğer tam bölünemiyorsa kalanları ekle
            int remaining = totalQuestionCount - questions.Count;
            for (int i = 0; i < remaining; i++)
            {
                questions.Add(new CategoryQuestion
                {
                    Category = categories[i % categories.Count],
                    Question = $"{categories[i % categories.Count]} için ek soru?",
                    Choices = new List<string> { "A", "B", "C", "D" },
                    Answer = "A",
                    Explanation = $"{categories[i % categories.Count]} için ek açıklama."
                });
            }
            return questions;
        }
        try
        {
            var prompt = $"Aşağıdaki kategoriler için, her biriyle ilgili çoktan seçmeli 4 şıklı, tek doğru cevaplı toplamda {totalQuestionCount} soru, şıkları, doğru cevabı ve doğru cevabın açıklamasını JSON formatında üret. Kategoriler: {string.Join(", ", categories)}. Her kategori için mümkünse eşit sayıda soru üret. Format:\n[{{\"category\":\"...\",\"question\":\"...\",\"choices\":[\"A\",\"B\",\"C\",\"D\"],\"answer\":\"A\",\"explanation\":\"...\"}}, ...]";

            var requestBody = new
            {
                model = "claude-3-opus-20240229",
                max_tokens = 4096,
                temperature = 0.7,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"API Error: {response.StatusCode} - {responseString}");
            }

            using var doc = JsonDocument.Parse(responseString);
            var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
            var jsonStart = text.IndexOf('[');
            var jsonEnd = text.LastIndexOf(']');
            if (jsonStart == -1 || jsonEnd == -1)
                throw new Exception($"API response format error: {text}");
            var json = text.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var questions = JsonSerializer.Deserialize<List<CategoryQuestion>>(json);
            return questions;
        }
        catch (Exception ex)
        {
            throw new Exception($"Exception: {ex.Message}");
        }
    }
}

public class CategoryQuestion
{
    [JsonPropertyName("category")]
    public string Category { get; set; }
    [JsonPropertyName("question")]
    public string Question { get; set; }
    [JsonPropertyName("choices")]
    public List<string> Choices { get; set; }
    [JsonPropertyName("answer")]
    public string Answer { get; set; }
    [JsonPropertyName("explanation")]
    public string Explanation { get; set; }
}

// using System.Net.Http;
// using System.Net.Http.Headers;
// using System.Text;
// using System.Text.Json;
// using System.Threading.Tasks;

// public class AnthropicService
// {
//     private readonly string _apiKey;
//     private readonly HttpClient _httpClient;

//     public AnthropicService(string apiKey)
//     {
//         _apiKey = apiKey;
//         _httpClient = new HttpClient();
//         _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
//         _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
//     }

//     public async Task<string> GenerateQuestionAsync(string category)
//     {
//         try
//         {
//             var prompt = $"Lütfen {category} kategorisinde, çoktan seçmeli, 4 şıklı ve tek doğru cevabı olan bir test sorusu üret. Soru ve şıkları JSON formatında dön: {{\"question\": \"...\", \"choices\": [\"A\", \"B\", \"C\", \"D\"], \"answer\": \"A\"}}";

//             var requestBody = new
//             {
//                 model = "claude-3-opus-20240229",
//                 max_tokens = 256,
//                 temperature = 0.7,
//                 messages = new[]
//                 {
//                     new { role = "user", content = prompt }
//                 }
//             };

//             var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
//             var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);
//             var responseString = await response.Content.ReadAsStringAsync();

//             if (!response.IsSuccessStatusCode)
//             {
//                 return $"API Error: {response.StatusCode} - {responseString}";
//             }

//             using var doc = JsonDocument.Parse(responseString);
//             var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
//             var jsonStart = text.IndexOf('{');
//             var jsonEnd = text.LastIndexOf('}');
//             if (jsonStart == -1 || jsonEnd == -1)
//                 return $"API response format error: {text}";
//             var json = text.Substring(jsonStart, jsonEnd - jsonStart + 1);
//             return json;
//         }
//         catch (Exception ex)
//         {
//             return $"Exception: {ex.Message}";
//         }
//     }
// } 