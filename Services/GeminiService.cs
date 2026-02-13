using BIP_SMEMC.Models;
using Google.GenAI;
using Google.GenAI.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BIP_SMEMC.Services
{
    public class GeminiService
    {
        private readonly Client _client;
        private readonly Supabase.Client _db;
        // Define Model Priority List
        private readonly string[] _models = new[] {"gemini-not"};
            //new[] { 
            //"gemini-2.5-flash", 
            //"gemini-3-flash-preview", 
            //"gemini-2.5-flash-lite",
            //"gemini-2.0-flash" }; 
        public GeminiService(IConfiguration config, Supabase.Client db)
        {
            var apiKey = config["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey)) throw new Exception("Gemini API Key is missing in appsettings.json");

            // Initialize Official Google GenAI Client
            _client = new Client(apiKey: apiKey);
            _db = db;
        }

        // =========================================================================
        // CORE: SMART EXECUTION ENGINE (HANDLES FALLBACK, SDK, & LOGGING)
        // =========================================================================
        // Services/GeminiService.cs

        private async Task<bool> IsOverQuota(string modelName)
        {
            try
            {
                var today = DateTime.UtcNow.Date;
                var countRes = await _db.From<AIResponseModel>()
                    .Filter("version_tag", Postgrest.Constants.Operator.Equals, modelName)
                    .Filter("date_key", Postgrest.Constants.Operator.Equals, today)
                    .Count(Postgrest.Constants.CountType.Exact);

                return countRes >= 18; // Threshold before switching
            }
            catch { return false; }
        }

        private async Task<string> ExecutePromptWithFallbackAsync(
            string prompt,
            string featureType,
            string userId = "SYSTEM_BG_SERVICE",
            bool expectJson = false)
        {
            foreach (var modelName in _models)
            {
                // 1. Quota Guard: Check if we have already used this model 18 times today
                if (await IsOverQuota(modelName))
                {
                    Debug.WriteLine($"[QUOTA SKIP] {modelName} is near limit. Trying next...");
                    continue;
                }

                try
                {
                    Debug.WriteLine($"[GEMINI] Attempting {modelName}...");

                    var config = new GenerateContentConfig
                    {
                        Temperature = 0.4,
                        MaxOutputTokens = 1500,
                        ResponseMimeType = expectJson ? "application/json" : "text/plain"
                    };

                    var response = await _client.Models.GenerateContentAsync(modelName, prompt, config);
                    var text = response?.Candidates?[0]?.Content?.Parts?[0]?.Text;

                    if (string.IsNullOrWhiteSpace(text)) continue;

                    // 2. Validate JSON if required
                    if (expectJson)
                    {
                        try
                        {
                            var cleaned = CleanJson(text);
                            Newtonsoft.Json.Linq.JToken.Parse(cleaned);
                            await LogToSupabase(userId, featureType, text, $"Success via {modelName}");
                            return cleaned; // SUCCESS: EXIT LOOP IMMEDIATELY
                        }
                        catch {
                            Debug.WriteLine($"[GEMINI] {modelName} JSON invalid. Retrying...");
                            continue;
                        }
                    }

                    // 3. Text Success
                    await LogToSupabase(userId, featureType, text, $"Success via {modelName}");
                    return text; // SUCCESS: EXIT LOOP IMMEDIATELY
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("429")) continue;
                    Debug.WriteLine($"[GEMINI ERROR] {modelName}: {ex.Message}");
                }
            }
            return null;
        }
        
        private async Task LogToSupabase(string userId, string feature, string rawResponse, string justification)
        {
            try
            {
                var entry = new AIResponseModel
                {
                    UserId = userId,
                    FeatureType = feature,
                    ResponseText = rawResponse, // Saving the actual AI text response
                    Justification = justification,
                    VersionTag = "Google.GenAI SDK",
                    DateKey = DateTime.UtcNow.Date,
                    CreatedAt = DateTime.UtcNow
                };

                await _db.From<AIResponseModel>().Insert(entry);
                Debug.WriteLine($"[DB LOG] Saved {feature} response to DB.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB LOG FAIL] Could not save AI log: {ex.Message}");
            }
        }

        // =========================================================================
        // MISSING METHOD RESTORED (Used by ChatbotController)
        // =========================================================================
        public async Task<string> GenerateFinanceInsightAsync(string prompt)
        {
            // Wraps the new fallback logic
            return await ExecutePromptWithFallbackAsync(prompt, "GENERAL_INSIGHT", "USER_ACTION", false)
                   ?? "AI Service is currently unavailable.";
        }

        // =========================================================================
        // FEATURE 1: INDUSTRY OUTLOOKS (Called by NewsBGService)
        // =========================================================================
        public async Task<List<NewsOutlookModel>> GenerateIndustryOutlooks(List<NewsArticleModel> articles, List<string> industries, List<string> regions)
        {
            var sb = new StringBuilder();
            foreach (var a in articles.Take(40))
            {
                sb.AppendLine($"- {a.Title} ({a.Source}): {a.Summary}");
            }

            var prompt = $@"
            Role: Market Analyst.
            News Context:
            {sb}

            Task: Analyze impacts for these Industries: [{string.Join(", ", industries)}] in Regions: [{string.Join(", ", regions)}].

            Return ONLY a valid JSON array matching this schema exactly (No Markdown):
            [
              {{ 
                ""industry"": ""string"", 
                ""region"": ""string"", 
                ""outlook_summary"": ""string"", 
                ""key_events"": ""string""
              }}
            ]";

            var jsonResponse = await ExecutePromptWithFallbackAsync(prompt, "NEWS_OUTLOOK", "SYSTEM_BG_SERVICE", true);

            if (string.IsNullOrEmpty(jsonResponse)) return new List<NewsOutlookModel>();

            try
            {
                var cleanJson = CleanJson(jsonResponse);
                var outlooks = JsonConvert.DeserializeObject<List<NewsOutlookModel>>(cleanJson);

                if (outlooks != null)
                {
                    foreach (var o in outlooks) o.Date = DateTime.UtcNow.Date;
                }
                return outlooks ?? new List<NewsOutlookModel>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PARSE ERROR] Could not deserialize Outlooks: {ex.Message}");
                return new List<NewsOutlookModel>();
            }
        }

        // =========================================================================
        // FEATURE 2: CASHFLOW ANALYSIS (Called by CashFlowController)
        // =========================================================================
        public async Task<string> GenerateDetailedCashflowAnalysis(string jsonSummary)
        {
            var prompt = $@"
            Role: Senior CFO. 
            Data: {jsonSummary}

            Task:
            1. DETERMINE TREND: Is cashflow INCREASING or DECREASING?
            2. DIAGNOSE: Why? Cite specific numbers/categories.
            3. PREDICT: 3-Month Outlook.
            4. ADVISE: One actionable step.

            Format: Plain text, under 100 words.
            ";

            return await GenerateFinanceInsightAsync(prompt);
        }

        // =========================================================================
        // FEATURE 3: FINANCIAL TRENDS (Legacy)
        // =========================================================================
        public async Task<string> AnalyzeFinancialTrends(List<TransactionModel> history)
        {
            var monthlyStats = history
                .GroupBy(t => t.Date.ToString("MMM"))
                .Select(g => new { Month = g.Key, Net = g.Sum(t => t.Credit - t.Debit) });

            var json = JsonConvert.SerializeObject(monthlyStats);

            var prompt = $@"
            Analyze this monthly net cashflow trend: {json}.
            Identify the worst month and give 1 tip. Keep it brief.
            ";

            return await GenerateFinanceInsightAsync(prompt);
        }

        // =========================================================================
        // FEATURE 4: CHATBOT
        // =========================================================================
        public async Task<(bool Success, string Content, bool QuotaExceeded)> GenerateFinanceChatJsonAsync(string system, string context, string userMsg)
        {
            var prompt = $@"
            {system}
            
            Context Data:
            {context}

            User Question: {userMsg}

            Return strict JSON: {{ ""answer"": ""..."", ""actionItems"": [], ""whichNumbersIUsed"": {{...}} }}
            ";

            var response = await ExecutePromptWithFallbackAsync(prompt, "CHAT_BOT", "USER_CHAT", true);

            if (response == null) return (false, "AI Service Busy.", true);

            return (true, CleanJson(response), false);
        }

        // --- HELPER ---
        private string CleanJson(string text)
        {
            return Regex.Replace(text, @"^```json\s*|\s*```$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline).Trim();
        }
    }
}