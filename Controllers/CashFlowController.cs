using BIP_SMEMC.Models;
using BIP_SMEMC.Services;
using Microsoft.AspNetCore.Mvc;
using System.Composition;
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

                var categoriesList = (await _supabase.From<CategoryModel>().Get()).Models;
                var catMap = categoriesList.ToDictionary(k => k.Id, v => v);
                var revRoot = categoriesList.FirstOrDefault(c => c.Name.Equals("Revenue", StringComparison.OrdinalIgnoreCase))?.Id;
                var expRoot = categoriesList.FirstOrDefault(c => c.Name.Equals("Expense", StringComparison.OrdinalIgnoreCase))?.Id;
                var cogsRoot = categoriesList.FirstOrDefault(c => c.Name.Contains("Cost of Goods Sold"))?.Id;

                var anchorDate = await _financeService.GetLatestTransactionDate(userEmail);
                model.LatestDataDate = anchorDate;

                // 2. Fetch & Deduplicate (Matching Dashboard total of $1.46M)
                DateTime startDate = anchorDate.AddMonths(-12);
                var rawHistory = await _financeService.GetUserTransactions(userEmail, startDate, anchorDate);
                var history = _financeService.GetCleanOperationalData(_financeService.Deduplicate(rawHistory));

                _financeService.LogMonthlyDiagnosticReport(history, revRoot, expRoot, cogsRoot, categoriesList);

                // 3. DAILYSurplus Calculation (Cumulative In/Out)
                var dailyStats = history.GroupBy(t => t.Date.Date).OrderBy(g => g.Key)
                    .Select(g => new {
                        Date = g.Key,
                        Rev = g.Where(t => _financeService.IsRevenue(t, revRoot, categoriesList)).Sum(t => t.Credit),
                        Exp = g.Where(t => _financeService.IsExpense(t, expRoot, cogsRoot, categoriesList)).Sum(t => t.Debit)
                    }).ToList();

                decimal cumulativeNet = 0;
                foreach (var day in dailyStats)
                {
                    cumulativeNet += (day.Rev - day.Exp);
                    model.ChartData.Add(new ChartDataPoint { Date = day.Date.ToString("yyyy-MM-dd"), Actual = cumulativeNet });
                }
                model.CurrentBalance = cumulativeNet;

                // 4. Forecating Engine
                decimal weightedRev = 0, weightedFix = 0, weightedVar = 0, totalWeight = 0;
                var monthlyGroups = history.GroupBy(t => new { t.Date.Year, t.Date.Month }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month).ToList();

                foreach (var group in monthlyGroups)
                {
                    int weight = (monthlyGroups.IndexOf(group) >= monthlyGroups.Count - 2) ? 3 : 1;
                    decimal mRev = 0, mFix = 0, mVar = 0;

                    foreach (var t in group)
                    {
                        if (t.CategoryId.HasValue && catMap.TryGetValue(t.CategoryId.Value, out var c)) t.CategoryName = c.Name;

                        if (_financeService.IsRevenue(t, revRoot, categoriesList)) mRev += t.Credit;
                        else if (_financeService.IsExpense(t, expRoot, cogsRoot, categoriesList))
                        {
                            string name = (t.CategoryName ?? "").ToLower();
                            // Fixed Cost detection for Projection
                            bool isFixed = name.Contains("rent") || name.Contains("salary") || name.Contains("loan") || name.Contains("wage") || name.Contains("payroll");
                            if (isFixed) mFix += t.Debit;
                            else mVar += t.Debit;
                        }
                    }
                    weightedRev += (mRev * weight); weightedFix += (mFix * weight); weightedVar += (mVar * weight); totalWeight += weight;
                }

                model.MonthlyFixedBurn = totalWeight > 0 ? weightedFix / totalWeight : 0;
                decimal avgWRev = totalWeight > 0 ? weightedRev / totalWeight : 0;
                model.VariableCostRatio = avgWRev > 0 ? (weightedVar / totalWeight) / avgWRev : 0;

                // 5. Projection
                int nextMonth = anchorDate.AddMonths(1).Month;
                double seasonalMult = _financeService.CalculateSeasonalityMultiplier(history, nextMonth);
                var projections = GenerateSmartProjection(cumulativeNet, anchorDate, avgWRev * (decimal)seasonalMult, model.VariableCostRatio, model.MonthlyFixedBurn);
                model.ChartData.AddRange(projections);
                model.ProjectedCashIn30Days = projections.Last().Predicted ?? 0;

                decimal monthlyProfit = avgWRev - (model.MonthlyFixedBurn + (avgWRev * model.VariableCostRatio));
                model.CashRunway = monthlyProfit > 0 ? "Operating Profitably (Steady Profit)" : (cumulativeNet > 0 ? $"{(cumulativeNet / Math.Abs(monthlyProfit)):N1} Months Runway" : "0 Months");
                // 6. Fix for AI Persistence query
                string todayKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var existingAI = await _supabase.From<AIResponseModel>()
                    .Where(x => x.UserId == userEmail && x.FeatureType == "FINANCIAL_GRAPH_ANALYSIS")
                    .Filter("date_key", Postgrest.Constants.Operator.Equals, todayKey)
                    .Limit(1).Get();

                if (existingAI.Models.Any()) model.AIAnalysis = existingAI.Models.First().ResponseText;
                else
                {
                    var aiData = _financeService.GetCashflowSummaryForAI(history);
                    model.AIAnalysis = await HttpContext.RequestServices.GetRequiredService<GeminiService>().GenerateDetailedCashflowAnalysis(Newtonsoft.Json.JsonConvert.SerializeObject(aiData));
                    await _financeService.SaveCashflowInsight(userEmail, model.AIAnalysis, "6M Weighted Trend");
                }
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