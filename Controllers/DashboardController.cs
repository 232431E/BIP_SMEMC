using BIP_SMEMC.Models;
using BIP_SMEMC.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace BIP_SMEMC.Controllers
{
    public class DashboardController : Controller
    {
        private readonly FinanceService _financeService;
        private readonly DebtService _debtService;
        private readonly LearningService _learningService;
        private readonly CommunityService _communityService;
        private readonly RewardsService _rewardsService;
        private readonly Supabase.Client _supabase;
        private readonly GeminiService _gemini;

        public DashboardController(FinanceService finance, DebtService debt, LearningService learning,
                                   CommunityService community, RewardsService rewards, 
                                   Supabase.Client supabase, GeminiService gemini)
        {
            _financeService = finance;
            _debtService = debt;
            _learningService = learning;
            _communityService = community;
            _rewardsService = rewards;
            _supabase = supabase;
            _gemini = gemini;
        }

        public async Task<IActionResult> Index()
        {
            // 1. Strict Session Check
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrWhiteSpace(userEmail))
            {
                return RedirectToAction("Login", "Account");
            }
            Debug.WriteLine($"[DASHBOARD] Logged In: {userEmail}");
            // 2. Fetch User Display Name
            // Fetch user info for name display
            var userRes = await _supabase.From<UserModel>().Where(x => x.Email == userEmail).Get();
            var user = userRes.Models.FirstOrDefault();
            
            // 2. Data Freshness Check
            var latestDate = await _financeService.GetLatestTransactionDate(userEmail);
            var now = DateTime.Now;
            var model = new DashboardViewModel
            {
                UserName = user?.FullName ?? userEmail.Split('@')[0],
                IsHistoricalData = (latestDate.Year < now.Year || latestDate.Month < now.Month),
                DataMonthLabel = latestDate.ToString("MMMM yyyy")
            };
            // 3. Define the Snapshot Month (Start of latest month to latest date)
            var startOfSnapshot = new DateTime(latestDate.Year, latestDate.Month, 1);
            Debug.WriteLine($"[DASHBOARD] Snapshot Month: {startOfSnapshot:MMM yyyy}");
            // 4. Fetch Core Data (Parallel)
            var tDebt = _debtService.GetTotalOwed(userEmail);
            var tTrans = _financeService.GetUserTransactions(userEmail, startOfSnapshot, latestDate);
            var tBudgets = _supabase.From<BudgetModel>().Filter("user_id", Postgrest.Constants.Operator.Equals, userEmail).Get();
            var tCategories = _supabase.From<CategoryModel>().Get();

            await Task.WhenAll(tDebt, tTrans, tBudgets, tCategories);

            var transactions = await tTrans;
            var budgets = (await tBudgets).Models;
            var categories = (await tCategories).Models;

            
            // Filter to exclude summary and adjustment rows
            var cleanTrans = transactions.Where(t =>
                !t.Description.StartsWith("Total") &&
                !t.Description.Contains("Balance Forward") &&
                !t.Description.Contains("Opening Balance") &&
                !t.Description.Contains("Jan - Dec")
            ).ToList();

            // 6. Budget Health logic (with historical carry-forward)

            //var tProgress = _learningService.GetOverallCompletionAsync(userEmail);
            //var tThreads = _communityService.GetThreadsAsync();
            //var tPoints = _rewardsService.GetPointsAsync(userEmail);


            //model.LearningCompletionPct = await tProgress;
            //model.RewardsPoints = await tPoints;
            //model.NewForumPosts = (await tThreads).Count;

            // 1. Establish Hierarchical Roots
            // 1. Identify specific Roots for surgical filtering
            var revenueRoot = categories.FirstOrDefault(c => c.Name.Equals("Revenue", StringComparison.OrdinalIgnoreCase));
            var incomeRoot = categories.FirstOrDefault(c => c.Name.Equals("Income", StringComparison.OrdinalIgnoreCase));
            var expenseRoot = categories.FirstOrDefault(c => c.Name.Equals("Expense", StringComparison.OrdinalIgnoreCase));

            var cleanSnapshot = cleanTrans
        .Where(t => !t.Description.Contains("Total") && !t.Description.Contains("Balance"))
        .GroupBy(t => new { t.Date.Date, t.Description, t.Credit, t.Debit })
        .Select(g => g.First())
        .ToList();
            // 2. Refine Exclusion Lists (Fixing the Comptroller typo)
            var exclusionKeywords = new[] {
        "Refund", "Adv pay", "Adjustment", "Disbursement", "Opening Balance",
        "Balance Forward", "Utilities Income", "Internet", "scale",
        "pay", "salary", "Shortfall", "Shortage", "manpower pd for",
        "Petrol", "GST", "CPF", "FWL", "claim", "Return of loan", "Transfer",
        "Part JT", "Bal JT", "collection"
    };

            var exclusionEntities = new[] { "Comptroller", "Singtel", "Popular Book", "LTA", "IRAS", "MOM", "CPFB", "ACE STAR AUTO" };


            // 2. REVENUE: Credits belonging ONLY to the Income tree (excluding non-revenue credits)
            Debug.WriteLine("---------- REVENUE AUDIT ----------");
            var revenueTrans = cleanSnapshot
        .Where(t => t.Credit > 0 &&
                    IsInHierarchy(t.CategoryId, revenueRoot?.Id, categories) &&
                    !exclusionKeywords.Any(k => t.Description.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                    !exclusionEntities.Any(e => t.Description.Contains(e, StringComparison.OrdinalIgnoreCase)) &&
            !t.Description.Contains("Total") &&
                    !t.Description.Contains("Jan - Dec"))
        .ToList();

            foreach (var r in revenueTrans)
            {
                Debug.WriteLine($" + Revenue: {r.Date:dd/MM} | ${r.Credit} | {r.Description}");
            }
            model.TotalRevenue = revenueTrans.Sum(t => t.Credit);
            Debug.WriteLine($"TOTAL REVENUE: ${model.TotalRevenue}");
            Debug.WriteLine("-----------------------------------");

            // Expenses
            var cogsRoot = categories.FirstOrDefault(c => c.Name.Contains("Cost of Goods Sold"));
            var expenseTrans = transactions
    .Where(t => t.Debit > 0 &&
                (IsInHierarchy(t.CategoryId, expenseRoot?.Id, categories) ||
                 IsInHierarchy(t.CategoryId, cogsRoot?.Id, categories)) &&
                !t.Description.Contains("Total") &&
                !t.Description.Contains("Balance Forward"))
    .ToList();
            model.TotalExpenses = expenseTrans.Sum(t => t.Debit);
            // 5. Cashflow Trend (Last 6 Months)
            // 5. Cashflow Trend (Last 6 Months) - Optimized with Deduplication & Audit Logs
            var rawHistory = await _financeService.GetUserTransactions(userEmail, latestDate.AddMonths(-6), latestDate);
            var deduplicatedHistory = rawHistory
                .Where(t => !t.Description.Contains("Total") && !t.Description.Contains("Balance"))
                .GroupBy(t => new { t.Date.Date, t.Description, t.Credit, t.Debit })
                .Select(g => g.First())
                .ToList();
            Debug.WriteLine("========== MONTHLY CASHFLOW BREAKDOWN (DEBUG) ==========");
            model.CashflowTrend = deduplicatedHistory
        .GroupBy(t => new { t.Date.Year, t.Date.Month })
        .Select(g => {
            var monthLabel = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yy");

            // Use exact same logic for Rev/Exp inside the loop
            decimal mRev = g.Where(t => t.Credit > 0 && IsInHierarchy(t.CategoryId, revenueRoot?.Id, categories) &&
                                     !exclusionKeywords.Any(k => t.Description.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                                     !exclusionEntities.Any(e => t.Description.Contains(e, StringComparison.OrdinalIgnoreCase)))
                             .Sum(x => x.Credit);

            decimal mExp = g.Where(t => t.Debit > 0 && (IsInHierarchy(t.CategoryId, expenseRoot?.Id, categories) || IsInHierarchy(t.CategoryId, cogsRoot?.Id, categories)))
                             .Sum(x => x.Debit);

            Debug.WriteLine($"[{monthLabel}] Revenue: ${mRev,12:N2} | Expense: ${mExp,12:N2} | Net: ${mRev - mExp,12:N2}");

            return new ChartDataPoint { Date = monthLabel, Actual = mRev - mExp };
        }).OrderBy(x => DateTime.ParseExact(x.Date, "MMM yy", null)).ToList();

            Debug.WriteLine("=========================================================");
            // 3. GENERATE ACTION ITEMS
            // 4. BUDGET HEALTH (Last Month Snapshot)
            // Budget Health
            foreach (var cat in categories.Where(c => c.ParentId == expenseRoot?.Id))
            {
                var bRecord = budgets.Where(b => b.CategoryId == cat.Id)
                    .OrderByDescending(b => b.Year).ThenByDescending(b => b.Month)
                    .FirstOrDefault(b => b.Year < latestDate.Year || (b.Year == latestDate.Year && b.Month <= latestDate.Month));

                if (bRecord == null) continue;

                decimal spent = cleanTrans.Where(t => IsInHierarchy(t.CategoryId, cat.Id, categories)).Sum(t => t.Debit);
                model.BudgetHealth.Add(new BudgetStatusItem { Category = cat.Name, Spent = spent, Budget = bRecord.BudgetAmount });
            }

            
            // 5. AI INSIGHT (Optional: Call Gemini)
            // model.AiSummary = await _gemini.AnalyzeFinancialTrends(history); 
            model.AiSummary = model.NetProfit < 0
        ? "Warning: Negative cashflow this month. Review your 'Action Required' list."
        : "Your business is currently profitable. Consider allocating surplus to debt reduction.";

            return View(model);
        }

        // Helper: Traces if a category belongs to a specific root tree
        private bool IsCostOfGoodsSold(int? catId, List<CategoryModel> cats)
        {
            if (catId == null) return false;
            var c = cats.FirstOrDefault(x => x.Id == catId);
            while (c != null)
            {
                if (c.Name.Contains("Cost of Goods Sold")) return true;
                c = cats.FirstOrDefault(x => x.Id == c.ParentId);
            }
            return false;
        }

        private bool IsInHierarchy(int? catId, int? rootId, List<CategoryModel> cats)
        {
            if (catId == null || rootId == null) return false;
            var curr = cats.FirstOrDefault(c => c.Id == catId);
            while (curr != null)
            {
                if (curr.Id == rootId) return true;
                if (curr.ParentId == 0 || curr.ParentId == null) break;
                curr = cats.FirstOrDefault(c => c.Id == curr.ParentId);
            }
            return false;
        }

        
    }
}