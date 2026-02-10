using BIP_SMEMC.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace BIP_SMEMC.Services
{
    public class NewsBGService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly HttpClient _http;
        private const string GuardianKey = "e43ea3b9-209a-43cd-801a-16051f6678f6";

        public NewsBGService(IServiceProvider services, HttpClient http)
        {
            _services = services;
            _http = http;
        }
        public async Task TriggerNewsCycle()
        {
            // Create a scope to resolve Scoped services (Seeder and Supabase)
            using (var scope = _services.CreateScope())
            {
                // Resolve Scoped services inside the using block
                var db = scope.ServiceProvider.GetRequiredService<Supabase.Client>();
                var seederService = scope.ServiceProvider.GetRequiredService<CategorySeederService>();

                await seederService.EnsureIndustriesAndRegionsExist();

                // 2. Load them for tagging
                var industries = await db.From<IndustryModel>().Get();
                var regions = await db.From<RegionModel>().Get();

                Debug.WriteLine($"[DB] Loaded {industries.Models.Count} industries and {regions.Models.Count} regions.");
            }
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var nowSgt = DateTime.UtcNow.AddHours(8);
                var nextRun = DateTime.Today.AddHours(11);
                if (nowSgt > nextRun) nextRun = nextRun.AddDays(1);
                await Task.Delay(nextRun - nowSgt, stoppingToken);

                using (var scope = _services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<Supabase.Client>();
                    var ai = scope.ServiceProvider.GetRequiredService<GeminiService>();
                    await RunDailyNewsCycle(db, ai);
                }
            }
        }

        public async Task RunDailyNewsCycle(Supabase.Client db, GeminiService ai)
        {
            try
            {
                Debug.WriteLine("--- [START] Manual News Cycle Triggered ---");                // PHASE 1: RETRIEVAL & STORAGE
                var industryLookups = (await db.From<IndustryModel>().Get()).Models;
                var regionLookups = (await db.From<RegionModel>().Get()).Models;
                Debug.WriteLine($"[DB] Loaded {industryLookups.Count} industries and {regionLookups.Count} regions for tagging.");
                // PHASE 1: Retrieval
                var rawArticles = await FetchMultiSourceNews();
                Debug.WriteLine($"[FETCH] Retrieved {rawArticles.Count} total raw articles.");
                if (!rawArticles.Any())
                {
                    Debug.WriteLine("[WARN] No articles were returned from any external source.");
                    return;
                }
                // PHASE 2: Tagging & Fallback Logic
                var taggedArticles = LocalKeywordTagging(rawArticles, industryLookups, regionLookups);
                
                // Retention: Remove news older than 7 days
                // PHASE 3: Fixed Persistence (Avoid LINQ Where for Delete)
                Debug.WriteLine("[DB] Cleaning up old news...");
                // Use string-based Filter to avoid NotImplementedException
                await db.From<NewsArticleModel>()
                    .Filter("date", Postgrest.Constants.Operator.LessThan, DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd"))
                    .Delete();
                Debug.WriteLine($"[DB] Saving {taggedArticles.Count} new articles...");
                var insertResult = await db.From<NewsArticleModel>().Insert(taggedArticles);
                Debug.WriteLine($"[SUPABASE] Successfully saved {insertResult.Models.Count} articles.");// PHASE 2: AI OUTLOOK BATCH
                                                                                                        // PHASE 4: AI Outlook with Gemini
                Debug.WriteLine("[AI] Calling Gemini for Market Outlook...");
                var outlooks = await ai.GenerateIndustryOutlooks(taggedArticles,
                    industryLookups.Select(i => i.Name).ToList(),
                    regionLookups.Select(r => r.Name).ToList());

                if (outlooks != null && outlooks.Any())
                {
                    await db.From<NewsOutlookModel>().Delete();
                    await db.From<NewsOutlookModel>().Insert(outlooks);
                    Debug.WriteLine($"[AI SUCCESS] Saved {outlooks.Count} outlooks.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CRITICAL NEWS SERVICE ERROR] {ex.Message}");
                // More specific logging for Supabase errors
                if (ex.InnerException != null) Debug.WriteLine($"[INNER ERROR] {ex.InnerException.Message}");
                throw;
            }
        }

        private async Task<List<NewsArticleModel>> FetchMultiSourceNews()
        {
            var allArticles = new List<NewsArticleModel>();

            // Source 1: The Guardian
            Debug.WriteLine("[SOURCE] Hitting Guardian API...");
            // FIX: Added 'order-by=newest' to get fresh news
            string url = $"[https://content.guardianapis.com/search?section=business&q=SME%20Asia&order-by=newest&api-key=](https://content.guardianapis.com/search?section=business&q=SME%20Asia&order-by=newest&api-key=){GuardianKey}";
            try
            {
                var guardianJson = await _http.GetStringAsync(url);
                var guardianList = ParseGuardian(guardianJson);
                Debug.WriteLine($"[EXTRACTED] Found {guardianList.Count} articles from The Guardian.");
                foreach (var art in guardianList)
                    Debug.WriteLine($" -> Headline: {art.Title} | URL: {art.Url}");

                allArticles.AddRange(guardianList);
                // Source 2: Yahoo Finance (via RSS or generic business search)
                // YahooFinanceClient is typically for quotes; for news we use the public RSS feeds
                var yahooRes = await _http.GetStringAsync("https://finance.yahoo.com/news/rssindex");
                // Basic parsing logic for RSS would go here

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GUARDIAN ERROR] {ex.Message}");
            }

            
            return allArticles;
        }

        private List<NewsArticleModel> LocalKeywordTagging(List<NewsArticleModel> articles, List<IndustryModel> inds, List<RegionModel> regs)
        {
            foreach (var art in articles)
            {
                string content = (art.Title + " " + art.Summary).ToLower();

                // Strict matching to avoid everything becoming "General"
                art.Industries = inds.Where(i => content.Contains(i.Name.ToLower())).Select(i => i.Name).ToList();
                art.Regions = regs.Where(r => content.Contains(r.Name.ToLower())).Select(r => r.Name).ToList();

                if (!art.Industries.Any()) art.Industries.Add("General Business");
                if (!art.Regions.Any()) art.Regions.Add("Global");
                art.Date = DateTime.Today;
            }
            return articles;
        }

        private List<NewsArticleModel> ParseGuardian(string json)
        {
            var list = new List<NewsArticleModel>();
            var data = JObject.Parse(json);
            foreach (var item in data["response"]["results"])
            {
                list.Add(new NewsArticleModel
                {
                    Title = item["webTitle"]?.ToString(),
                    Url = item["webUrl"]?.ToString(),
                    Source = "The Guardian",
                    Summary = "Recent update on SME business trends and regional economic shifts."
                });
            }
            return list;
        }
    }
}