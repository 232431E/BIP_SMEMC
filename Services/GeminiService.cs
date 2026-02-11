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
        // Using v1beta as it is standard for 1.5-flash. 
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash-latest:generateContent";
        public GeminiService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _apiKey = config["Gemini:ApiKey"];
        }

        public async Task<List<NewsOutlookModel>> GenerateIndustryOutlooks(List<NewsArticleModel> articles, List<string> industries, List<string> regions)
        {
            // CRITICAL FIX: Append API Key here
            var url = $"{BaseUrl}?key={_apiKey}";

            // 1. Build Rich Context
            var contextBuilder = new StringBuilder();
            // Limiting to 15 articles to avoid token limits, prioritizing those with diverse tags if possible
            foreach (var a in articles.Take(15))
            {
                contextBuilder.AppendLine($"--- NEWS ITEM ---");
                contextBuilder.AppendLine($"Headline: {a.Title}");
                contextBuilder.AppendLine($"Summary: {a.Summary}"); // Included Summary
                contextBuilder.AppendLine($"Source: {a.Source} | Link: {a.Url}"); // Included Link
                contextBuilder.AppendLine($"Tags: {string.Join(", ", a.Industries)}");
                contextBuilder.AppendLine();
            }

            // 2. Enhanced Prompt
            var promptText = $@"
Role: Strategic Market Analyst.
Context Data (News Articles):
{contextBuilder}

Task:
Analyze the provided news. Use the links and summaries to infer deeper market trends. 
You may use your internal knowledge to supplement the 'Outlook' if the news is sparse, but prioritize the provided context.

Requirements:
For unique Industry/Region combinations found in the context (matching: Industries [{string.Join(",", industries)}] and Regions [{string.Join(",", regions)}]):

1. 'outlook_summary': A 3-sentence strategic summary for SMEs in that sector/region.
2. 'top_leaders': Top 5 companies or figures mentioned or relevant to this trend.

Format:
Return ONLY a valid JSON array. No markdown formatting.
Example: [{{ ""industry"": ""Tech"", ""region"": ""Global"", ""outlook_summary"": ""..."", ""top_leaders"": [""A"",""B""] }}]";

            var requestBody = new { contents = new[] { new { parts = new[] { new { text = promptText } } } } };
            var jsonContent = JsonConvert.SerializeObject(requestBody);

            // DEBUG: Log what we are sending
            Debug.WriteLine("------------------------------------------------");
            Debug.WriteLine($"[GEMINI REQ] URL: {BaseUrl}..."); // Don't log full key
            Debug.WriteLine($"[GEMINI REQ] Prompt Length: {promptText.Length} chars");
            // Debug.WriteLine($"[GEMINI REQ] Payload: {jsonContent}"); // Uncomment to see full payload
            Debug.WriteLine("------------------------------------------------");

            try
            {
                var response = await _http.PostAsync(url, new StringContent(jsonContent, Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode)
                {
                    // DEBUG: Log the exact error from Google
                    var errorJson = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[GEMINI HTTP ERROR] Status: {response.StatusCode}");
                    Debug.WriteLine($"[GEMINI ERROR BODY] {errorJson}");
                    return new List<NewsOutlookModel>();
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                return ParseOutlookResponse(jsonResponse);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GEMINI EXCEPTION] {ex.Message}");
                return new List<NewsOutlookModel>();
            }
        }

        private List<NewsOutlookModel> ParseOutlookResponse(string json)
        {
            try
            {
                Debug.WriteLine($"[GEMINI RES] Raw Length: {json.Length}");
                var obj = JObject.Parse(json);

                var text = obj["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                if (string.IsNullOrEmpty(text))
                {
                    Debug.WriteLine("[GEMINI RES] No text candidate returned.");
                    return new List<NewsOutlookModel>();
                }

                // Clean Markdown code blocks if present
                string cleanJson = Regex.Replace(text, @"^```json\s*|\s*```$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline).Trim();

                var outlooks = JsonConvert.DeserializeObject<List<NewsOutlookModel>>(cleanJson);
                Debug.WriteLine($"[GEMINI RES] Successfully deserialized {outlooks?.Count} outlooks.");
                return outlooks ?? new List<NewsOutlookModel>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GEMINI PARSE ERROR] {ex.Message}");
                Debug.WriteLine($"[RAW WAS] {json}"); // Log raw to see why parse failed
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