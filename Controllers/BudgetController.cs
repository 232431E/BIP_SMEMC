using BIP_SMEMC.Models;
using BIP_SMEMC.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace BIP_SMEMC.Controllers
{
    public class BudgetController : Controller
    {
        private readonly Supabase.Client _supabase;
        private readonly FinanceService _financeService;

        public BudgetController(Supabase.Client supabase, FinanceService financeService)
        {
            _supabase = supabase;
            _financeService = financeService;
        }

        public async Task<IActionResult> Index()
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return RedirectToAction("Login", "Account");

            // 1. Get Data Range
            var latestDate = await _financeService.GetLatestTransactionDate(userEmail);
            var startDate = latestDate.AddMonths(-5);

            // 2. Fetch Data
            var allTrans = await _financeService.GetUserTransactions(userEmail, startDate, latestDate);
            var catRes = await _supabase.From<CategoryModel>().Get();
            var budgetRes = await _supabase.From<BudgetModel>().Where(b => b.UserId == userEmail).Get();

            // 3. Map Categories
            var expenseRoot = catRes.Models.FirstOrDefault(c => c.Name.Equals("Expense", StringComparison.OrdinalIgnoreCase));

            var processedExpenses = allTrans
                .Where(t => t.Debit > 0)
                .Select(t => {
                    var leaf = catRes.Models.FirstOrDefault(c => c.Id == t.CategoryId);
                    var t1 = leaf;
                    while (t1 != null && t1.ParentId != expenseRoot?.Id && t1.ParentId != 0)
                        t1 = catRes.Models.FirstOrDefault(c => c.Id == t1.ParentId);

                    t.ParentCategoryName = t1?.Name ?? "General Expense";
                    return t;
                }).ToList();

            var model = new BudgetViewModel
            {
                AllTransactions = processedExpenses,
                // Only pass Tier 1 categories for the Budget dropdown
                ExpenseCategories = catRes.Models.Where(c => c.ParentId == expenseRoot?.Id).ToList(),
                BudgetRecords = budgetRes.Models,
                LatestDataDate = latestDate
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateBudget(int categoryId, decimal amount, int month, int year)
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return RedirectToAction("Login", "Account");
            try
            {
                // Check if a specific budget override already exists for this Month/Year
                var existing = await _supabase.From<BudgetModel>()
                    .Filter("user_id", Postgrest.Constants.Operator.Equals, userEmail)
                    .Filter("category_id", Postgrest.Constants.Operator.Equals, categoryId)
                    .Filter("month", Postgrest.Constants.Operator.Equals, month)
                    .Filter("year", Postgrest.Constants.Operator.Equals, year)
                    .Get();

                var record = existing.Models.FirstOrDefault();

                if (record != null)
                {
                    // Update existing override
                    record.BudgetAmount = amount;
                    await record.Update<BudgetModel>();
                }
                else
                {
                    // Create new override
                    var newBudget = new BudgetModel
                    {
                        UserId = userEmail,
                        CategoryId = categoryId,
                        Month = month,
                        Year = year,
                        BudgetAmount = amount
                    };
                    await _supabase.From<BudgetModel>().Insert(newBudget);
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // [POST] Auto Reallocate: Finds worst offender and moves budget from best saver
        [HttpPost]
        public async Task<IActionResult> AutoReallocate(int month, int year)
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return Json(new { success = false, message = "Session expired" });

            // 1. Fetch Budget & Actuals
            var budgetRes = await _supabase.From<BudgetModel>()
                .Where(b => b.UserId == userEmail && b.Month == month && b.Year == year)
                .Get();

            // We need actuals to know who is over/under
            var transactions = await _financeService.GetUserTransactions(userEmail,
                new DateTime(year, month, 1),
                new DateTime(year, month, DateTime.DaysInMonth(year, month)));

            var budgets = budgetRes.Models;
            var varianceList = new List<(BudgetModel Budget, decimal Actual, decimal Variance)>();

            foreach (var b in budgets)
            {
                decimal actual = transactions.Where(t => t.CategoryId == b.CategoryId).Sum(t => t.Debit);
                decimal variance = b.BudgetAmount - actual; // Negative means overspent
                varianceList.Add((b, actual, variance));
            }

            // 2. Identify Candidates
            var worstOffender = varianceList.OrderBy(v => v.Variance).FirstOrDefault(); // Most negative
            var bestSaver = varianceList.OrderByDescending(v => v.Variance).FirstOrDefault(); // Most positive

            if (worstOffender.Budget == null || bestSaver.Budget == null)
                return Json(new { success = false, message = "Not enough budget data." });

            if (worstOffender.Variance >= 0)
                return Json(new { success = false, message = "Good news! No categories are over budget." });

            if (bestSaver.Variance <= 0)
                return Json(new { success = false, message = "No categories have surplus to reallocate." });

            // 3. Calculate Transfer Amount
            decimal needed = Math.Abs(worstOffender.Variance);
            decimal available = bestSaver.Variance;
            decimal transferAmount = Math.Min(needed, available);

            // 4. Update DB (Batch Logic via Loop for safety)
            worstOffender.Budget.BudgetAmount += transferAmount;
            bestSaver.Budget.BudgetAmount -= transferAmount;

            await worstOffender.Budget.Update<BudgetModel>();
            await bestSaver.Budget.Update<BudgetModel>();

            // 5. Fetch Category Names for Message
            var cats = (await _supabase.From<CategoryModel>().Get()).Models;
            string fromName = cats.FirstOrDefault(c => c.Id == bestSaver.Budget.CategoryId)?.Name ?? "Saver";
            string toName = cats.FirstOrDefault(c => c.Id == worstOffender.Budget.CategoryId)?.Name ?? "Spender";

            return Json(new
            {
                success = true,
                message = $"Reallocated ${transferAmount:N0} from {fromName} to {toName}."
            });
        }

        // [POST] Quick Increase: Rounds up to next 1000
        [HttpPost]
        public async Task<IActionResult> QuickIncreaseBudget(int categoryId, int month, int year, decimal currentSpent)
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return Json(new { success = false });

            // Logic: If spent is 1200, target is 2000. If 800, target 1000.
            decimal newTarget = Math.Ceiling(currentSpent / 1000m) * 1000;
            if (newTarget == currentSpent) newTarget += 1000; // Ensure it's always an increase if exactly on 1000

            // Check if record exists
            var existing = await _supabase.From<BudgetModel>()
                .Where(b => b.UserId == userEmail && b.CategoryId == categoryId && b.Month == month && b.Year == year)
                .Get();

            var record = existing.Models.FirstOrDefault();

            if (record != null)
            {
                record.BudgetAmount = newTarget;
                await record.Update<BudgetModel>();
            }
            else
            {
                var newBudget = new BudgetModel
                {
                    UserId = userEmail,
                    CategoryId = categoryId,
                    Month = month,
                    Year = year,
                    BudgetAmount = newTarget,
                    CreatedAt = DateTime.UtcNow
                };
                await _supabase.From<BudgetModel>().Insert(newBudget);
            }

            return Json(new { success = true });
        }
    }
}