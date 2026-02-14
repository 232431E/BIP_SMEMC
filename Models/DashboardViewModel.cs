using System.Collections.Generic;

namespace BIP_SMEMC.Models
{
    public class DashboardViewModel
    {
        public string UserName { get; set; } = "User";

        public bool IsHistoricalData { get; set; } // Tracks if current month is empty
        public string DataMonthLabel { get; set; } // Label for the italic tag

        // headline Metrics
        public decimal TotalRevenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetProfit => TotalRevenue - TotalExpenses;
        public decimal TotalDebt { get; set; }

        // Insights & Customization
        public string AiSummary { get; set; }
        public List<NextBestAction> ActionItems { get; set; } = new();

        // Charts
        public List<ChartDataPoint> CashflowTrend { get; set; } = new();
        public List<BudgetStatusItem> BudgetHealth { get; set; } = new();

        // --- NEW MODULE SUMMARIES ---
        public int RewardsPoints { get; set; }
        public int NewForumPosts { get; set; }
        public double LearningCompletionPct { get; set; }
        public string NextLessonTitle { get; set; }
        public decimal PendingPayroll { get; set; }
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