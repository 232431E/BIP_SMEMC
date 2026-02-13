using BIP_SMEMC.Models;
using System.Diagnostics;

namespace BIP_SMEMC.Services
{
    public class FinanceService
    {
        private readonly Supabase.Client _supabase;

        public FinanceService(Supabase.Client supabase)
        {
            _supabase = supabase;
        }

        // Services/FinanceService.cs

        public async Task<DateTime> GetLatestTransactionDate(string userEmail)
        {
            try
            {
                // 1. Try fetching the very last transaction for this user
                var res = await _supabase.From<TransactionModel>()
                    .Where(x => x.UserId == userEmail)
                    .Order("date", Postgrest.Constants.Ordering.Descending)
                    .Limit(1)
                    .Get();

                var lastTx = res.Models.FirstOrDefault();

                if (lastTx != null)
                {
                    Debug.WriteLine($"[DATE ANCHOR] Found latest data at: {lastTx.Date:yyyy-MM-dd}");
                    return lastTx.Date;
                }

                // 2. FALLBACK STRATEGY: If user has no data, check for ANY data in the system (Simulation Mode)
                // This is crucial if you are logging in as a new user but want to see the "Demo" timeline (2023)
                var globalRes = await _supabase.From<TransactionModel>()
                    .Order("date", Postgrest.Constants.Ordering.Descending)
                    .Limit(1)
                    .Get();

                var globalLast = globalRes.Models.FirstOrDefault();
                if (globalLast != null)
                {
                    Debug.WriteLine($"[DATE ANCHOR] User has no data. Using Global Anchor: {globalLast.Date:yyyy-MM-dd}");
                    return globalLast.Date;
                }

                Debug.WriteLine("[DATE ANCHOR] No data found in DB. Defaulting to DateTime.Now");
                return DateTime.Now;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DATE ANCHOR ERROR] {ex.Message}");
                return DateTime.Now;
            }
        }

        public async Task<List<TransactionModel>> GetUserTransactions(string userEmail, DateTime startDate, DateTime endDate)
        {
            var allResults = new List<TransactionModel>();
            int pageSize = 1000;
            int offset = 0;
            bool hasMore = true;

            Debug.WriteLine($"[DB FETCH] Requesting Range: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

            while (hasMore)
            {
                try
                {
                    var response = await _supabase.From<TransactionModel>()
                        .Where(x => x.UserId == userEmail)
                        .Filter("date", Postgrest.Constants.Operator.GreaterThanOrEqual, startDate.ToString("yyyy-MM-dd"))
                        .Filter("date", Postgrest.Constants.Operator.LessThanOrEqual, endDate.ToString("yyyy-MM-dd"))
                        .Order("date", Postgrest.Constants.Ordering.Descending)
                        .Range(offset, offset + pageSize - 1)
                        .Get();

                    if (response.Models.Any())
                    {
                        allResults.AddRange(response.Models);
                        offset += pageSize;
                        Debug.WriteLine($"[DB FETCH] Batch {offset / pageSize}: Retrieved {response.Models.Count} rows.");

                        if (response.Models.Count < pageSize) hasMore = false;
                    }
                    else
                    {
                        hasMore = false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DB ERROR] Batch failed: {ex.Message}");
                    hasMore = false;
                }
            }
            Debug.WriteLine($"[DB FETCH] Total Retrieved: {allResults.Count}");
            return allResults;
        }

        // 1. Helper to group data for AI (Saves tokens vs sending raw rows)
        public object GetCashflowSummaryForAI(List<TransactionModel> transactions)
        {
            Debug.WriteLine($"[AI PREP] Summarizing {transactions.Count} transactions...");

            var monthly = transactions
                .GroupBy(t => t.Date.ToString("MMM yyyy"))
                .Select(g => new
                {
                    Month = g.Key,
                    Revenue = g.Where(t => t.Credit > 0).Sum(t => t.Credit),
                    Expense = g.Where(t => t.Debit > 0).Sum(t => t.Debit),
                    Net = g.Where(t => t.Credit > 0).Sum(t => t.Credit) - g.Where(t => t.Debit > 0).Sum(t => t.Debit)
                }).ToList();

            var topExpenses = transactions
                .Where(t => t.Debit > 0)
                .GroupBy(t => t.CategoryName ?? "Uncategorized")
                .Select(g => new { Category = g.Key, Amount = g.Sum(t => t.Debit) })
                .OrderByDescending(x => x.Amount)
                .Take(5)
                .ToList();

            Debug.WriteLine($"[AI PREP] Generated {monthly.Count} monthly points and {topExpenses.Count} top expenses.");

            return new
            {
                MonthlyTrend = monthly,
                TopCostDrivers = topExpenses,
                OverallNet = monthly.Sum(m => m.Net)
            };
        }
        // 2. Specialized Saver for Cashflow Insights
        public async Task SaveCashflowInsight(string userId, string insightText, string rangeLabel)
        {
            try
            {
                var entry = new AIResponseModel
                {
                    UserId = userId,
                    FeatureType = "FINANCIAL_GRAPH_ANALYSIS", // Unique Key
                    ResponseText = insightText,
                    Justification = $"Automated analysis for {rangeLabel} range",
                    VersionTag = "gemini-2.5-flash",
                    DateKey = DateTime.UtcNow.Date,
                    CreatedAt = DateTime.UtcNow
                };

                await _supabase.From<AIResponseModel>().Insert(entry);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB SAVE FAIL] {ex.Message}");
            }
        }
        public double CalculateSeasonalityMultiplier(List<TransactionModel> history, int month)
        {
            if (!history.Any()) return 1.0;

            // 1. Calculate Average Monthly Revenue across ALL months
            var allMonths = history.Where(t => t.Credit > 0).GroupBy(t => t.Date.Month)
                .Select(g => g.Sum(t => t.Credit)).ToList();

            if (!allMonths.Any()) return 1.0;

            double globalAvg = (double)allMonths.Average();
            if (globalAvg == 0) return 1.0;

            // 2. Calculate Average Revenue for TARGET month
            var targetMonthData = history.Where(t => t.Credit > 0 && t.Date.Month == month)
                .GroupBy(t => t.Date.Year)
                .Select(g => g.Sum(t => t.Credit)).ToList();

            if (!targetMonthData.Any()) return 1.0;

            double targetAvg = (double)targetMonthData.Average();

            // 3. Return Ratio (e.g., 12000 / 10000 = 1.2)
            return targetAvg / globalAvg;
        }

        // COMPLEX LOGIC: Seasonal changes based on historical monthly averages
        public double CalculateSeasonality(List<TransactionModel> history, int month)
        {
            if (!history.Any()) return 0;

            // Calculate avg net cash flow for this specific month in previous years
            var monthlyFlows = history
                .Where(t => t.Date.Month == month)
                .GroupBy(t => t.Date.Year)
                .Select(g => g.Sum(x => x.Credit - x.Debit))
                .ToList();

            return monthlyFlows.Any() ? (double)monthlyFlows.Average() : 0;
        }

        // COMPLEX LOGIC: Prediction Formula
        public decimal CalculateAdvancedTrend(int dayOffset, double seasonalFactor, decimal recurringExpenses, decimal currentBalance)
        {
            // Formula: Current + (Seasonality / 30 * day) - (Recurring / 30 * day)
            decimal dailySeasonality = (decimal)seasonalFactor / 30;
            decimal dailyRecurring = recurringExpenses / 30;

            return currentBalance + (dailySeasonality * dayOffset) - (dailyRecurring * dayOffset);
        }

        public async Task SaveAIResponse(string email, string feature, string text, string justification)
        {
            var response = new AIResponseModel
            {
                UserId = email,
                FeatureType = feature,
                ResponseText = text,
                Justification = justification,
                VersionTag = "gemini-2.5-flash", // Or DateTime.Now.ToString("v.yyyyMMdd")
                DateKey = DateTime.UtcNow.Date,  // CRITICAL: Required by your DB schema
                CreatedAt = DateTime.UtcNow
            };
            await _supabase.From<AIResponseModel>().Insert(response);
        }

        // Logic for AI Data Validation (discussed below)
        public bool ValidateImport(TransactionModel item)
        {
            return item.Date != default && item.Debit >= 0 && !string.IsNullOrEmpty(item.Description);
        }
        public decimal CalculateAvgDailyNet(List<TransactionModel> history, int year)
        {
            // Filter for the specific year
            var yearData = history.Where(t => t.Date.Year == year).ToList();
            if (!yearData.Any()) return 0;

            // RULE: Only count actual Income vs actual Operating Expenses
            // Exclude 'Opening Balance' or 'Retained Earnings' adjustments if they exist in descriptions
            var totalIncome = yearData.Where(t => t.Credit > 0 && !t.Description.Contains("Opening")).Sum(t => t.Credit);
            var totalExpense = yearData.Where(t => t.Debit > 0).Sum(t => t.Debit);

            // Debug to console to see the raw burn rate
            Debug.WriteLine($"[BURN RATE] Income: {totalIncome} | Expense: {totalExpense}");
            // Average over 365 days to get a smooth trend
            return (totalIncome - totalExpense) / 365m;
        }

        public decimal CalculateCpfRate(int? age)
        {
            // Singapore CPF Contribution Rates (Employee Share estimates for simulation)
            // You can adjust these to include Employer share (approx +17%) if needed.
            if (!age.HasValue) return 20.00m; // Default if age unknown

            int a = age.Value;

            if (a <= 55) return 20.00m;
            if (a <= 60) return 15.00m;
            if (a <= 65) return 9.50m;
            if (a <= 70) return 6.00m;
            return 5.00m; // Above 70
        }
    }
}