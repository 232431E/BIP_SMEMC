using BIP_SMEMC.Models;
using BIP_SMEMC.Services;
using Microsoft.AspNetCore.Mvc;

namespace BIP_SMEMC.Controllers
{
    public class DashboardController : Controller
    {
        private readonly FinanceService _financeService;
        private readonly DebtService _debtService;
        private readonly Supabase.Client _supabase;
        private readonly GeminiService _gemini;

        public DashboardController(FinanceService financeService, DebtService debtService, Supabase.Client supabase, GeminiService gemini)
        {
            _financeService = financeService;
            _debtService = debtService;
            _supabase = supabase;
            _gemini = gemini;
        }

        public async Task<IActionResult> Index()
        {
            var userEmail = User.Identity?.Name ?? "dummy@sme.com";
            var model = new DashboardViewModel { UserName = userEmail.Split('@')[0] };

            // 1. FETCH DATA (Parallel for speed)
            var latestDate = await _financeService.GetLatestTransactionDate(userEmail);

            var tDebt = _debtService.GetTotalOwed(userEmail);
            var tTrans = _financeService.GetUserTransactions(userEmail, latestDate.AddMonths(-1), latestDate); // Last 30 days
            var tBudgets = _supabase.From<BudgetModel>().Where(b => b.UserId == userEmail).Get();
            var tCategories = _supabase.From<CategoryModel>().Get();

            await Task.WhenAll(tDebt, tTrans, tBudgets, tCategories);

            var transactions = await tTrans;
            var debtsVal = await tDebt;
            var budgets = (await tBudgets).Models;
            var categories = (await tCategories).Models;

            // 2. CALCULATE METRICS
            model.TotalRevenue = transactions.Where(t => t.Credit > 0).Sum(t => t.Credit);
            model.TotalExpenses = transactions.Where(t => t.Debit > 0).Sum(t => t.Debit);
            model.TotalDebt = debtsVal;

            // 3. GENERATE ACTION ITEMS
            // A. Check for Overspending
            foreach (var b in budgets)
            {
                var catName = categories.FirstOrDefault(c => c.Id == b.CategoryId)?.Name ?? "Unknown";
                var spent = transactions.Where(t => t.ParentCategoryName == catName).Sum(t => t.Debit);

                model.BudgetHealth.Add(new BudgetStatusItem { Category = catName, Budget = b.BudgetAmount, Spent = spent });

                if (spent > b.BudgetAmount)
                {
                    model.ActionItems.Add(new NextBestAction
                    {
                        Title = $"Budget Alert: {catName}",
                        Priority = "High",
                        Reasoning = $"You are ${spent - b.BudgetAmount:N0} over budget.",
                        LinkAction = "/Budget"
                    });
                }
            }

            // B. Check Cashflow Trend (Simple rule)
            if (model.NetProfit < 0)
            {
                model.ActionItems.Add(new NextBestAction
                {
                    Title = "Negative Cashflow Warning",
                    Priority = "High",
                    Reasoning = "Expenses exceeded revenue this month. Review recurring costs.",
                    LinkAction = "/CashFlow"
                });
            }

            // 4. CHART DATA (Last 6 Months Trend)
            // Re-fetch a smaller summary for the chart specifically
            var history = await _financeService.GetUserTransactions(userEmail, latestDate.AddMonths(-6), latestDate);
            model.CashflowTrend = history
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .Select(g => new ChartDataPoint
                {
                    Date = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yy"),
                    Actual = g.Sum(x => x.Credit - x.Debit) // Net Cashflow
                })
                .OrderBy(x => DateTime.Parse(x.Date))
                .ToList();

            // 5. AI INSIGHT (Optional: Call Gemini)
            // model.AiSummary = await _gemini.AnalyzeFinancialTrends(history); 
            model.AiSummary = "Cashflow is stable, but debt levels are rising. Consider consolidating loans.";

            return View(model);
        }
    }
}