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
        public async Task TriggerNewsCycle()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Supabase.Client>();
                var ai = scope.ServiceProvider.GetRequiredService<GeminiService>();
                await RunDailyNewsCycle(db, ai);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(5000, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                await TriggerNewsCycle();
            }
        }

        public async Task RunDailyNewsCycle(Supabase.Client db, GeminiService ai)
        {
            try
            {
                Debug.WriteLine("--- [CHECKING] News Status for Today ---");

                // 1. Define "Today" range (UTC)
                var todayStart = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

                // 2. CHECK: Do we already have outlooks generated for today?
                // If we have outlooks, we likely don't need to run the full cycle again.
                var todayOutlooks = await db.From<NewsOutlookModel>()
                    .Filter("date", Postgrest.Constants.Operator.Equals, todayStart)
                    .Get();

                if (todayOutlooks.Models.Any())
                {
                    Debug.WriteLine("[SKIP] Industry outlooks already exist for today. News cycle aborted.");
                    return;
                }

                // 3. CHECK: Do we have enough fresh news articles?
                var todayArticles = await db.From<NewsArticleModel>()
                    .Filter("date", Postgrest.Constants.Operator.Equals, todayStart)
                    .Get();

                // If you have at least some articles but NO outlooks, you might want to 
                // skip the FETCH step and just run the AI generation.
                bool needFetch = todayArticles.Models.Count < 10; // Threshold: e.g., 10 articles

                if (!needFetch)
                {
                    Debug.WriteLine("[INFO] Fresh articles found, skipping fetch and proceeding to AI generation.");
                }
                else
                {
                    // --- EXISTING FETCH & TAGGING LOGIC ---
                    Debug.WriteLine("[PROCESS] Fetching new articles...");
                    var rawArticles = await FetchMultiSourceNews();
                    if (!rawArticles.Any()) return;

                    var industries = (await db.From<IndustryModel>().Get()).Models;
                    var regions = (await db.From<RegionModel>().Get()).Models;
                    var taggedArticles = LocalKeywordTagging(rawArticles, industries, regions);

                    foreach (var art in taggedArticles)
                    {
                        var exists = await db.From<NewsArticleModel>()
                            .Filter("url", Postgrest.Constants.Operator.Equals, art.Url)
                            .Get();
                        if (exists.Models.Count == 0) await db.From<NewsArticleModel>().Insert(art);
                    }
                }

                // 4. GENERATE OUTLOOKS (Only if we reached this point)
                var industriesList = (await db.From<IndustryModel>().Get()).Models;
                var regionsList = (await db.From<RegionModel>().Get()).Models;

                var context = (await db.From<NewsArticleModel>()
                    .Order("date", Postgrest.Constants.Ordering.Descending)
                    .Limit(50).Get()).Models;

                Debug.WriteLine("[AI] Generating new industry outlooks...");
                var newOutlooks = await ai.GenerateIndustryOutlooks(
                    context,
                    industriesList.Select(i => i.Name).ToList(),
                    regionsList.Select(r => r.Name).ToList()
                );

                if (newOutlooks.Any()) await db.From<NewsOutlookModel>().Insert(newOutlooks);

                Debug.WriteLine("--- [FINISHED] News Cycle ---");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NEWS BG ERROR] {ex.Message}");
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