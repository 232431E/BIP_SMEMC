using BIP_SMEMC.Models;
using Newtonsoft.Json;
using BIP_SMEMC.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BIP_SMEMC.Services
{
    public class GeminiService
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly Supabase.Client _db;
        // Correct Endpoint for 1.5 Flash (v1beta)
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
        public GeminiService(HttpClient http, IConfiguration config, Supabase.Client db)
        {
            _http = http;
            _apiKey = config["Gemini:ApiKey"];
            _db = db;
        }

        public async Task<List<NewsOutlookModel>> GenerateIndustryOutlooks(List<NewsArticleModel> articles, List<string> industries, List<string> regions)
        {
            var url = $"{BaseUrl}?key={_apiKey}";

            var contextBuilder = new StringBuilder();
            // Limit increased to 50 for 2.5 Flash
            foreach (var a in articles.Take(50))
            {
                contextBuilder.AppendLine($"- {a.Title} ({a.Source}) | Tags: {string.Join(",", a.Industries)}");
                contextBuilder.AppendLine($"  Summary: {a.Summary}");
            }

            var promptText = $@"
Role: Strategic Market Analyst for SMEs.
Context Data (Recent News):
{contextBuilder}

Task:
Analyze the news above. Identify trends affecting specific Industries and Regions.
Cross-reference against these target lists:
Industries: [{string.Join(", ", industries)}]
Regions: [{string.Join(", ", regions)}]

Output Requirements:
For every Industry/Region combination that has RELEVANT news or implied impact in the data:
1. 'industry': The specific industry name from the list.
2. 'region': The specific region name from the list.
3. 'outlook_summary': A 3-sentence strategic forecast for SMEs.
4. 'key_events': One specific event or trend driving this outlook.
5. 'top_leaders': Top 3 entities/companies mentioned or implied.

Format:
Return ONLY a valid JSON array of objects. No markdown.
[{{ ""industry"": ""Tech"", ""region"": ""Global"", ""outlook_summary"": ""..."", ""key_events"": ""..."", ""top_leaders"": [] }}]";

            Debug.WriteLine("==================================================");
            Debug.WriteLine($"[GEMINI REQUEST] Generating Outlooks");
            Debug.WriteLine($"[CONTEXT SIZE] {articles.Count} articles");
            Debug.WriteLine("==================================================");

            var requestBody = new { contents = new[] { new { parts = new[] { new { text = promptText } } } } };

            try
            {
                var response = await _http.PostAsync(url, new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[GEMINI HTTP ERROR] {response.StatusCode}: {errorBody}");
                    return new List<NewsOutlookModel>();
                }

                var json = await response.Content.ReadAsStringAsync();
                return ParseOutlookResponse(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GEMINI CRITICAL ERROR] {ex.Message}");
                return new List<NewsOutlookModel>();
            }
        }

        private List<NewsOutlookModel> ParseOutlookResponse(string json)
        {
            try
            {
                Debug.WriteLine("==================================================");
                Debug.WriteLine($"[GEMINI RAW RESPONSE] Length: {json.Length}");
                Debug.WriteLine("==================================================");

                var obj = JObject.Parse(json);
                var text = obj["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

                if (string.IsNullOrEmpty(text))
                {
                    Debug.WriteLine("[GEMINI PARSE FAIL] No candidate text found.");
                    return new List<NewsOutlookModel>();
                }

                // Clean Markdown
                string cleanJson = Regex.Replace(text, @"^```json\s*|\s*```$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline).Trim();

                // DEBUG: Print clean JSON to verify structure before deserialization
                // Debug.WriteLine($"[CLEAN JSON PREVIEW] {cleanJson.Substring(0, Math.Min(500, cleanJson.Length))}...");

                var outlooks = JsonConvert.DeserializeObject<List<NewsOutlookModel>>(cleanJson);

                if (outlooks == null)
                {
                    Debug.WriteLine("[GEMINI PARSE FAIL] Deserialization returned null.");
                    return new List<NewsOutlookModel>();
                }

                Debug.WriteLine($"[GEMINI SUCCESS] Parsed {outlooks.Count} outlook items.");

                // Validate items before returning
                foreach (var o in outlooks)
                {
                    o.Date = DateTime.UtcNow; // Ensure date is set
                    if (o.TopLeaders == null) o.TopLeaders = new List<string>(); // Prevent null list errors
                    // Log individual items to ensure fields are mapping correctly
                    Debug.WriteLine($" -> Outlook: {o.Industry}/{o.Region} | Key Event: {o.KeyEvents?.Substring(0, Math.Min(20, o.KeyEvents?.Length ?? 0))}...");
                }

                return outlooks;
            }
            catch (JsonReaderException jex)
            {
                Debug.WriteLine($"[GEMINI JSON ERROR] {jex.Message}");
                Debug.WriteLine($"[BAD JSON] {json}"); // Log full bad JSON for manual inspection
                return new List<NewsOutlookModel>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GEMINI PARSE ERROR] {ex.Message}");
                return new List<NewsOutlookModel>();
            }
        }
        public async Task<string> AnalyzeFinancialTrends(List<TransactionModel> history)
        {
            var url = $"{BaseUrl}?key={_apiKey}";

            var monthlyStats = history
                .GroupBy(t => t.Date.ToString("MMM yyyy"))
                .Select(g => new {
                    Month = g.Key,
                    Net = g.Sum(t => t.Credit - t.Debit),
                    In = g.Sum(t => t.Credit),
                    Out = g.Sum(t => t.Debit)
                }).ToList();

            var summaryJson = JsonConvert.SerializeObject(monthlyStats);

            var promptText = $@"
Act as a Financial Analyst. Here is the monthly cashflow summary:
{summaryJson}

Task:
1. Identify the trend (Growing/Declining).
2. Point out the worst month.
3. Give 1 specific recommendation.
Keep it under 50 words.";

            Debug.WriteLine($"[GEMINI REQ] AnalyzeTrends for {monthlyStats.Count} months.");

            var request = new { contents = new[] { new { parts = new[] { new { text = promptText } } } } };

            try
            {
                var response = await _http.PostAsync(url, new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[GEMINI TRENDS ERROR] {response.StatusCode}: {err}");
                    return "AI service unavailable.";
                }

                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                var text = obj["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                return text ?? "Analysis unavailable.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AI TRENDS EXCEPTION] {ex.Message}");
                return "AI Analysis error.";
            }
        }
    }
}