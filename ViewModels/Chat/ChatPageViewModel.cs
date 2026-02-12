namespace BIP_SMEMC.ViewModels.Chat;

public class ChatPageViewModel
{
    public int SelectedYear { get; set; }
    public List<int> AvailableYears { get; set; } = new();
    public string FinancialContextPreview { get; set; } = string.Empty;
    public List<ChatMessageViewModel> Messages { get; set; } = new();
}

public class ChatMessageViewModel
{
    public string Role { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ChatSendRequest
{
    public int ReportYear { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ChatStructuredResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<string> ActionItems { get; set; } = new();
    public UsedNumbers UsedNumbers { get; set; } = new();
}

public class UsedNumbers
{
    public decimal Revenue { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetProfit { get; set; }
    public decimal ProfitMargin { get; set; }
    public List<string> TopExpenseCategories { get; set; } = new();
}

public class FinancialContextSummary
{
    public string UserId { get; set; } = string.Empty;
    public int ReportYear { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cogs { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetProfit { get; set; }
    public decimal ProfitMargin { get; set; }
    public decimal Cash { get; set; }
    public decimal Liabilities { get; set; }
    public List<ExpenseCategoryAmount> TopExpenseCategories { get; set; } = new();
    public GoalContext? Goal { get; set; }
    public List<FixContext> Fixes { get; set; } = new();
}

public class ExpenseCategoryAmount
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class GoalContext
{
    public decimal TargetProfit { get; set; }
    public DateTime Deadline { get; set; }
    public string Assessment { get; set; } = string.Empty;
}

public class FixContext
{
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal EstimatedAnnualImpact { get; set; }
    public decimal? RealizedSavings { get; set; }
}

