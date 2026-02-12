using BIP_SMEMC.Models;
using BIP_SMEMC.ViewModels.Chat; // Required for FinancialContextSummary
using BIP_SMEMC.ViewModels.ProfitImprovement;

namespace BIP_SMEMC.Services.Finance;

public class ProfitImprovementService
{
    private readonly Supabase.Client _supabase;
    private readonly FinancialDataService _financialDataService;

    public ProfitImprovementService(Supabase.Client supabase, FinancialDataService financialDataService)
    {
        _supabase = supabase;
        _financialDataService = financialDataService;
    }

    public async Task<ProfitImprovementPageViewModel?> BuildPageAsync(string userId, int? selectedYear = null)
    {
        var context = await _financialDataService.GetFinancialContextAsync(userId, selectedYear);
        if (context is null) return null;

        await EnsureFixCardsAsync(context);

        // Fetch Fixes
        var fixesResponse = await _supabase.From<ProfitFixActionModel>()
            .Where(x => x.UserId == userId && x.ReportYear == context.ReportYear)
            .Order("sort_order", Postgrest.Constants.Ordering.Ascending)
            .Get();

        var fixes = fixesResponse.Models.Select(x => new FixCardViewModel
        {
            Id = x.Id,
            Title = x.Title,
            WhyItMatters = x.WhyItMatters,
            Steps = x.Steps,
            EstimatedAnnualImpact = x.EstimatedAnnualImpact,
            Status = x.Status,
            RealizedSavings = x.RealizedSavings
        }).ToList();

        var completedSavings = fixes
            .Where(x => x.Status == "Completed" && x.RealizedSavings.HasValue)
            .Sum(x => x.RealizedSavings!.Value);

        // Fetch Active Goal
        var goalResponse = await _supabase.From<ProfitGoalModel>()
            .Where(x => x.UserId == userId && x.ReportYear == context.ReportYear && x.IsActive == true)
            .Order("updated_at", Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get();

        var activeGoal = goalResponse.Models.FirstOrDefault();
        var years = await _financialDataService.GetAvailableYearsAsync(userId);

        return new ProfitImprovementPageViewModel
        {
            SelectedYear = context.ReportYear,
            AvailableYears = years,
            Revenue = context.Revenue,
            Expenses = context.Expenses,
            CurrentProfit = context.NetProfit,
            ProfitMargin = context.ProfitMargin,
            Cash = context.Cash,
            Liabilities = context.Liabilities,
            CompletedSavings = completedSavings,
            TopExpenseCategories = context.TopExpenseCategories
                .Select(x => new ExpenseBreakdownItemViewModel { Category = x.Category, Amount = x.Amount })
                .ToList(),
            Goal = activeGoal is null ? new GoalViewModel() : new GoalViewModel
            {
                TargetProfit = activeGoal.TargetProfit,
                Deadline = activeGoal.Deadline,
                Label = activeGoal.AssessmentLabel,
                Explanation = activeGoal.AssessmentExplanation,
                RequiredMonthlyIncrease = activeGoal.RequiredMonthlyIncrease
            },
            FixCards = fixes
        };
    }

    public async Task<(bool Success, string Message, GoalViewModel Goal)> SaveGoalAsync(string userId, SetGoalRequest request)
    {
        var context = await _financialDataService.GetFinancialContextAsync(userId, request.ReportYear);
        if (context is null) return (false, "No data found for this year.", new GoalViewModel());

        if (request.TargetProfit <= 0) return (false, "Target must be positive.", new GoalViewModel());
        if (request.Deadline <= DateTime.UtcNow) return (false, "Deadline must be in future.", new GoalViewModel());

        // Deactivate old goals
        await _supabase.From<ProfitGoalModel>()
            .Where(x => x.UserId == userId && x.ReportYear == request.ReportYear)
            .Set(x => x.IsActive, false)
            .Update();

        var assessment = AssessGoal(context, request.TargetProfit, request.Deadline);

        var goal = new ProfitGoalModel
        {
            UserId = userId,
            ReportYear = request.ReportYear,
            TargetProfit = request.TargetProfit,
            Deadline = request.Deadline,
            AssessmentLabel = assessment.Label,
            AssessmentExplanation = assessment.Explanation,
            RequiredMonthlyIncrease = assessment.RequiredMonthlyIncrease,
            IsActive = true,
            UpdatedAt = DateTime.UtcNow
        };

        await _supabase.From<ProfitGoalModel>().Insert(goal);

        return (true, "Goal saved.", new GoalViewModel
        {
            TargetProfit = goal.TargetProfit,
            Deadline = goal.Deadline,
            Label = goal.AssessmentLabel,
            Explanation = goal.AssessmentExplanation,
            RequiredMonthlyIncrease = goal.RequiredMonthlyIncrease
        });
    }

    public async Task<bool> StartFixAsync(string userId, Guid fixId)
    {
        var result = await _supabase.From<ProfitFixActionModel>()
            .Where(x => x.Id == fixId && x.UserId == userId)
            .Get();

        var fix = result.Models.FirstOrDefault();
        if (fix == null) return false;

        if (fix.Status == "Suggested")
        {
            await _supabase.From<ProfitFixActionModel>()
                .Where(x => x.Id == fixId)
                .Set(x => x.Status, "InProgress")
                .Set(x => x.StartedAt, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .Update();
        }
        return true;
    }

    public async Task<(bool Success, decimal CompletedSavings)> CompleteFixAsync(string userId, CompleteFixRequest request)
    {
        var result = await _supabase.From<ProfitFixActionModel>()
            .Where(x => x.Id == request.FixId && x.UserId == userId)
            .Get();

        var fix = result.Models.FirstOrDefault();
        if (fix == null) return (false, 0);

        await _supabase.From<ProfitFixActionModel>()
            .Where(x => x.Id == request.FixId)
            .Set(x => x.Status, "Completed")
            .Set(x => x.RealizedSavings, request.RealizedSavings)
            .Set(x => x.CompletedAt, DateTime.UtcNow)
            .Set(x => x.UpdatedAt, DateTime.UtcNow)
            .Update();

        // Recalculate total savings
        var allFixes = await _supabase.From<ProfitFixActionModel>()
            .Where(x => x.UserId == userId && x.ReportYear == fix.ReportYear && x.Status == "Completed")
            .Get();

        var total = allFixes.Models.Sum(x => x.RealizedSavings ?? 0);
        return (true, total);
    }

    private async Task EnsureFixCardsAsync(FinancialContextSummary context)
    {
        var countRes = await _supabase.From<ProfitFixActionModel>()
            .Where(x => x.UserId == context.UserId && x.ReportYear == context.ReportYear)
            .Count(Postgrest.Constants.CountType.Exact);

        if (countRes > 0) return; // Already exists

        var generated = GenerateRuleBasedFixes(context);

        var toAdd = generated.Select((x, idx) => new ProfitFixActionModel
        {
            UserId = context.UserId,
            ReportYear = context.ReportYear,
            Title = x.Title,
            WhyItMatters = x.Why,
            Steps = x.Steps,
            EstimatedAnnualImpact = x.Impact,
            SortOrder = idx + 1,
            Status = "Suggested",
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        if (toAdd.Any())
        {
            await _supabase.From<ProfitFixActionModel>().Insert(toAdd);
        }
    }

    private static GoalAssessment AssessGoal(FinancialContextSummary context, decimal targetProfit, DateTime deadline)
    {
        var currentProfit = context.NetProfit;
        var gap = targetProfit - currentProfit;
        if (gap <= 0) return new GoalAssessment("Achievable", "Target already met.", 0);

        var months = Math.Max(1, ((deadline.Year - DateTime.UtcNow.Year) * 12) + (deadline.Month - DateTime.UtcNow.Month));
        var reqIncrease = Math.Round(gap / months, 2);

        return new GoalAssessment("On Track", $"Requires +{reqIncrease:N0}/mo increase.", reqIncrease);
    }

    private static List<(string Title, string Why, string Steps, decimal Impact)> GenerateRuleBasedFixes(FinancialContextSummary context)
    {
        var list = new List<(string, string, string, decimal)>();
        // Simple example rule
        if (context.ProfitMargin < 15)
        {
            list.Add(("Optimize Pricing", "Margin is low.", "Review top 5 products.", context.Revenue * 0.05m));
        }
        else
        {
            list.Add(("Reinvest Surplus", "Healthy margin.", "Expand marketing.", 0));
        }
        return list;
    }

    private sealed record GoalAssessment(string Label, string Explanation, decimal RequiredMonthlyIncrease);
}