namespace BIP_SMEMC.ViewModels.ProfitImprovement;

public class ProfitImprovementPageViewModel
{
    public int SelectedYear { get; set; }
    public List<int> AvailableYears { get; set; } = new();

    public decimal Revenue { get; set; }
    public decimal Expenses { get; set; }
    public decimal CurrentProfit { get; set; }
    public decimal ProfitMargin { get; set; }
    public decimal CompletedSavings { get; set; }
    public decimal Cash { get; set; }
    public decimal Liabilities { get; set; }

    public GoalViewModel Goal { get; set; } = new();
    public List<ExpenseBreakdownItemViewModel> TopExpenseCategories { get; set; } = new();
    public List<FixCardViewModel> FixCards { get; set; } = new();
}

public class GoalViewModel
{
    public decimal TargetProfit { get; set; }
    public DateTime? Deadline { get; set; }
    public string Label { get; set; } = "No goal set";
    public string Explanation { get; set; } = "Set a profit goal to get achievability insights.";
    public decimal RequiredMonthlyIncrease { get; set; }
}

public class ExpenseBreakdownItemViewModel
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class FixCardViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string Steps { get; set; } = string.Empty;
    public decimal EstimatedAnnualImpact { get; set; }
    public string Status { get; set; } = "Suggested";
    public decimal? RealizedSavings { get; set; }
}

public class SetGoalRequest
{
    public int ReportYear { get; set; }
    public decimal TargetProfit { get; set; }
    public DateTime Deadline { get; set; }
}

public class CompleteFixRequest
{
    public Guid FixId { get; set; }
    public decimal RealizedSavings { get; set; }
}

