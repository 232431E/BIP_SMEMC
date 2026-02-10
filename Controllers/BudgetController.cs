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
            var userEmail = User.Identity?.Name ?? "dummy@sme.com";
            // 1. DYNAMIC DATE: Find the latest transaction date in the DB
            var latestDate = await _financeService.GetLatestTransactionDate(userEmail);
            // Set range: 1 year back from the latest date to ensure the chart is full
            var startDate = latestDate.AddMonths(-12);

            // 2. FETCH DATA IN BATCH: Get all transactions for the relevant window
            var allTrans = await _financeService.GetUserTransactions(userEmail, startDate, latestDate);
            var catRes = await _supabase.From<CategoryModel>().Get();
            var budgetRes = await _supabase.From<BudgetModel>().Where(b => b.UserId == userEmail).Get();

            // 3. MAP CATEGORIES (Logic remains the same but uses dynamic data)
            var expenseRoot = catRes.Models.FirstOrDefault(c => c.Name.Equals("Expense", StringComparison.OrdinalIgnoreCase));
            var tier1Categories = catRes.Models.Where(c => c.ParentId == expenseRoot?.Id).ToList();

            var processedExpenses = allTrans
        .Where(t => t.Debit > 0)
        .Select(t => {
            var leaf = catRes.Models.FirstOrDefault(c => c.Id == t.CategoryId);
            var t1 = leaf;
            while (t1 != null && t1.ParentId != expenseRoot?.Id && t1.ParentId != 0)
                t1 = catRes.Models.FirstOrDefault(c => c.Id == t1.ParentId);

            t.ParentCategoryName = t1?.Name ?? "General Expense";
            return t;
        })
        .ToList();

            // 4. PREPARE VIEWMODEL
            var model = new BudgetViewModel
            {
                AllTransactions = processedExpenses,
                ExpenseCategories = tier1Categories,
                BudgetRecords = budgetRes.Models,
                // Pass the dynamic "latest" date to JS so it knows where to start the chart
                LatestDataDate = latestDate
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> SaveBudget(int categoryId, decimal amount, int month, int year)
        {
            var userEmail = User.Identity?.Name ?? "dummy@sme.com";
            var budget = new BudgetModel
            {
                UserId = userEmail,
                CategoryId = categoryId,
                Month = month,
                Year = year,
                BudgetAmount = amount
            };
            await _supabase.From<BudgetModel>().Upsert(budget);
            return Json(new { success = true });
        }
        [HttpPost]
        public async Task<IActionResult> UpdateBudget(int categoryId, decimal amount, int month, int year)
        {
            var userEmail = User.Identity?.Name ?? "dummy@sme.com";

            try
            {
                // 1. Check existing using safe filters
                var existing = await _supabase.From<BudgetModel>()
                    .Filter("user_id", Postgrest.Constants.Operator.Equals, userEmail)
                    .Filter("category_id", Postgrest.Constants.Operator.Equals, categoryId)
                    .Filter("month", Postgrest.Constants.Operator.Equals, month)
                    .Filter("year", Postgrest.Constants.Operator.Equals, year)
                    .Get();

                var budget = new BudgetModel
                {
                    UserId = userEmail,
                    CategoryId = categoryId,
                    Month = month,
                    Year = year,
                    BudgetAmount = amount
                };

                if (existing.Models.Any())
                {
                    budget.Id = existing.Models.First().Id;
                    await _supabase.From<BudgetModel>().Update(budget);
                    Debug.WriteLine($"[CRUD] Updated Cat {categoryId}");
                }
                else
                {
                    await _supabase.From<BudgetModel>().Insert(budget);
                    Debug.WriteLine($"[CRUD] Inserted Cat {categoryId}");
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] {ex.Message}");
                return Json(new { success = false, error = ex.Message });
            }
        }
    }
}