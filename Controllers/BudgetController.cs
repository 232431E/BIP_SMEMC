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
            Debug.WriteLine($"\n--- [AUTO-REALLOCATE START] Target Date: {month}/{year} ---");
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return Json(new { success = false, message = "Session expired" });

            try
            {
                // 1. Fetch All Historical Context
                var budgetRes = await _supabase.From<BudgetModel>().Filter("user_id", Postgrest.Constants.Operator.Equals, userEmail).Get();
                var allBudgets = budgetRes.Models;
                var categories = (await _supabase.From<CategoryModel>().Get()).Models;
                var expenseRoot = categories.FirstOrDefault(c => c.Name == "Expense");
                var tier1Cats = categories.Where(c => c.ParentId == expenseRoot?.Id).ToList();

                Debug.WriteLine($"[FETCH] Found {allBudgets.Count} total budget records for user.");

                // 2. Fetch Transactions for the specific month
                var start = new DateTime(year, month, 1);
                var end = start.AddMonths(1).AddDays(-1);
                var transactions = await _financeService.GetUserTransactions(userEmail, start, end);
                Debug.WriteLine($"[FETCH] Found {transactions.Count} transactions for {month}/{year}.");

                // Helper: Get carry-forward limit from previous months
                decimal GetLimit(int catId)
                {
                    var match = allBudgets.Where(b => b.CategoryId == catId)
                        .OrderByDescending(b => b.Year).ThenByDescending(b => b.Month)
                        .FirstOrDefault(b => b.Year < year || (b.Year == year && b.Month <= month));

                    if (match != null)
                        Debug.WriteLine($"   - Category {catId}: Borrowing limit ${match.BudgetAmount} from {match.Month}/{match.Year}");

                    return match?.BudgetAmount ?? 0;
                }

                decimal totalPool = 0;
                BudgetModel worstOffender = null;
                decimal maxDeficit = 0;
                var updatesMap = new Dictionary<int, BudgetModel>();

                // 3. Analysis Loop
                foreach (var cat in tier1Cats)
                {
                    int catId = cat.Id ?? 0;
                    decimal budgetLimit = GetLimit(catId);

                    // Calculate actual spend for this Parent Category (aggregating all sub-categories)
                    decimal actual = transactions.Where(t => {
                        var leaf = categories.FirstOrDefault(c => c.Id == t.CategoryId);
                        while (leaf != null && leaf.ParentId != expenseRoot?.Id && leaf.ParentId != 0)
                            leaf = categories.FirstOrDefault(c => c.Id == leaf.ParentId);
                        return leaf?.Id == catId;
                    }).Sum(t => t.Debit);

                    decimal variance = budgetLimit - actual;
                    Debug.WriteLine($"[ANALYSIS] {cat.Name} (ID:{catId}) | Limit: {budgetLimit} | Spent: {actual} | Var: {variance}");

                    if (variance > 50) // Surplus found
                    {
                        decimal newLimit = Math.Ceiling(actual * 1.05m); // Set new limit to actual + 5%
                        decimal surplus = budgetLimit - newLimit;
                        totalPool += surplus;

                        Debug.WriteLine($"   >> SURPLUS: ${surplus} added to pool.");

                        var bRecord = allBudgets.FirstOrDefault(b => b.CategoryId == catId && b.Month == month && b.Year == year)
                                      ?? new BudgetModel { UserId = userEmail, CategoryId = catId, Month = month, Year = year };

                        bRecord.BudgetAmount = newLimit;
                        updatesMap[catId] = bRecord;
                    }
                    else if (variance < 0) // Overspent
                    {
                        decimal deficit = Math.Abs(variance);
                        Debug.WriteLine($"   !! DEFICIT: ${deficit}");
                        if (deficit > maxDeficit)
                        {
                            maxDeficit = deficit;
                            worstOffender = allBudgets.FirstOrDefault(b => b.CategoryId == catId && b.Month == month && b.Year == year)
                                            ?? new BudgetModel { UserId = userEmail, CategoryId = catId, Month = month, Year = year, BudgetAmount = budgetLimit };
                        }
                    }
                }

                // 4. Distribution logic
                if (worstOffender != null && totalPool > 0)
                {
                    Debug.WriteLine($"[ACTION] Moving ${totalPool} to {worstOffender.CategoryId}. Previous: {worstOffender.BudgetAmount}");
                    worstOffender.BudgetAmount += totalPool;
                    updatesMap[worstOffender.CategoryId] = worstOffender;

                    // 5. SECURE SEQUENTIAL UPDATE (Prevents 21000 Error)
                    int successCount = 0;
                    foreach (var item in updatesMap.Values)
                    {
                        try
                        {
                            if (item.Id > 0)
                            {
                                await item.Update<BudgetModel>();
                            }
                            else
                            {
                                await _supabase.From<BudgetModel>().Insert(item);
                            }
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SAVE ERROR] Failed Cat {item.CategoryId}: {ex.Message}");
                        }
                    }

                    Debug.WriteLine($"--- [AUTO-REALLOCATE FINISHED] {successCount} rows updated ---");
                    return Json(new { success = true, message = $"Moved ${totalPool:N0} to cover overspending." });
                }

                return Json(new { success = false, message = "No surplus found to reallocate." });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FATAL ERROR] {ex.Message}\n{ex.StackTrace}");
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SmartIncreaseAll(int month, int year)
        {
            Debug.WriteLine($"\n--- [SMART INCREASE START] Target: {month}/{year} ---");
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail)) return Json(new { success = false });

            try
            {
                var budgetRes = await _supabase.From<BudgetModel>().Filter("user_id", Postgrest.Constants.Operator.Equals, userEmail).Get();
                var allBudgets = budgetRes.Models;
                var categories = (await _supabase.From<CategoryModel>().Get()).Models;
                var expenseRoot = categories.FirstOrDefault(c => c.Name == "Expense");
                var tier1Cats = categories.Where(c => c.ParentId == expenseRoot?.Id).ToList();

                var start = new DateTime(year, month, 1);
                var end = start.AddMonths(1).AddDays(-1);
                var transactions = await _financeService.GetUserTransactions(userEmail, start, end);

                int updatedCount = 0;
                foreach (var cat in tier1Cats)
                {
                    int catId = cat.Id ?? 0;
                    var match = allBudgets.Where(b => b.CategoryId == catId)
                        .OrderByDescending(b => b.Year).ThenByDescending(b => b.Month)
                        .FirstOrDefault(b => b.Year < year || (b.Year == year && b.Month <= month));

                    decimal currentLimit = match?.BudgetAmount ?? 0;

                    decimal actual = transactions.Where(t => {
                        var leaf = categories.FirstOrDefault(c => c.Id == t.CategoryId);
                        while (leaf != null && leaf.ParentId != expenseRoot?.Id && leaf.ParentId != 0)
                            leaf = categories.FirstOrDefault(c => c.Id == leaf.ParentId);
                        return leaf?.Id == catId;
                    }).Sum(t => t.Debit);

                    if (actual > currentLimit)
                    {
                        decimal newTarget = Math.Ceiling(actual / 1000m) * 1000;
                        if (newTarget <= actual) newTarget += 1000;

                        Debug.WriteLine($"[FIX] {cat.Name}: {currentLimit} -> {newTarget}");

                        var record = allBudgets.FirstOrDefault(b => b.CategoryId == catId && b.Month == month && b.Year == year)
                                     ?? new BudgetModel { UserId = userEmail, CategoryId = catId, Month = month, Year = year };

                        record.BudgetAmount = newTarget;

                        if (record.Id > 0) await record.Update<BudgetModel>();
                        else await _supabase.From<BudgetModel>().Insert(record);

                        updatedCount++;
                    }
                }

                return Json(new { success = true, message = $"Adjusted {updatedCount} overspent categories." });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMART ERROR] {ex.Message}");
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