namespace BIP_SMEMC.Models
{
    public class ProfitImprovementViewModel
    {
        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }
        public decimal CurrentProfit => Revenue - Expenses;
        public decimal CompletedSavings { get; set; }
        public ProfitGoalSessionModel Goal { get; set; } = new();
        public string GoalAssessment { get; set; } = "No goal set yet.";
        public List<ProfitFixItem> Fixes { get; set; } = new();
    }

    public class ProfitGoalSessionModel
    {
        public decimal TargetProfit { get; set; }
        public DateTime Deadline { get; set; }
    }

    public class ProfitFixItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public decimal EstimatedMonthlySavings { get; set; }
        public int ProgressPercent { get; set; }
    }
}
