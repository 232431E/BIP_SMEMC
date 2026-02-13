using BIP_SMEMC.Models;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly IMemoryCache _cache; // Faster than DB

        // Define Model Priority List
        // ==========================================
        // 1. DEFINE MODEL CONSTANTS FOR RATE LIMIT SPREADING
        // ==========================================

        // Define specific model constants based on your API list
        private const string MODEL_CHAT_WORKHORSE = "gemini-2.0-flash-lite"; // 14.4k limit
        private const string MODEL_FAST = "gemini-3-flash-preview";         // 1.5k limit
        private const string MODEL_SMART = "gemini-2.5-pro";          // 1.5k limit
        private readonly string[] _emergencyModels = new[]
        {
            "gemini-2.5-flash-lite"
        };
        // Daily Limits (Safety buffer: set slightly lower than actual API limit)
        private readonly Dictionary<string, int> _modelLimits = new()
        {
            { MODEL_CHAT_WORKHORSE, 14000 },
            { MODEL_FAST, 1450 },
            { MODEL_SMART, 1450 }
        };

        public GeminiService(IConfiguration config, Supabase.Client db, IMemoryCache cache)
        {
            var apiKey = config["Gemini:ApiKey"];
            if (string.IsNullOrEmpty(apiKey)) throw new Exception("Gemini API Key is missing");

            _client = new Client(apiKey: apiKey);
            _db = db;
            _cache = cache;
        }

        // =========================================================================
        // 1. QUOTA MANAGEMENT (RAM CACHE)
        // =========================================================================
        private bool IsOverQuota(string modelName)
        {
            // Unique key per day per model
            string cacheKey = $"GEMINI_USAGE_{modelName}_{DateTime.UtcNow:yyyyMMdd}";

            int currentCount = _cache.GetOrCreate(cacheKey, entry =>
            {
                entry.AbsoluteExpiration = DateTime.UtcNow.AddDays(1).Date; // Reset at midnight UTC
                return 0;
            });

            // If model is in our tracked list, check limit
            if (_modelLimits.TryGetValue(modelName, out int limit))
            {
                bool isOver = currentCount >= limit;
                if (isOver) Debug.WriteLine($"[QUOTA] {modelName} hit limit ({currentCount}/{limit})");
                return isOver;
            }

            // Emergency models usually have low limits (e.g. 1500), assume 1400 safety
            return currentCount >= 1400;
        }

        private void IncrementUsage(string modelName)
        {
            string cacheKey = $"GEMINI_USAGE_{modelName}_{DateTime.UtcNow:yyyyMMdd}";

            if (_cache.TryGetValue(cacheKey, out int currentCount))
            {
                _cache.Set(cacheKey, currentCount + 1);
            }
            else
            {
                _cache.Set(cacheKey, 1);
            }
            Debug.WriteLine($"[USAGE] {modelName} count: {currentCount + 1}");
        }

        // =========================================================================
        // 2. INTELLIGENT ROUTING
        // =========================================================================
        private List<string> GetModelsForFeature(string featureType)
        {
            var priority = new List<string>();

            switch (featureType)
            {
                case "CHAT_BOT":
                    // High volume -> Gemma first
                    priority.Add(MODEL_CHAT_WORKHORSE);
                    priority.Add(MODEL_FAST);
                    priority.Add(MODEL_SMART);
                    priority.Add("gemini-2.5-flash-lite");
                    priority.Add("gemini-2.0-flash-lite");
                    break;

                case "NEWS_OUTLOOK":
                    // Medium reasoning, large context -> Fast Flash first
                    priority.Add("gemini-2.5-flash-lite");
                    priority.Add("gemini-3-flash-preview");
                    break;

                case "FINANCIAL_INSIGHT":
                    // High reasoning -> Smart Pro first
                    priority.Add(MODEL_SMART);
                    priority.Add(MODEL_FAST);
                    break;

                default:
                    priority.Add("gemini-2.5-flash-lite");
                    break;
            }

            // Always add emergency models at the end
            priority.AddRange(_emergencyModels);
            return priority;
        }

        // =========================================================================
        // 3. EXECUTION ENGINE
        // =========================================================================
        private async Task<string> ExecutePromptWithFallbackAsync(
            string prompt,
            string featureType,
            string userId,
            bool expectJson)
        {
            var modelsToTry = GetModelsForFeature(featureType);
            StringBuilder errorLog = new StringBuilder();

            foreach (var modelName in modelsToTry)
            {
                if (IsOverQuota(modelName)) continue;

                try
                {
                    Debug.WriteLine($"--- [AI START] Feature: {featureType} | Model: {modelName} ---");
                    Debug.WriteLine($"[PROMPT PREVIEW] {prompt.Substring(0, Math.Min(100, prompt.Length))}...");

                    var config = new GenerateContentConfig
                    {
                        Temperature = 0.2,
                        MaxOutputTokens = 4000, // Slightly higher for complex JSON
                        ResponseMimeType = expectJson ? "application/json" : "text/plain"
                    };

                    var response = await _client.Models.GenerateContentAsync(modelName, prompt, config);
                    var text = response?.Candidates?[0]?.Content?.Parts?[0]?.Text;

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        Debug.WriteLine($"[AI FAIL] {modelName} returned empty text.");
                        await LogToSupabase(userId, featureType, text, modelName);
                        continue;
                    }

                    // JSON Validation
                    if (expectJson)
                    {
                        try
                        {
                            text = CleanJson(text);
                            if (!text.Trim().EndsWith("]") && !text.Trim().EndsWith("}"))
                            {
                                Debug.WriteLine($"[GEMINI] {modelName} output truncated. Retrying next...");
                                continue;
                            }
                            JToken.Parse(text);
                        }
                        catch (Exception jsonEx)
                        {
                            Debug.WriteLine($"[AI JSON ERROR] {modelName}: {jsonEx.Message}");
                            continue; // Try next model
                        }
                    }

                    // Success!
                    IncrementUsage(modelName);

                    // Log details to DB asynchronously (don't block return)
                    _ = LogToSupabase(userId, featureType, text, modelName);

                    Debug.WriteLine($"--- [AI SUCCESS] {modelName} | Length: {text.Length} ---");
                    return text;
                }
                catch (Exception ex)
                {
                    errorLog.AppendLine($"{modelName}: {ex.Message}");
                    Debug.WriteLine($"[AI EXCEPTION] {modelName}: {ex.Message}");
                }
            }

            Debug.WriteLine($"[AI CRITICAL FAILURE] All models failed. Errors: {errorLog}");
            return null;
        }

        private async Task LogToSupabase(string userId, string feature, string response, string modelUsed)
        {
            try
            {
                var entry = new AIResponseModel
                {
                    UserId = userId,
                    FeatureType = feature,
                    ResponseText = response,
                    VersionTag = modelUsed, // CRITICAL: This lets you track which model is failing/garbling
                    Justification = $"Log for {feature} via {modelUsed}",
                    DateKey = DateTime.UtcNow.Date,
                    CreatedAt = DateTime.UtcNow // Stored as full timestamp for the 3-day cleanup logic
                };

                await _db.From<AIResponseModel>().Insert(entry);
            }
            catch (Exception ex) { Debug.WriteLine($"[DB LOG ERROR] {ex.Message}"); }
        }

        private string CleanJson(string text)
        {
            return Regex.Replace(text, @"^```json\s*|\s*```$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline).Trim();
        }
        // =========================================================================
        // PUBLIC METHODS (Updated to use new Routing)
        // =========================================================================

        //GenerateFinanceInsightAsync for sean profit improvement use?
        public async Task<string> GenerateFinanceInsightAsync(string prompt)
        {
            // Uses FINANCIAL_INSIGHT strategy (Pro -> Flash)
            return await ExecutePromptWithFallbackAsync(prompt, "FINANCIAL_INSIGHT", "USER_ACTION", false)
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
            var prompt = $@"{system} ... {context} ... {userMsg} ... JSON schema..."; // (Your prompt logic)

            // Uses CHAT_BOT strategy (Gemma -> Flash -> Pro)
            var response = await ExecutePromptWithFallbackAsync(prompt, "CHAT_BOT", "USER_CHAT", true);

            if (response == null) return (false, "AI Service Busy.", true);
            return (true, response, false);
        }

    }
}