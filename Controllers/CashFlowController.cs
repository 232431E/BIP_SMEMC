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
            var model = new CashFlowViewModel();
            var userEmail = User.Identity?.Name ?? "dummy@sme.com";
            // 1. DYNAMIC ANCHOR: Find the most recent date in the ledger
            var latestDate = await _financeService.GetLatestTransactionDate(userEmail);
            // Set sim date to match data reality
            var currentSimDate = latestDate;

            // 1. Fetch Metadata for Checkboxes
            var industries = (await _supabase.From<IndustryModel>().Get()).Models;
            var regions = (await _supabase.From<RegionModel>().Get()).Models;

            // FIX: Populate ViewBag so the View doesn't crash on @foreach
            ViewBag.AllIndustries = industries;
            ViewBag.AllRegions = regions;

            // 2. Fetch User Profile (CRUD: Read)
            var userRes = await _supabase.From<UserModel>().Where(u => u.Email == userEmail).Get();
            model.UserPreferences = userRes.Models.FirstOrDefault();


            // Attempt 1: Get news based on User Preferences
            var newsRes = await _supabase.From<NewsArticleModel>()
                .Filter("industries", Postgrest.Constants.Operator.Overlap, model.UserPreferences?.Industries ?? new List<string>())
                .Order("date", Postgrest.Constants.Ordering.Descending)
                .Limit(12).Get(); 
            model.News = newsRes.Models;
            // FALLBACK: If no news matches user preferences, show ALL recent news
            if (!model.News.Any())
            {
                Debug.WriteLine("[INFO] No news matches user preferences. Falling back to Global/Recent news.");
                var globalNews = await _supabase.From<NewsArticleModel>()
                    .Order("date", Postgrest.Constants.Ordering.Descending)
                    .Limit(12).Get();
                model.News = globalNews.Models;
            }
            // TRIGGER REFRESH: If still no news at all, run the background process
            if (!model.News.Any())
            {
                int attempts = 0;
                bool success = false;

                while (attempts < 3 && !success)
                {
                    attempts++;
                    Debug.WriteLine($"[RETRY] Attempt {attempts} of 3 to fetch news...");
                    try
                    {
                        var ai = HttpContext.RequestServices.GetRequiredService<GeminiService>();
                        var newsService = HttpContext.RequestServices.GetRequiredService<IEnumerable<IHostedService>>()
                                            .OfType<NewsBGService>().FirstOrDefault();

                        if (newsService != null)
                        {
                            await newsService.RunDailyNewsCycle(_supabase, ai);
                            success = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[RETRY ERROR] Attempt {attempts} failed: {ex.Message}");
                    }
                }

                if (!success)
                {
                    ViewBag.ErrorMessage = "Max amount of times today for articles, unable to handle the request. Gemini cannot handle it without having the news. Please try again later.";
                    Debug.WriteLine("[TIMEOUT] Failed to retrieve news after 3 attempts.");
                }
                else
                {
                    // Re-fetch now that we have data
                    model.News = (await _supabase.From<NewsArticleModel>().Get()).Models;
                }
            }

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
