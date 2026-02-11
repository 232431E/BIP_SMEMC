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

        public async Task<List<TransactionModel>> GetUserTransactions(string userEmail, DateTime startDate, DateTime endDate)
        {
            var allResults = new List<TransactionModel>();
            int pageSize = 1000;
            int offset = 0;
            bool hasMore = true;

            Debug.WriteLine($"[DB FETCH] Starting batch retrieval: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

            while (hasMore)
            {
                try
                {
                    var response = await _supabase.From<TransactionModel>()
                        .Where(x => x.UserId == userEmail)
                        .Filter("date", Postgrest.Constants.Operator.GreaterThanOrEqual, startDate.ToString("yyyy-MM-dd"))
                        .Filter("date", Postgrest.Constants.Operator.LessThanOrEqual, endDate.ToString("yyyy-MM-dd"))
                        .Order("date", Postgrest.Constants.Ordering.Descending)
                        .Range(offset, offset + pageSize - 1) // Critical for getting > 1000 rows
                        .Get();

                    if (response.Models.Any())
                    {
                        allResults.AddRange(response.Models);
                        offset += pageSize;
                        Debug.WriteLine($"[DB FETCH] Batch {offset / pageSize}: Retrieved {response.Models.Count} rows.");

                        if (response.Models.Count < pageSize) hasMore = false; // End of data
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

       
        public async Task<DateTime> GetLatestTransactionDate(string userEmail)
        {
            var response = await _supabase.From<TransactionModel>()
                .Where(x => x.UserId == userEmail)
                .Order("date", Postgrest.Constants.Ordering.Descending)
                .Limit(1)
                .Get();

            var latest = response.Models.FirstOrDefault();

            // If we have data, return that date. If not, return real Today.
            return latest != null ? latest.Date : DateTime.Today;
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
                VersionTag = "gemini-1.5-flash", // Or DateTime.Now.ToString("v.yyyyMMdd")
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