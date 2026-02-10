namespace BIP_SMEMC.Models
{
    public class DashboardViewModel
    {
        public string UserName { get; set; } = "User";
        public decimal TotalRevenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetProfit => TotalRevenue - TotalExpenses;
        public string AiSummary { get; set; }
        public List<AnomalyAlert> Anomalies { get; set; } = new();
        public NextBestAction TaskAction { get; set; }
    }
    public class AnomalyAlert
    {
        public string Metric { get; set; }
        public int Variance { get; set; }
        public string Tip { get; set; }
    }

    public class NextBestAction
    {
        public string Title { get; set; }
        public string Priority { get; set; } // High/Medium
        public string Reasoning { get; set; }
    }
}
