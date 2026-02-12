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
                // 1. ANCHOR DATE & DATA
                var anchorDate = await _financeService.GetLatestTransactionDate(userEmail);
                model.LatestDataDate = anchorDate;

                // 2. FETCH HISTORY (6 Months for Trend Analysis)
                var startDate = anchorDate.AddMonths(-6);
                var history = await _financeService.GetUserTransactions(userEmail, startDate, anchorDate);

                // 1. METADATA & PREFERENCES
                var categoriesList = (await _supabase.From<CategoryModel>().Get()).Models;
                var catMap = categoriesList.ToDictionary(k => k.Id, v => v);
                // Populate ViewBags for UI
                var indList = (await _supabase.From<IndustryModel>().Get()).Models;
                var regList = (await _supabase.From<RegionModel>().Get()).Models;
                ViewBag.AllIndustries = indList.OrderBy(i => i.Name).ToList();
                ViewBag.AllRegions = regList.OrderBy(r => r.Name).ToList();

                
                // 3. SMART TREND CALCULATION
                // ---------------------------------------------------------
                decimal currentCash = 0;

                // Buckets for Trend Analysis
                decimal totalRev = 0;
                decimal totalFixed = 0;
                decimal totalVar = 0;
                decimal excludedOneOffs = 0; // Track anomalies

                // Keywords to detect anomalies that shouldn't affect future predictions
                var anomalyKeywords = new[] { "renovation", "deposit", "setup fee", "equipment purchase", "one-time" };

                var historicalPoints = new List<ChartDataPoint>();

                foreach (var t in history.OrderBy(t => t.Date))
                {
                    // A. Actual Balance (Always includes everything)
                    currentCash += (t.Credit - t.Debit);

                    historicalPoints.Add(new ChartDataPoint
                    {
                        Date = t.Date.ToString("yyyy-MM-dd"),
                        Actual = currentCash
                    });

                    // B. Trend Logic (Strict Filtering)
                    if (t.CategoryId.HasValue && catMap.TryGetValue(t.CategoryId.Value, out var cat))
                    {
                        string catName = cat.Name.ToLower();
                        string desc = (t.Description ?? "").ToLower();
                        int parentId = cat.ParentId ?? 0;

                        // 1. Ignore Non-Cash (Depreciation ID 88)
                        if (catName.Contains("depreciation") || catName.Contains("amortization")) continue;

                        if (t.Debit > 0) // Expense
                        {
                            // 2. Detect & Exclude One-Offs from Trend
                            if (anomalyKeywords.Any(k => catName.Contains(k) || desc.Contains(k)))
                            {
                                excludedOneOffs += t.Debit;
                                continue;
                            }

                            // 3. Handle Liabilities (Loan Repayments) - ID 278 is usually Long Term Liability
                            // Even though not an "Expense" in P&L, it burns cash.
                            if (parentId == 278 || catName.Contains("hire purchase") || catName.Contains("loan"))
                            {
                                totalFixed += t.Debit;
                            }
                            // 4. Fixed vs Variable
                            else if (catName.Contains("rent") || catName.Contains("salary") || catName.Contains("insurance"))
                            {
                                totalFixed += t.Debit;
                            }
                            else
                            {
                                totalVar += t.Debit;
                            }
                        }
                        else if (t.Credit > 0) // Revenue
                        {
                            totalRev += t.Credit;
                        }
                    }
                }

                model.CurrentBalance = currentCash;
                model.ChartData.AddRange(historicalPoints);

                // 4. GENERATE PROJECTION
                int months = 6;
                decimal avgRev = totalRev / months;
                decimal avgFixed = totalFixed / months;
                decimal varRatio = totalRev > 0 ? (totalVar / totalRev) : 0;

                // Debugging Trend
                Debug.WriteLine($"[TREND] Avg Rev: {avgRev:C}, Avg Fixed: {avgFixed:C}, Var Ratio: {varRatio:P1}");
                Debug.WriteLine($"[ANOMALY] Excluded from trend: {excludedOneOffs:C}");

                // Bridge Point
                model.ChartData.Add(new ChartDataPoint
                {
                    Date = anchorDate.ToString("yyyy-MM-dd"),
                    Actual = null,
                    Predicted = currentCash
                });

                // 30 Day Forward Projection
                decimal projBalance = currentCash;
                decimal dailyRev = avgRev / 30m;
                decimal dailyFixed = avgFixed / 30m;

                for (int i = 1; i <= 30; i++)
                {
                    decimal dailyVar = dailyRev * varRatio;
                    decimal dailyNet = dailyRev - dailyFixed - dailyVar;
                    projBalance += dailyNet;

                    model.ChartData.Add(new ChartDataPoint
                    {
                        Date = anchorDate.AddDays(i).ToString("yyyy-MM-dd"),
                        Actual = null,
                        Predicted = Math.Round(projBalance, 2)
                    });
                }

                model.MonthlyFixedBurn = avgFixed;
                model.VariableCostRatio = varRatio;
                model.ProjectedCashIn30Days = projBalance;

                // Runway Calc
                decimal burnRate = (avgFixed + (avgRev * varRatio)) - avgRev;
                model.CashRunway = burnRate > 0 && currentCash > 0
                    ? $"{currentCash / burnRate:N1} Months"
                    : "Stable (Cash Positive)";

                // 5. FETCH NEWS (OPTIMIZED)
                // Only fetch if preferences exist, otherwise don't slam the DB.
                var userRes = await _supabase.From<UserModel>().Where(u => u.Email == userEmail).Get();
                var userPrefs = userRes.Models.FirstOrDefault();
                model.UserPreferences = userPrefs;

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
            }

            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] {ex.Message}");
            }

            return View(model);
        }

        // Helper to update prefs via AJAX
        [HttpPost]
        public async Task<IActionResult> UpdatePreferences(List<string> industries, List<string> regions)
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return Unauthorized();

            var userRes = await _supabase.From<UserModel>().Where(u => u.Email == userEmail).Get();
            var user = userRes.Models.FirstOrDefault();

            if (user != null)
            {
                user.Industries = industries ?? new List<string>();
                user.Regions = regions ?? new List<string>();
                await user.Update<UserModel>();
                return Json(new { success = true });
            }
            return Json(new { success = false });
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