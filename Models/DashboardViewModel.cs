using System.Collections.Generic;

namespace BIP_SMEMC.Models
{
    public class DashboardViewModel
    {
        public string UserName { get; set; } = "User";

        // --- 1. HEADLINE METRICS ---
        public decimal TotalRevenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetProfit => TotalRevenue - TotalExpenses;
        public decimal TotalDebt { get; set; }

        // --- 2. INSIGHTS ---
        public string AiSummary { get; set; }
        public List<NextBestAction> ActionItems { get; set; } = new();

        // --- 3. CHARTS ---
        public List<ChartDataPoint> CashflowTrend { get; set; } = new();
        public List<BudgetStatusItem> BudgetHealth { get; set; } = new();
    }

    public class NextBestAction
    {
        public string Title { get; set; }
        public string Priority { get; set; } // High, Medium, Low
        public string Reasoning { get; set; }
        public string LinkAction { get; set; } // URL to go to
    }

    public class BudgetStatusItem
    {
        public string Category { get; set; }
        public decimal Spent { get; set; }
        public decimal Budget { get; set; }
        public double Percentage => Budget > 0 ? (double)(Spent / Budget) * 100 : 0;
    }
}