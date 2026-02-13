using BIP_SMEMC.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Xml.Linq;

namespace BIP_SMEMC.Services
{
    public class NewsBGService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly HttpClient _http;
        private readonly IConfiguration _config;

        public NewsBGService(IServiceScopeFactory scopeFactory, HttpClient http, IConfiguration config)
        {
            _scopeFactory = scopeFactory;
            _http = http;
            _config = config;
        }
        // Controllers need this public method to manually trigger a refresh
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 1. Initial short delay to let Supabase/Gemini services initialize
            await Task.Delay(2000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                DateTime nextRun;

                // 2. Decide the next window (12 AM or 12 PM)
                if (now.Hour < 12)
                {
                    nextRun = now.Date.AddHours(12); // Target: Today at Noon
                }
                else
                {
                    nextRun = now.Date.AddDays(1);   // Target: Tomorrow at Midnight
                }

                var delay = nextRun - now;

                // 3. SCHEDULE LOGGING
                Debug.WriteLine($"[SCHEDULE] Current Time: {now:HH:mm:ss}");
                Debug.WriteLine($"[SCHEDULE] Next 12h window: {nextRun:yyyy-MM-dd HH:mm:ss}");
                Debug.WriteLine($"[SCHEDULE] Sleeping for {delay.TotalMinutes:F1} minutes...");

                // 4. WAIT UNTIL THE WINDOW
                await Task.Delay(delay, stoppingToken);

                // 5. TRIGGER ONLY AFTER DELAY
                if (!stoppingToken.IsCancellationRequested)
                {
                    await TriggerNewsCycle();
                }
            }
        }
        public async Task TriggerNewsCycle()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Supabase.Client>();
            var ai = scope.ServiceProvider.GetRequiredService<GeminiService>();

            Debug.WriteLine($"--- [NEWS CYCLE START] {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");

            // 1. Cleanup old AI responses (Fixes Criterion Type Error)
            await CleanupOldAIResponses(db);

            // 2. Run Cycle (Fetch -> Insert -> AI Generation)
            await RunDailyNewsCycle(db, ai);

            // 3. Debug
            await DebugRetrieveLatestOutlook(db);
        }

        private async Task CleanupOldAIResponses(Supabase.Client db)
        {
            try
            {
                // Fix: Use ISO String format for Supabase dates
                var cutoff = DateTime.UtcNow.AddDays(-3).ToString("yyyy-MM-ddTHH:mm:ssZ");
                Debug.WriteLine($"[CLEANUP] Removing outlooks older than {cutoff}");

                await db.From<AIResponseModel>()
                    .Filter("feature_type", Postgrest.Constants.Operator.Equals, "NEWS_OUTLOOK")
                    .Filter("created_at", Postgrest.Constants.Operator.LessThan, cutoff)
                    .Delete();
            }
            catch (Exception ex) { Debug.WriteLine($"[CLEANUP ERROR] {ex.Message}"); }
        }

        private async Task DebugRetrieveLatestOutlook(Supabase.Client db)
        {
            try
            {
                // Fix: Simplified query logic to prevent PGRST100
                var res = await db.From<AIResponseModel>()
                    .Filter("feature_type", Postgrest.Constants.Operator.Equals, "NEWS_OUTLOOK")
                    .Order("created_at", Postgrest.Constants.Ordering.Descending)
                    .Limit(1)
                    .Get();

                var latest = res.Models.FirstOrDefault();
                if (latest != null)
                {
                    Debug.WriteLine("--- [RETRIEVAL CHECK] ---");
                    Debug.WriteLine($"Model: {latest.VersionTag}");
                    Debug.WriteLine($"Content Length: {latest.ResponseText.Length}");
                    // Preview first 200 chars
                    Debug.WriteLine($"Preview: {latest.ResponseText.Substring(0, Math.Min(200, latest.ResponseText.Length))}");
                
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[DEBUG RETRIEVAL ERROR] {ex.Message}"); }
        }

        public async Task RunDailyNewsCycle(Supabase.Client db, GeminiService ai)
        {
            try
            {
                // 1. FETCH & TAG NEWS
                Debug.WriteLine("[PROCESS 1/2] Fetching fresh news articles...");
                var rawArticles = await FetchMultiSourceNews();

                if (rawArticles.Any())
                {
                    var industries = (await db.From<IndustryModel>().Get()).Models;
                    var regions = (await db.From<RegionModel>().Get()).Models;
                    var tagged = LocalKeywordTagging(rawArticles, industries, regions);

                    int newCount = 0;
                    foreach (var art in tagged)
                    {
                        var check = await db.From<NewsArticleModel>().Filter("url", Postgrest.Constants.Operator.Equals, art.Url).Get();
                        if (!check.Models.Any())
                        {
                            await db.From<NewsArticleModel>().Insert(art);
                            newCount++;
                        }
                    }
                    Debug.WriteLine($"[PROCESS] Inserted {newCount} new articles.");
                }

                // 2. AI OUTLOOK GENERATION
                Debug.WriteLine("[PROCESS 2/2] Retrieving news context for AI...");
                var context = (await db.From<NewsArticleModel>()
                    .Order("date", Postgrest.Constants.Ordering.Descending)
                    .Limit(40).Get()).Models;

                if (!context.Any())
                {
                    Debug.WriteLine("[SKIP] No news articles available in DB to process.");
                    return;
                }

                var industriesList = (await db.From<IndustryModel>().Get()).Models.Select(i => i.Name).ToList();
                var regionsList = (await db.From<RegionModel>().Get()).Models.Select(r => r.Name).ToList();

                var outlooks = await ai.GenerateIndustryOutlooks(context, industriesList, regionsList);

                if (outlooks != null && outlooks.Any())
                {
                    // Clear today's previous run to prevent duplicate UI items
                    var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                    await db.From<NewsOutlookModel>().Filter("date", Postgrest.Constants.Operator.Equals, today).Delete();

                    await db.From<NewsOutlookModel>().Insert(outlooks);
                    Debug.WriteLine($"[SUCCESS] AI Outlook generated and saved. Count: {outlooks.Count}");
                }
                else
                {
                    Debug.WriteLine("[WARNING] AI returned 0 outlooks or failed all models.");
                    // Attempt to retrieve the last "Garbled" response for debug viewing
                    await DebugRetrieveGarbledResponse(db);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[NEWS CYCLE ERROR] {ex.Message}"); }
        }
        
        private async Task DebugRetrieveGarbledResponse(Supabase.Client db)
        {
            try
            {
                var res = await db.From<AIResponseModel>()
                    .Filter("feature_type", Postgrest.Constants.Operator.Equals, "NEWS_OUTLOOK")
                    .Order("created_at", Postgrest.Constants.Ordering.Descending)
                    .Limit(1)
                    .Get();

                var log = res.Models.FirstOrDefault();
                if (log != null)
                {
                    Debug.WriteLine("--- [GARBLED DATA RETRIEVAL] ---");
                    Debug.WriteLine($"Model: {log.VersionTag}");
                    Debug.WriteLine($"Full Raw Text: {log.ResponseText}");
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[DEBUG RETRIEVAL ERROR] {ex.Message}"); }
        }
        private async Task<List<NewsArticleModel>> FetchMultiSourceNews()
        {
            var articles = new List<NewsArticleModel>();
            var apiKey = _config["NewsApi:GuardianKey"];

            // GUARDIAN
            // Added 'show-fields=trailText' to get summaries
            string guardianUrl = $"https://content.guardianapis.com/search?section=business&q=economy&order-by=newest&show-fields=trailText&page-size=30&api-key={apiKey}";
            try
            {
                Debug.WriteLine($"[HTTP] GET Guardian...");
                var json = await _http.GetStringAsync(guardianUrl);
                var jObj = JObject.Parse(json);
                int count = 0;
                foreach (var result in jObj["response"]["results"])
                {
                    articles.Add(new NewsArticleModel
                    {
                        Title = result["webTitle"]?.ToString(),
                        Url = result["webUrl"]?.ToString(),
                        Source = "The Guardian",
                        Summary = result["fields"]?["trailText"]?.ToString() ?? result["webTitle"]?.ToString(), // Fallback to title
                        Date = DateTime.UtcNow
                    });
                    count++;
                }
                Debug.WriteLine($"[GUARDIAN] Parsed {count} articles.");
            }
            catch (Exception ex) { Debug.WriteLine($"[Guardian Fail] {ex.Message}"); }

            // YAHOO RSS
            try
            {
                var rssUrl = "https://finance.yahoo.com/news/rssindex";
                Debug.WriteLine($"[HTTP] GET Yahoo RSS...");
                var rssXml = await _http.GetStringAsync(rssUrl);
                var doc = XDocument.Parse(rssXml);

                var items = doc.Descendants("item").Take(20);
                int count = 0;
                foreach (var item in items)
                {
                    string rawDesc = item.Element("description")?.Value ?? "";
                    // Remove HTML tags from Yahoo description
                    string cleanDesc = System.Text.RegularExpressions.Regex.Replace(rawDesc, "<.*?>", String.Empty);

                    articles.Add(new NewsArticleModel
                    {
                        Title = item.Element("title")?.Value,
                        Url = item.Element("link")?.Value,
                        Source = "Yahoo Finance",
                        Summary = cleanDesc.Length > 200 ? cleanDesc.Substring(0, 197) + "..." : cleanDesc,
                        Date = DateTime.UtcNow
                    });
                    count++;
                }
                Debug.WriteLine($"[YAHOO] Parsed {count} articles.");
            }
            catch (Exception ex) { Debug.WriteLine($"[Yahoo Fail] {ex.Message}"); }

            return articles;
        }

        // ---------------------------------------------------------
        // IMPROVED TAGGING LOGIC
        // ---------------------------------------------------------
        private List<NewsArticleModel> LocalKeywordTagging(List<NewsArticleModel> articles, List<IndustryModel> dbIndustries, List<RegionModel> dbRegions)
        {
            foreach (var art in articles)
            {
                // Normalize text for searching
                var text = $"{art.Title} {art.Summary}".ToLower();

                // Use HashSets to prevent duplicate tags on the same article
                var foundIndustries = new HashSet<string>();
                var foundRegions = new HashSet<string>();

                // 1. MATCH INDUSTRIES
                foreach (var ind in dbIndustries)
                {
                    // Get keywords for this specific industry (e.g., "F&B" -> food, drink, dining)
                    var keywords = GetIndustryKeywords(ind.Name);

                    // Check if the article text contains ANY of the keywords
                    if (keywords.Any(k => text.Contains(k)))
                    {
                        foundIndustries.Add(ind.Name);
                    }
                }

                // 2. MATCH REGIONS
                foreach (var reg in dbRegions)
                {
                    var keywords = GetRegionKeywords(reg.Name);
                    if (keywords.Any(k => text.Contains(k)))
                    {
                        foundRegions.Add(reg.Name);
                    }
                }

                // 3. DEFAULTS
                if (!foundIndustries.Any()) foundIndustries.Add("General Business");
                if (!foundRegions.Any()) foundRegions.Add("Global");

                // Assign back to list
                art.Industries = foundIndustries.ToList();
                art.Regions = foundRegions.ToList();
            }
            return articles;
        }

        // --- KEYWORD MAPPINGS ---
        // This maps the "Database Name" to "Real World Text" found in news
        private string[] GetIndustryKeywords(string dbName)
        {
            var lowerName = dbName.ToLower();
            return lowerName switch
            {
                "technology" => new[] { "tech", "software", " ai ", "artificial intelligence", "cyber", "digital", "data", "app ", "computing" },
                "finance" => new[] { "bank", "money", "invest", "rates", "tax", "stock", "crypto", "wealth", "economy", "financial", "inflation", "cpi" },
                "healthcare" => new[] { "health", "pharma", "medical", "hospital", "drug", "care", "vaccine", "doctor" },
                "retail" => new[] { "shop", "store", "consumer", "sales", "retail", "supermarket", "mall" },
                "logistics" => new[] { "supply chain", "shipping", "transport", "cargo", "freight", "delivery", "courier", "mail" },
                "f&b" => new[] { "food", "beverage", "restaurant", "dining", "cafe", "drink", "meal", "diet", "cooking" },
                "energy" => new[] { "oil", "gas", "solar", "wind", "power", "electric", "fuel", "energy", "nuclear" },
                "construction" => new[] { "build", "property", "estate", "housing", "construct", "infrastructure" },
                "manufacturing" => new[] { "factory", "production", "make", "manufacturing", "industrial", "assembly" },
                "agriculture" => new[] { "farm", "crop", "agri", "harvest", "livestock" },
                "education" => new[] { "school", "university", "student", "college", "education", "learning" },
                "e-commerce" => new[] { "online shopping", "amazon", "alibaba", "e-commerce", "marketplace" },
                "sustainability" => new[] { "green", "carbon", "esg", "climate", "sustainable", "environment", "renewable" },
                // Default: just look for the name itself
                _ => new[] { lowerName }
            };
        }

        private string[] GetRegionKeywords(string dbName)
        {
            var lowerName = dbName.ToLower();
            return lowerName switch
            {
                "north america" => new[] { "usa", "u.s.", "united states", "america", "canada", "mexico", "fed ", "wall street" },
                "europe" => new[] { "uk", "britain", "london", "eu ", "european", "germany", "france", "italy", "spain", "eurozone" },
                "asia-pacific" => new[] { "asia", "pacific", "apac", "asean", "orient" },
                "china" => new[] { "china", "chinese", "beijing", "shanghai", "hk", "hong kong" },
                "singapore" => new[] { "singapore", "sg ", "lion city", "mas " },
                "malaysia" => new[] { "malaysia", "kuala lumpur", "ringgit" },
                "indonesia" => new[] { "indonesia", "jakarta", "bali" },
                "india" => new[] { "india", "mumbai", "delhi" },
                "global" => new[] { "global", "world", "international", "planet", "earth" },
                // Default
                _ => new[] { lowerName }
            };
        }
    }
}