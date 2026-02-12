using System.Text.Json;
using BIP_SMEMC.Models;
using BIP_SMEMC.ViewModels.Chat;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace BIP_SMEMC.Services.Finance;

public class FinancialDataService
{
    private readonly Supabase.Client _supabase;

    public FinancialDataService(Supabase.Client supabase)
    {
        _supabase = supabase;
    }

    public async Task<List<int>> GetAvailableYearsAsync(string userId)
    {
        // Fetch unique years from FinancialReportModel
        var response = await _supabase.From<FinancialReportModel>()
            .Select("report_year")
            .Where(x => x.UserId == userId)
            .Get();

        var years = response.Models
            .Select(x => x.ReportYear)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        return years;
    }

    public async Task<FinancialContextSummary?> GetFinancialContextAsync(string userId, int? reportYear = null)
    {
        var years = await GetAvailableYearsAsync(userId);
        if (!years.Any())
        {
            return null;
        }

        var year = reportYear.HasValue && years.Contains(reportYear.Value) ? reportYear.Value : years.First();
        // 1. Get Report ID
        var reportRes = await _supabase.From<FinancialReportModel>()
            .Where(x => x.UserId == userId && x.ReportYear == year)
            .Get();
        var report = reportRes.Models.FirstOrDefault();
        if (report == null) return null;

        // 2. Get KPI Snapshot
        var kpiRes = await _supabase.From<KpiSnapshotModel>()
            .Where(x => x.ReportId == report.Id)
            .Order("calculated_at", Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get();

        var kpi = kpiRes.Models.FirstOrDefault();
        if (kpi == null) return null;

        // 3. Get Goal
        var goalRes = await _supabase.From<ProfitGoalModel>()
            .Where(x => x.UserId == userId && x.ReportYear == year && x.IsActive == true)
            .Get();
        var goal = goalRes.Models.FirstOrDefault();

        // 4. Get Fixes
        var fixRes = await _supabase.From<ProfitFixActionModel>()
            .Where(x => x.UserId == userId && x.ReportYear == year)
            .Get();

        return new FinancialContextSummary
        {
            UserId = userId,
            ReportYear = year,
            Revenue = kpi.Revenue,
            Expenses = kpi.Expenses,
            NetProfit = kpi.NetProfit,
            ProfitMargin = kpi.ProfitMargin,
            Cash = kpi.Cash,
            Liabilities = kpi.Liabilities,
            TopExpenseCategories = JsonSerializer.Deserialize<List<ExpenseCategoryAmount>>(kpi.TopExpenseCategoriesJson) ?? new(),
            Goal = goal == null ? null : new GoalContext { TargetProfit = goal.TargetProfit, Assessment = goal.AssessmentLabel },
            Fixes = fixRes.Models.Select(x => new FixContext { Title = x.Title, Status = x.Status }).ToList()
        };
    }

}

