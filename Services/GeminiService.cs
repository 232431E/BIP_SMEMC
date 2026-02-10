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
        private readonly string _apiKey = "AIzaSyAiLClWYM9KJ9VT98c_dIG4WdCU4JnAahs";
        private readonly HttpClient _http;

        public GeminiService(HttpClient http) => _http = http;
        public async Task<List<NewsOutlookModel>> GenerateIndustryOutlooks(List<NewsArticleModel> articles, List<string> industries, List<string> regions)
        {
            var url = $"https://generativelanguage.googleapis.com/v1/models/gemini-1.5-flash:generateContent?key={_apiKey}";

            // Batch all articles into one context string
            var context = string.Join("\n", articles.Take(15).Select(a => $"Headline: {a.Title} | Tags: {string.Join(",", a.Industries)}"));
            Debug.WriteLine("--- [GEMINI PROMPT CONTEXT] ---");
            Debug.WriteLine(context);
            Debug.WriteLine("-------------------------------");
            var promptText = $@"You are a Strategic Market Analyst. Using these headlines:
    {context}

    For each unique combination of Industry: [{string.Join(",", industries)}] and Region: [{string.Join(",", regions)}] found:
    1. Generate an 'outlook_summary' (3 sentences on SME impact).
    2. Rank 'top_leaders': List the top 5 influential companies or figures.
    
    Return ONLY a valid JSON array of objects:
    [{{ ""industry"": """", ""region"": """", ""outlook_summary"": """", ""top_leaders"": [""Company A"", ""Person B""] }}]";

            var request = new { contents = new[] { new { parts = new[] { new { text = promptText } } } } };
            var response = await _http.PostAsync(url, new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();

            return ParseOutlookResponse(json);
        }
        private List<NewsOutlookModel> ParseOutlookResponse(string json)
        {
            try
            {
                Debug.WriteLine($"[GEMINI RAW] {json}"); // Log full response

                var obj = JObject.Parse(json);

                // Check if Gemini blocked the prompt or returned an error
                if (obj["error"] != null)
                {
                    Debug.WriteLine($"[GEMINI API ERROR] {obj["error"]["message"]}");
                    return new List<NewsOutlookModel>();
                }

                var text = obj["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                if (string.IsNullOrEmpty(text))
                {
                    Debug.WriteLine("[GEMINI ERROR] Empty response content. Gemini might have blocked the content.");
                    return new List<NewsOutlookModel>();
                }

                string cleanJson = Regex.Replace(text, "```json|```", "").Trim();
                var outlooks = JsonConvert.DeserializeObject<List<NewsOutlookModel>>(cleanJson);
                return outlooks ?? new List<NewsOutlookModel>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GEMINI PARSE ERROR] {ex.Message}");
                return new List<NewsOutlookModel>();
            }
        }
        public async Task<List<NewsArticleModel>> ProcessNewsBatch(string rawHeadlines, List<string> industries, List<string> regions)
        {
    //        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";

    //        // BATCH PROMPT: Request summaries for all articles at once
    //        var promptText = $@"Analyze the following business headlines. 
    //1. Summarize the top 10 relevant to SMEs.
    //2. Provide a 1-sentence 'Outlook' for the industry.
    //3. Return ONLY a JSON array. 
    //Format: [{{""title"": """", ""summary"": """", ""url"": """", ""industries"": [], ""regions"": []}}]
    //Data: {rawHeadlines}";

    //        var request = new { contents = new[] { new { parts = new[] { new { text = promptText } } } } };
    //        var jsonBody = JsonConvert.SerializeObject(request);

    //        var response = await _http.PostAsync(url, new StringContent(jsonBody, Encoding.UTF8, "application/json"));
    //        var result = await response.Content.ReadAsStringAsync();

            return new List<NewsArticleModel>();
        }
        public async Task<List<NewsOutlookModel>> GenerateBatchOutlooks(List<NewsArticleModel> articles, List<string> industries, List<string> regions)
        {
            // Change from v1beta to v1 and use the latest alias
            var url = $"https://generativelanguage.googleapis.com/v1/models/gemini-1.5-flash-latest:generateContent?key={_apiKey}";
            // Group all news into a context string
            var context = string.Join("\n", articles.Select(a => $"- {a.Title}: {a.Summary} (Source: {a.Source})"));

            var promptText = $@"You are a Strategic SME Consultant. Analyze this news context:
    {context}

    For each Industry: [{string.Join(",", industries)}] in Region: [{string.Join(",", regions)}]:
    1. Summarize current SME impact (outlook_summary).
    2. Identify 'key_events' (impactful regulatory or market shifts).
    3. Rank 'top_leaders': List the top 5 influential companies or figures in that specific niche.
    
    Return ONLY a JSON array. Format: [{{""industry"":"""", ""region"":"""", ""outlook_summary"":"""", ""key_events"":"""", ""top_leaders"":[""1"",""2""]}}]";

            var request = new { contents = new[] { new { parts = new[] { new { text = promptText } } } } };
            var response = await _http.PostAsync(url, new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();

            return ParseOutlookResponse(json);
        }
    }
}