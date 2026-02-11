using BIP_SMEMC.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Xml.Linq;

namespace BIP_SMEMC.Services
{
    public class NewsBGService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly HttpClient _http;
        private readonly IConfiguration _config;

        public NewsBGService(IServiceProvider services, HttpClient http, IConfiguration config)
        {
            _services = services;
            _http = http;
            _config = config;
        }

        public async Task TriggerNewsCycle()
        {
            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Supabase.Client>();
                var ai = scope.ServiceProvider.GetRequiredService<GeminiService>();
                var seeder = scope.ServiceProvider.GetRequiredService<CategorySeederService>();

                await seeder.EnsureIndustriesAndRegionsExist();
                await RunDailyNewsCycle(db, ai);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(5000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await TriggerNewsCycle();

                var now = DateTime.UtcNow.AddHours(8);
                var nextRun = now.Date.AddDays(1).AddHours(8);
                var delay = nextRun - now;

                Debug.WriteLine($"[NEWS BG] Next run in {delay.TotalHours:F1} hours");
                await Task.Delay(delay, stoppingToken);
            }
        }

        public async Task RunDailyNewsCycle(Supabase.Client db, GeminiService ai)
        {
            try
            {
                Debug.WriteLine("--- [START] News Cycle Debugger ---");

                // 1. Fetch Source Data
                var rawArticles = await FetchMultiSourceNews();
                if (!rawArticles.Any())
                {
                    Debug.WriteLine("[WARN] No articles found from any source.");
                    return;
                }

                // 2. Load Tags
                var industries = (await db.From<IndustryModel>().Get()).Models;
                var regions = (await db.From<RegionModel>().Get()).Models;

                // 3. Tag Articles
                var taggedArticles = LocalKeywordTagging(rawArticles, industries, regions);

                // 4. CLEANUP: Delete old news
                try
                {
                    var cutoffDate = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");
                    await db.From<NewsArticleModel>()
                        .Filter("date", Postgrest.Constants.Operator.LessThan, cutoffDate)
                        .Delete();
                }
                catch (Exception ex) { Debug.WriteLine($"[CLEANUP WARN] {ex.Message}"); }

                // 5. CRITICAL FIX: SAFE INSERT LOOP
                // Instead of batch Upsert which fails on conflict if constraints aren't perfect,
                // we check existence and insert only new ones.
                var uniqueArticles = taggedArticles
                    .GroupBy(x => x.Url)
                    .Select(g => g.First())
                    .ToList();

                Debug.WriteLine($"[DB PRE-SAVE] Processing {uniqueArticles.Count} articles...");

                int savedCount = 0;
                foreach (var art in uniqueArticles)
                {
                    try
                    {
                        // 1. Check if URL exists
                        var exists = await db.From<NewsArticleModel>()
                            .Select("id") // Select minimal data
                            .Filter("url", Postgrest.Constants.Operator.Equals, art.Url)
                            .Get();

                        if (exists.Models.Count == 0)
                        {
                            // 2. Insert if new
                            await db.From<NewsArticleModel>().Insert(art);
                            savedCount++;
                            Debug.WriteLine($" -> Saved: {art.Title.Substring(0, Math.Min(20, art.Title.Length))}...");
                        }
                        else
                        {
                            Debug.WriteLine($" -> Skipped (Exists): {art.Title.Substring(0, Math.Min(20, art.Title.Length))}...");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but continue loop so one bad apple doesn't kill the batch
                        Debug.WriteLine($"[INSERT ERROR] {art.Url}: {ex.Message}");
                    }
                }

                Debug.WriteLine($"[DB SUCCESS] Saved {savedCount} new articles.");
                // 6. AI Generation
                Debug.WriteLine("[AI] Starting Outlook Generation...");
                var outlooks = await ai.GenerateIndustryOutlooks(uniqueArticles,
                    industries.Select(i => i.Name).ToList(),
                    regions.Select(r => r.Name).ToList());

                if (outlooks != null && outlooks.Any())
                {
                    // Clear old outlooks before adding new ones
                    await db.From<NewsOutlookModel>().Delete();
                    await db.From<NewsOutlookModel>().Insert(outlooks);
                    Debug.WriteLine($"[AI SUCCESS] Saved {outlooks.Count} industry outlooks.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NEWS SERVICE ERROR] {ex.Message}");
                if (ex.InnerException != null) Debug.WriteLine($"[INNER] {ex.InnerException.Message}");
            }
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

        private List<NewsArticleModel> LocalKeywordTagging(List<NewsArticleModel> articles, List<IndustryModel> inds, List<RegionModel> regs)
        {
            foreach (var art in articles)
            {
                var text = (art.Title + " " + art.Summary).ToLower();
                art.Industries = new List<string>();
                art.Regions = new List<string>();

                foreach (var ind in inds)
                    if (text.Contains(ind.Name.ToLower())) art.Industries.Add(ind.Name);

                foreach (var reg in regs)
                    if (text.Contains(reg.Name.ToLower())) art.Regions.Add(reg.Name);

                if (!art.Industries.Any()) art.Industries.Add("General Business");
                if (!art.Regions.Any()) art.Regions.Add("Global");
            }
            return articles;
        }
    }
}