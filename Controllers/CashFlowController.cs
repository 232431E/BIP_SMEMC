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
            Debug.WriteLine("=== [START] CashFlow Page Load ===");
            var model = new CashFlowViewModel();
            var userEmail = User.Identity?.Name ?? "dummy@sme.com";
            try
            {
                // 1. DYNAMIC ANCHOR: Find the most recent date in the ledger
                var latestDate = await _financeService.GetLatestTransactionDate(userEmail);
                // Set sim date to match data reality
                var currentSimDate = latestDate;

                // 1. Fetch Metadata for Checkboxes
                var industries = (await _supabase.From<IndustryModel>().Get()).Models;
                var regions = (await _supabase.From<RegionModel>().Get()).Models;
                Debug.WriteLine($"[DB] Industries: {industries.Count}, Regions: {regions.Count}");
                // FIX: Populate ViewBag so the View doesn't crash on @foreach
                ViewBag.AllIndustries = industries;
                ViewBag.AllRegions = regions;

                // ---------------------------------------------------------
                // NEWS RETRIEVAL LOGIC (UPDATED)
                // ---------------------------------------------------------

                // 1. Get User Preferences
                var userRes = await _supabase.From<UserModel>().Where(u => u.Email == userEmail).Get();
                var userPrefs = userRes.Models.FirstOrDefault();
                model.UserPreferences = userPrefs; // For Checkboxes

                // 2. Fetch ALL News from DB (Since DB is small ~100 items due to retention)
                var allNewsRes = await _supabase.From<NewsArticleModel>()
                    .Order("date", Postgrest.Constants.Ordering.Descending)
                    .Limit(100)
                    .Get();

                var allNews = allNewsRes.Models;

                // 3. Filter in Memory (C#) - THIS IS THE FIX YOU ASKED FOR
                // This handles "OR" logic: (Has Industry X OR Has Region Y)
                List<NewsArticleModel> filteredNews;

                if (userPrefs != null &&
                   ((userPrefs.Industries != null && userPrefs.Industries.Any()) ||
                    (userPrefs.Regions != null && userPrefs.Regions.Any())))
                {
                    var pInds = userPrefs.Industries ?? new List<string>();
                    var pRegs = userPrefs.Regions ?? new List<string>();

                    filteredNews = allNews.Where(n =>
                        // Article Industry overlaps with User Industry Prefs
                        (n.Industries != null && n.Industries.Intersect(pInds, StringComparer.OrdinalIgnoreCase).Any()) ||
                        // Article Region overlaps with User Region Prefs
                        (n.Regions != null && n.Regions.Intersect(pRegs, StringComparer.OrdinalIgnoreCase).Any())
                    ).ToList();
                }
                else
                {
                    // If no preferences set, show everything
                    filteredNews = allNews.ToList();
                }

                // Take top 20 after filtering
                model.News = filteredNews.Take(20).ToList();

                // ---------------------------------------------------------
                // TRIGGER BACKGROUND REFRESH IF EMPTY
                // ---------------------------------------------------------
                if (!model.News.Any())
                {
                    // Call the service manually if DB is empty
                    var newsService = HttpContext.RequestServices.GetService<NewsBGService>();
                    if (newsService != null)
                    {
                        await newsService.TriggerNewsCycle();
                        // Refetch one last time
                        model.News = (await _supabase.From<NewsArticleModel>().Order("date", Postgrest.Constants.Ordering.Descending).Limit(12).Get()).Models;
                    }
                }
                // Populate ViewBag for Filter Modal
                var indList = (await _supabase.From<IndustryModel>().Get()).Models;
                var regList = (await _supabase.From<RegionModel>().Get()).Models;
                ViewBag.AllIndustries = indList.OrderBy(i => i.Name).ToList();
                ViewBag.AllRegions = regList.OrderBy(r => r.Name).ToList();

                // Get Outlooks
                var outlookRes = await _supabase.From<NewsOutlookModel>().Get();
                // Filter outlooks relevant to user
                var relOutlook = outlookRes.Models.Where(o =>
                     (userPrefs?.Industries?.Contains(o.Industry) == true) ||
                     (userPrefs?.Regions?.Contains(o.Region) == true)
                ).ToList();
                ViewBag.Outlooks = relOutlook;

                // 1. Retrieve Data (Ordered by Date ASCENDING for running total calculation)
                var history = await _financeService.GetUserTransactions(userEmail, latestDate.AddMonths(-6), latestDate);
                // Sort Ascending to calculate the flow correctly
                var sortedHistory = history.OrderBy(t => t.Date).ToList();

                // 2. Manual Running Balance Calculation (If DB balance is 0)
                decimal runningTotal = 0;
                foreach (var trans in sortedHistory)
                {
                    runningTotal += (trans.Credit - trans.Debit);
                    trans.Balance = runningTotal;
                }

                // DEBUG: Log retrieval counts
                Debug.WriteLine($"[DEBUG] Total records for {userEmail}: {history.Count}");
                var monthsPresent = history.Select(t => t.Date.Month).Distinct();
                Debug.WriteLine($"[DEBUG] Months found in data: {string.Join(", ", monthsPresent)}");
                Debug.WriteLine($"[CASHFLOW DEBUG] Total History Count: {history.Count}");
                var expenses = history.Where(t => t.Debit > 0 && t.Date.Year == 2023).ToList();
                Debug.WriteLine($"[CASHFLOW DEBUG] 2023 Expenses: {expenses.Count}");

                // 3. Filter for Chart (Last 4 months of history)
                model.ChartData = sortedHistory
                .Where(t => t.Date >= latestDate.AddMonths(-4))
                .Select(t => new ChartDataPoint
                {
                    Date = t.Date.ToString("yyyy-MM-dd"),
                    Actual = t.Balance,
                    Predicted = null
                }).ToList();

                // 4. Current Balance is the last calculated point
                model.CurrentBalance = runningTotal;
                Debug.WriteLine($"[DEBUG] Manual Running Balance: {model.CurrentBalance:N2}");

                // 5. Generate Predictions (Next 30 Days)
                decimal dailyNet = _financeService.CalculateAvgDailyNet(history, latestDate.Year);
                decimal projectionPointer = model.CurrentBalance;
                for (int i = 1; i <= 30; i++)
                {
                    projectionPointer += dailyNet;
                    model.ChartData.Add(new ChartDataPoint
                    {
                        Date = latestDate.AddDays(i).ToString("yyyy-MM-dd"),
                        Actual = null,
                        Predicted = projectionPointer
                    });
                }
                // NEW: AI Analysis Integration
                try
                {
                    // Check if we have enough data to analyze
                    if (history.Count > 0)
                    {
                        var ai = HttpContext.RequestServices.GetRequiredService<GeminiService>();
                        // Pass the history to Gemini
                        model.AIAnalysis = await ai.AnalyzeFinancialTrends(history);
                    }
                    else
                    {
                        model.AIAnalysis = "Not enough data for AI analysis.";
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CONTROLLER AI ERROR] {ex.Message}");
                    model.AIAnalysis = " AI is taking a break. (Error connecting to service)";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CRITICAL CASHFLOW PAGE ERROR] {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
            // 5. Retrieve News from DB
            //var userProfile = await _supabase.From<UserModel>().Where(u => u.Email == userEmail).Get();
            //model.News = await GetFilteredNews(userProfile.Models.FirstOrDefault());

            return View(model);
        }
        //// 1. Calculate Seasonality (Average growth for this specific month in previous years)
        //double seasonalFactor = _financeService.CalculateSeasonality(history, DateTime.Now.Month);

        //// 2. Identify Constant Supplier Payments
        //var recurringExpenses = history
        //    .Where(t => t.Type == "Expense")
        //    .GroupBy(t => t.Description)
        //    .Where(g => g.Count() >= 3) // Appears at least 3 times
        //    .Select(g => g.Average(x => x.Debit))
        //    .Sum();
        [HttpPost]
        public async Task<IActionResult> UpdatePreferences(List<string> industries, List<string> regions)
        {
            var userEmail = User.Identity?.Name ?? "dummy@sme.com";

            // ENHANCED DEBUGGING
            Debug.WriteLine("--- [CRUD START] UpdatePreferences ---");
            Debug.WriteLine($"[USER] {userEmail}");
            Debug.WriteLine($"[DATA-IND] {(industries != null ? string.Join(", ", industries) : "NULL")}");
            Debug.WriteLine($"[DATA-REG] {(regions != null ? string.Join(", ", regions) : "NULL")}");
            try
            {
                // 1. Fetch the actual existing record first
                var response = await _supabase.From<UserModel>()
                    .Where(u => u.Email == userEmail)
                    .Get();
                var user = response.Models.FirstOrDefault();

                if (user == null)
                {
                    Debug.WriteLine($"[ERROR] User {userEmail} not found in DB. Cannot update.");
                    return Json(new { success = false, error = "User not found" });
                }
                // 2. Update the properties on the TRACKED object
                // Note: We use .ToList() to ensure we aren't passing a reference that might get cleared
                user.Industries = industries?.ToList() ?? new List<string>();
                user.Regions = regions?.ToList() ?? new List<string>();

                Debug.WriteLine($"[PRE-SAVE CHECK] Ind Count: {user.Industries.Count}, Reg Count: {user.Regions.Count}");

                // 3. Use Update() on the specific object
                var updateRes = await user.Update<UserModel>();

                Debug.WriteLine($"[SUPABASE RAW] {updateRes.ResponseMessage.Content}"); // This shows the JSON returned by Supabase

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CRITICAL DB ERROR] {ex.Message}");
                Debug.WriteLine($"[STACKTRACE] {ex.StackTrace}");
                return Json(new { success = false, error = ex.Message });
            }
        }

        private async Task<List<NewsArticleModel>> GetFilteredNews(UserModel user)
        {
            // ISupabaseTable requires the underlying Table to be handled via var for chaining
            var query = _supabase.From<NewsArticleModel>().Select("*");

            if (user?.Industries != null && user.Industries.Any())
            {
                // Use overlap operator (&&) for text[]
                query = query.Filter("industries", Postgrest.Constants.Operator.Overlap, user.Industries);
            }

            var response = await query
                .Order("date", Postgrest.Constants.Ordering.Descending)
                .Limit(12)
                .Get();

            return response.Models;
        }
    }
}
