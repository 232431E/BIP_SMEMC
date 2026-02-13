using BIP_SMEMC.Models;
using BIP_SMEMC.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace BIP_SMEMC.Controllers
{
    public class CashFlowController : Controller
    {
        private readonly FinanceService _financeService;
        private readonly Supabase.Client _supabase;

        public CashFlowController(Supabase.Client supabase, FinanceService financeService)
        {
            _financeService = financeService;
            _supabase = supabase;
        }

        public async Task<IActionResult> Index()
        {
            Debug.WriteLine("=== [START] SMART CASHFLOW ENGINE ===");
            var model = new CashFlowViewModel();
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return RedirectToAction("Login", "Account");

            try
            {
                // 1. DATA SETUP
                // 1. POPULATE VIEW BAGS FIRST (Fixes Empty Modal)
                var userRes = await _supabase.From<UserModel>().Where(u => u.Email == userEmail).Get();
                var userPrefs = userRes.Models.FirstOrDefault();
                model.UserPreferences = userPrefs;

                var indList = (await _supabase.From<IndustryModel>().Get()).Models;
                var regList = (await _supabase.From<RegionModel>().Get()).Models;

                ViewBag.AllIndustries = indList.OrderBy(i => i.Name).ToList();
                ViewBag.AllRegions = regList.OrderBy(r => r.Name).ToList();

                Debug.WriteLine($"[METADATA] Loaded {indList.Count} Industries, {regList.Count} Regions for Filter Modal.");

                var categoriesList = (await _supabase.From<CategoryModel>().Get()).Models;
                var catMap = categoriesList.ToDictionary(k => k.Id, v => v);

                var anchorDate = await _financeService.GetLatestTransactionDate(userEmail);
                model.LatestDataDate = anchorDate;

                // Get 6 months history for the "Trend"
                var startDate = anchorDate.AddMonths(-6);
                var history = await _financeService.GetUserTransactions(userEmail, startDate, anchorDate);

                // Map Category Names (needed for AI & Logic)
                foreach (var t in history)
                {
                    if (t.CategoryId.HasValue && catMap.TryGetValue(t.CategoryId.Value, out var c))
                        t.CategoryName = c.Name;
                }

                // 2. CALCULATE CHART DATA (Cumulative Balance)
                decimal currentCash = 0;
                var historicalPoints = new List<ChartDataPoint>();

                foreach (var t in history.OrderBy(t => t.Date))
                {
                    currentCash += (t.Credit - t.Debit);
                    historicalPoints.Add(new ChartDataPoint
                    {
                        Date = t.Date.ToString("yyyy-MM-dd"),
                        Actual = currentCash
                    });
                }
                model.CurrentBalance = currentCash;
                model.ChartData.AddRange(historicalPoints);

                // 3. SMART FORECASTING ENGINE (Weighted + Anomaly Free)
                // ----------------------------------------------------
                decimal weightedRevenue = 0;
                decimal weightedFixed = 0;
                decimal weightedVar = 0;
                decimal totalWeight = 0;

                // Anomaly Keywords (One-off large expenses to ignore in forecast)
                var anomalyKeywords = new[] { "renovation", "deposit", "equipment", "setup fee" };

                // Group by Month to apply weighting
                var monthlyGroups = history
                    .GroupBy(t => new { t.Date.Year, t.Date.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .ToList();

                // Apply Weights: Most recent month = 3, Previous = 2, Others = 1
                // This makes the forecast sensitive to recent changes (like a sudden rent hike)
                for (int i = 0; i < monthlyGroups.Count; i++)
                {
                    var group = monthlyGroups[i];
                    int weight = (i >= monthlyGroups.Count - 2) ? 3 : 1;

                    decimal mRev = 0, mFixed = 0, mVar = 0;

                    foreach (var t in group)
                    {
                        string cName = (t.CategoryName ?? "").ToLower();
                        string desc = (t.Description ?? "").ToLower();

                        // SKIP: Depreciation/Amortization
                        if (cName.Contains("depreciation")) continue;

                        if (t.Credit > 0) mRev += t.Credit;
                        else if (t.Debit > 0)
                        {
                            // SKIP: Anomalies
                            if (anomalyKeywords.Any(k => cName.Contains(k) || desc.Contains(k))) continue;

                            // Classify Fixed vs Variable
                            if (cName.Contains("rent") || cName.Contains("salary") || cName.Contains("loan") || cName.Contains("subscription"))
                                mFixed += t.Debit;
                            else
                                mVar += t.Debit;
                        }
                    }

                    weightedRevenue += (mRev * weight);
                    weightedFixed += (mFixed * weight);
                    weightedVar += (mVar * weight);
                    totalWeight += weight;
                }

                // Calculate Weighted Averages per Month
                decimal avgWRev = totalWeight > 0 ? weightedRevenue / totalWeight : 0;
                decimal avgWFixed = totalWeight > 0 ? weightedFixed / totalWeight : 0;
                decimal avgWVar = totalWeight > 0 ? weightedVar / totalWeight : 0;
                decimal varRatio = avgWRev > 0 ? avgWVar / avgWRev : 0;

                model.MonthlyFixedBurn = avgWFixed;
                model.VariableCostRatio = varRatio;

                // 4. GENERATE PROJECTION (Next 30 Days)
                // ----------------------------------------------------
                // Bridge Point
                model.ChartData.Add(new ChartDataPoint
                {
                    Date = anchorDate.ToString("yyyy-MM-dd"),
                    Actual = null,
                    Predicted = currentCash
                });

                // Calculate Seasonality (Optional: Apply specific month multiplier)
                // For this month (Month + 1)
                int nextMonth = anchorDate.AddMonths(1).Month;
                double seasonalMult = _financeService.CalculateSeasonalityMultiplier(history, nextMonth);

                var projections = GenerateSmartProjection(
                    currentCash,
                    anchorDate,
                    avgWRev * (decimal)seasonalMult, // Apply seasonality to revenue 
                    varRatio,
                    avgWFixed
                );

                model.ChartData.AddRange(projections);
                model.ProjectedCashIn30Days = projections.Last().Predicted ?? 0;

                // Determine Runway Text
                decimal netBurn = (avgWFixed + (avgWRev * varRatio)) - avgWRev;
                model.CashRunway = netBurn > 0 && currentCash > 0
                    ? $"{currentCash / netBurn:N1} Months Runway"
                    : "Cashflow Positive";

                // 5. AI DEEP ANALYSIS & PERSISTENCE
                // ----------------------------------------------------
                // Check if we already did analysis today
                var existingAI = await _supabase.From<AIResponseModel>()
                    .Where(x => x.UserId == userEmail && x.FeatureType == "FINANCIAL_GRAPH_ANALYSIS" && x.DateKey == DateTime.UtcNow.Date)
                    .Limit(1)
                    .Get();

                if (existingAI.Models.Any())
                {
                    model.AIAnalysis = existingAI.Models.First().ResponseText;
                    Debug.WriteLine("[AI] Loaded cached analysis.");
                }
                else if (history.Count > 0)
                {
                    // Generate New
                    var aiData = _financeService.GetCashflowSummaryForAI(history);
                    string jsonSummary = Newtonsoft.Json.JsonConvert.SerializeObject(aiData);
                    Debug.WriteLine($"[GEMINI SEND] Sending {jsonSummary.Length} chars of data to Gemini...");
                    Debug.WriteLine("[GEMINI] Calling API...");
                    var aiInsight = await HttpContext.RequestServices.GetRequiredService<GeminiService>()
                        .GenerateDetailedCashflowAnalysis(jsonSummary);
                    Debug.WriteLine($"[GEMINI] Received: {aiInsight.Substring(0, Math.Min(50, aiInsight.Length))}...");
                    model.AIAnalysis = aiInsight;

                    // Save
                    await _financeService.SaveCashflowInsight(userEmail, aiInsight, "6M Weighted Trend");
                }
                else
                {
                    model.AIAnalysis = "Insufficient data for analysis.";
                }
                // 5. FETCH NEWS (OPTIMIZED)
                // Only fetch if preferences exist, otherwise don't slam the DB.
                

                // Fetch outlooks specific to user preferences
                if (userPrefs != null)
                {
                    var pInds = userPrefs.Industries ?? new List<string>();
                    var pRegs = userPrefs.Regions ?? new List<string>();

                    // Fetch Outlooks
                    var outlooks = await _supabase.From<NewsOutlookModel>()
                        .Order("date", Postgrest.Constants.Ordering.Descending)
                        .Get();

                    model.Outlooks = outlooks.Models
                .Where(o => pInds.Contains(o.Industry) || pRegs.Contains(o.Region))
                .GroupBy(o => new { o.Industry, o.Region }) // Deduplicate
                .Select(g => g.First())
                .ToList();

                    // Fetch News
                    await LoadFilteredNews(model, userPrefs);
                }
                // Populate Dropdowns for Modal
                ViewBag.AllIndustries = (await _supabase.From<IndustryModel>().Get()).Models.OrderBy(i => i.Name).ToList();
                ViewBag.AllRegions = (await _supabase.From<RegionModel>().Get()).Models.OrderBy(r => r.Name).ToList();
                await LoadFilteredNews(model, model.UserPreferences);
            }

            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] {ex.Message}");
            }

            return View(model);
        }
        // Updated Helper Method
        private List<ChartDataPoint> GenerateSmartProjection(
            decimal startBalance,
            DateTime startDate,
            decimal monthlyRev,
            decimal varRatio,
            decimal monthlyFixed)
        {
            var points = new List<ChartDataPoint>();
            decimal balance = startBalance;
            decimal dailyRevenue = monthlyRev / 30m;
            decimal dailyFixed = monthlyFixed / 30m;

            for (int i = 1; i <= 30; i++)
            {
                decimal dailyVar = dailyRevenue * varRatio;
                decimal dailyNet = dailyRevenue - dailyFixed - dailyVar;
                balance += dailyNet;

                points.Add(new ChartDataPoint
                {
                    Date = startDate.AddDays(i).ToString("yyyy-MM-dd"),
                    Actual = null,
                    Predicted = Math.Round(balance, 2)
                });
            }
            return points;
        }
        // Helper to update prefs via AJAX
        [HttpPost]
        public async Task<IActionResult> UpdatePreferences(List<string> industries, List<string> regions)
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return Unauthorized();

            Debug.WriteLine($"[PREFS] Updating for {userEmail}...");

            try
            {
                // FIX: Use explicit Set() and Where() to guarantee update
                await _supabase.From<UserModel>()
                    .Where(u => u.Email == userEmail)
                    .Set(u => u.Industries, industries ?? new List<string>())
                    .Set(u => u.Regions, regions ?? new List<string>())
                    .Update();

                Debug.WriteLine("[PREFS] Update Successful.");
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PREFS ERROR] {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task LoadFilteredNews(CashFlowViewModel model, UserModel prefs)
        {
            // Trigger BG Service if table empty
            var count = await _supabase.From<NewsArticleModel>().Count(Postgrest.Constants.CountType.Exact);
            if (count == 0)
            {
                var bg = HttpContext.RequestServices.GetService<NewsBGService>();
                if (bg != null) await bg.TriggerNewsCycle();
            }

            var allNews = (await _supabase.From<NewsArticleModel>()
                .Order("date", Postgrest.Constants.Ordering.Descending)
                .Limit(100)
                .Get()).Models;

            var pInds = prefs?.Industries ?? new List<string>();
            var pRegs = prefs?.Regions ?? new List<string>();

            // Smart Filter: Show articles that match EITHER industry OR region
            model.News = allNews.Where(n =>
                (n.Industries != null && n.Industries.Intersect(pInds).Any()) ||
                (n.Regions != null && n.Regions.Intersect(pRegs).Any())
            ).Take(15).ToList();
        }
    }
}