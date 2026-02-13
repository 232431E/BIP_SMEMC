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
            try 
            {
                // 1. Get Data Range
                var latestDate = await _financeService.GetLatestTransactionDate(userEmail);
                var startDate = latestDate.AddMonths(-5);

                // 2. Fetch Data
                var allTrans = await _financeService.GetUserTransactions(userEmail, startDate, latestDate);
                var catRes = await _supabase.From<CategoryModel>().Get();
                // Use standard Filter to avoid Logic Tree errors
                var budgetRes = await _supabase.From<BudgetModel>()
                    .Filter("user_id", Postgrest.Constants.Operator.Equals, userEmail)
                    .Get();
                // DEBUG LOGS
                Debug.WriteLine($"[DEBUG] Transactions Count: {allTrans?.Count ?? 0}");
                Debug.WriteLine($"[DEBUG] Categories Count: {catRes.Models?.Count ?? 0}");
                Debug.WriteLine($"[DEBUG] Budgets Count: {budgetRes.Models?.Count ?? 0}");

                // 3. Map Categories
                var expenseRoot = catRes.Models.FirstOrDefault(c => c.Name.Equals("Expense", StringComparison.OrdinalIgnoreCase));
                if (expenseRoot == null) Debug.WriteLine("[DEBUG ERROR] Root 'Expense' category not found!");

                var processedExpenses = allTrans
                .Where(t => t.Debit > 0)
                .Select(t => {
                    var leaf = catRes.Models.FirstOrDefault(c => c.Id == t.CategoryId);
                    var t1 = leaf;
                    int safety = 0;
                    while (t1 != null && t1.ParentId != expenseRoot?.Id && t1.ParentId != 0 && safety < 10)
                    {
                        t1 = catRes.Models.FirstOrDefault(c => c.Id == t1.ParentId);
                        safety++;
                    }
                    t.ParentCategoryName = t1?.Name ?? "General Expense";
                    return t;
                }).ToList();

                Debug.WriteLine($"[DEBUG] Processed Expenses: {processedExpenses.Count}");
                // Inside BudgetController.cs
                var model = new BudgetViewModel
                {

                    AllTransactionsDTO = processedExpenses.Select(t => new TransactionDTO
                    {
                        Date = t.Date.ToString("yyyy-MM-dd"),
                        Description = t.Description ?? "",
                        Amount = t.Debit,
                        CategoryName = t.ParentCategoryName
                    }).ToList(),

                    ExpenseCategoriesDTO = catRes.Models
                .Where(c => c.ParentId == expenseRoot?.Id)
                .Select(c => new CategoryDTO { Id = c.Id ?? 0, Name = c.Name })
                .ToList(),

                    // This is critical for the JS Health list to display data
                    BudgetsDTO = budgetRes.Models.Select(b => new BudgetDTO
                    {
                        CategoryId = b.CategoryId,
                        BudgetAmount = b.BudgetAmount,
                        Month = b.Month,
                        Year = b.Year
                    }).ToList(),

                    LatestDataDate = latestDate
                };
                Debug.WriteLine($"[FINAL CHECK] Model has {model.AllTransactionsDTO.Count} DTOs");
                return View(model);
            }
                catch (Exception ex) {
                Debug.WriteLine($"[FATAL INDEX ERROR] {ex.Message} \n {ex.StackTrace}");
                return Content($"Error: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateBudget(int categoryId, decimal amount, int month, int year)
        {
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return Unauthorized();

            try
            {
                // Use clean Filter chain to avoid PGRST100 Logic Tree error
                var existing = await _supabase.From<BudgetModel>()
                    .Filter("user_id", Postgrest.Constants.Operator.Equals, userEmail)
                    .Filter("category_id", Postgrest.Constants.Operator.Equals, categoryId)
                    .Filter("month", Postgrest.Constants.Operator.Equals, month)
                    .Filter("year", Postgrest.Constants.Operator.Equals, year)
                    .Get();

                var record = existing.Models.FirstOrDefault();

                if (record != null)
                {
                    record.BudgetAmount = amount;
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
                        BudgetAmount = amount,
                        CreatedAt = DateTime.UtcNow
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
        // [POST] Auto Reallocate: Pools ALL excess and moves to BIGGEST deficit
        [HttpPost]
        public async Task<IActionResult> AutoReallocate(int month, int year)
        {
            Debug.WriteLine($"[AUTO-REALLOCATE] Started for {month}/{year}");
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return Json(new { success = false, message = "Session expired" });

            try
            {
                // 1. Fetch Existing Budgets
                // FIX: Use .Filter with string-formatted date to avoid logic tree error
                var budgetRes = await _supabase.From<BudgetModel>()
                    .Filter("user_id", Postgrest.Constants.Operator.Equals, userEmail)
                    .Filter("month", Postgrest.Constants.Operator.Equals, month)
                    .Filter("year", Postgrest.Constants.Operator.Equals, year)
                    .Get();

                var budgets = budgetRes.Models;
                if (!budgets.Any())
                {
                    Debug.WriteLine("[AUTO-REALLOCATE] No budgets found.");
                    return Json(new { success = false, message = "No budgets set for this month yet." });
                }
                // 2. Fetch Actuals
                var start = new DateTime(year, month, 1);
                var end = start.AddMonths(1).AddDays(-1);
                var transactions = await _financeService.GetUserTransactions(userEmail, start, end);

                // Fetch Categories for Naming
                var categories = (await _supabase.From<CategoryModel>().Get()).Models;
                var expenseRoot = categories.FirstOrDefault(c => c.Name == "Expense");

                // Helper to map transaction to Parent Category ID
                int GetParentCatId(int? leafId)
                {
                    if (!leafId.HasValue) return 0;
                    var leaf = categories.FirstOrDefault(c => c.Id == leafId);
                    while (leaf != null && leaf.ParentId != expenseRoot?.Id && leaf.ParentId != 0)
                        leaf = categories.FirstOrDefault(c => c.Id == leaf.ParentId);
                    return leaf?.Id ?? 0;
                }

                // 3. Logic: Pool Excess & Find Worst Offender
                decimal totalPool = 0;
                BudgetModel worstOffender = null;
                decimal maxDeficit = 0;
                var updates = new List<BudgetModel>();

                Debug.WriteLine("[AUTO-REALLOCATE] Analyzing Categories...");

                foreach (var b in budgets) //under budget
                {
                    // IMPORTANT: Ensure you are calculating the actual spend for the CORRECT CategoryId
                    decimal actual = transactions.Where(t => GetParentCatId(t.CategoryId) == b.CategoryId).Sum(t => t.Debit);
                    decimal variance = b.BudgetAmount - actual; 
                    Debug.WriteLine($" - Cat {b.CategoryId}: Budget {b.BudgetAmount}, Actual {actual}, Var {variance}");
                    if (variance > 0)
                    {
                        decimal newLimit = Math.Ceiling(actual * 1.05m);
                        totalPool += (b.BudgetAmount - newLimit);
                        b.BudgetAmount = newLimit;
                        updates.Add(b);
                    }
                    else if (variance < 0) //overbudget
                    {
                        decimal deficit = Math.Abs(variance);
                        if (deficit > maxDeficit)
                        {
                            maxDeficit = deficit;
                            worstOffender = b;
                        }
                    }
                }

                Debug.WriteLine($"[AUTO-REALLOCATE] Pool: {totalPool:C}, Worst Deficit: {maxDeficit:C}");

                if (worstOffender != null && totalPool > 0)
                {
                    worstOffender.BudgetAmount += totalPool;
                    if (!updates.Any(u => u.Id == worstOffender.Id)) updates.Add(worstOffender);

                    Debug.WriteLine($"[POOL] Moving {totalPool:C} to cover {worstOffender.CategoryId}");
                    await _supabase.From<BudgetModel>().Upsert(updates);

                    string worstName = categories.FirstOrDefault(c => c.Id == worstOffender.CategoryId)?.Name ?? "Unknown";
                    return Json(new { success = true, message = $"Moved {totalPool:C} to cover '{worstName}'." });
                }
                return Json(new { success = false, message = "No deficit found or no surplus available to move." });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTO-REALLOCATE FATAL] {ex.Message}");
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }
        // [POST] Smart Increase: Increase ALL overspent budgets to nearest 1000
        [HttpPost]
        public async Task<IActionResult> SmartIncreaseAll(int month, int year)
        {
            Debug.WriteLine($"[SMART INCREASE] Started for {month}/{year}");
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return Json(new { success = false });

            try
            {
                var start = new DateTime(year, month, 1);
                var end = start.AddMonths(1).AddDays(-1);
                var transactions = await _financeService.GetUserTransactions(userEmail, start, end);
                var categories = (await _supabase.From<CategoryModel>().Get()).Models;
                var expenseRoot = categories.FirstOrDefault(c => c.Name == "Expense");

                // Fetch existing budgets
                // FIX: Use explicit Filters to avoid PGRST100 Logic Tree error
                var budgetRes = await _supabase.From<BudgetModel>()
                    .Filter("user_id", Postgrest.Constants.Operator.Equals, userEmail)
                    .Filter("month", Postgrest.Constants.Operator.Equals, month)
                    .Filter("year", Postgrest.Constants.Operator.Equals, year)
                    .Get();

                var budgets = budgetRes.Models;

                var tier1Cats = categories.Where(c => c.ParentId == expenseRoot?.Id).ToList();
                int count = 0;

                foreach (var cat in tier1Cats)
                {
                    decimal actual = 0;
                    foreach (var t in transactions)
                    {
                        var leaf = categories.FirstOrDefault(c => c.Id == t.CategoryId);
                        while (leaf != null && leaf.ParentId != expenseRoot?.Id && leaf.ParentId != 0)
                            leaf = categories.FirstOrDefault(c => c.Id == leaf.ParentId);

                        if (leaf != null && leaf.Id == cat.Id) actual += t.Debit;
                    }

                    var existingBudget = budgets.FirstOrDefault(b => b.CategoryId == cat.Id);
                    decimal currentLimit = existingBudget?.BudgetAmount ?? 0;

                    if (actual > currentLimit)
                    {
                        decimal newTarget = Math.Ceiling(actual / 1000m) * 1000;
                        if (newTarget <= actual) newTarget += 1000;

                        if (existingBudget != null)
                        {
                            // UPDATE existing
                            existingBudget.BudgetAmount = newTarget;
                            await existingBudget.Update<BudgetModel>();
                        }
                        else
                        {
                            // INSERT new
                            var newBudget = new BudgetModel
                            {
                                UserId = userEmail,
                                CategoryId = cat.Id ?? 0,
                                Month = month,
                                Year = year,
                                BudgetAmount = newTarget,
                                CreatedAt = DateTime.UtcNow
                            };
                            await _supabase.From<BudgetModel>().Insert(newBudget);
                        }
                        count++;
                    }
                }

                return Json(new { success = true, message = $"Updated {count} categories." });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[INCREASE ERROR] {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }
        public class TransactionDTO
        {
            public string Date { get; set; }
            public string Description { get; set; }
            public decimal Amount { get; set; }
            public string CategoryName { get; set; }
        }

        public class CategoryDTO
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
        // Add this class inside the Controller or Models folder
        public class BudgetDTO
        {
            public int CategoryId { get; set; }
            public decimal BudgetAmount { get; set; }
            public int Month { get; set; }
            public int Year { get; set; }
        }
    }
}