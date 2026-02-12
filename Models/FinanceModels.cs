using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models
{
    [Table("chat_messages")]
    public class ChatMessageModel : BaseModel
    {
        [PrimaryKey("id", false)] public int Id { get; set; }
        [Column("user_id")] public string UserId { get; set; }
        [Column("report_year")] public int ReportYear { get; set; }
        [Column("role")] public string Role { get; set; }
        [Column("message")] public string Message { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }

    [Table("financial_reports")]
    public class FinancialReportModel : BaseModel
    {
        [PrimaryKey("id", false)] public Guid Id { get; set; }
        [Column("user_id")] public string UserId { get; set; }
        [Column("report_year")] public int ReportYear { get; set; }
        [Column("uploaded_at")] public DateTime UploadedAt { get; set; }
    }

    [Table("derived_kpi_snapshots")]
    public class KpiSnapshotModel : BaseModel
    {
        [PrimaryKey("id", false)] public int Id { get; set; }
        [Column("report_id")] public Guid ReportId { get; set; }
        [Column("revenue")] public decimal Revenue { get; set; }
        [Column("expenses")] public decimal Expenses { get; set; } // Map to 'operating_expenses' logic if needed, simplifed here
        [Column("net_profit")] public decimal NetProfit { get; set; }
        [Column("profit_margin")] public decimal ProfitMargin { get; set; }
        [Column("cash")] public decimal Cash { get; set; }
        [Column("liabilities")] public decimal Liabilities { get; set; }
        [Column("top_expense_categories_json")] public string TopExpenseCategoriesJson { get; set; }
        [Column("calculated_at")] public DateTime CalculatedAt { get; set; }
    }

    [Table("profit_goals")]
    public class ProfitGoalModel : BaseModel
    {
        [PrimaryKey("id", false)] public int Id { get; set; }
        [Column("user_id")] public string UserId { get; set; }
        [Column("report_year")] public int ReportYear { get; set; }
        [Column("target_profit")] public decimal TargetProfit { get; set; }
        [Column("deadline")] public DateTime Deadline { get; set; }
        [Column("assessment_label")] public string AssessmentLabel { get; set; }
        [Column("assessment_explanation")] public string AssessmentExplanation { get; set; }
        [Column("required_monthly_increase")] public decimal RequiredMonthlyIncrease { get; set; }
        [Column("is_active")] public bool IsActive { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    [Table("profit_fix_actions")]
    public class ProfitFixActionModel : BaseModel
    {
        [PrimaryKey("id", false)] public Guid Id { get; set; }
        [Column("user_id")] public string UserId { get; set; }
        [Column("report_year")] public int ReportYear { get; set; }
        [Column("title")] public string Title { get; set; }
        [Column("why_it_matters")] public string WhyItMatters { get; set; }
        [Column("steps")] public string Steps { get; set; }
        [Column("estimated_annual_impact")] public decimal EstimatedAnnualImpact { get; set; }
        [Column("sort_order")] public int SortOrder { get; set; }
        [Column("status")] public string Status { get; set; }
        [Column("realized_savings")] public decimal? RealizedSavings { get; set; }
        [Column("started_at")] public DateTime? StartedAt { get; set; }
        [Column("completed_at")] public DateTime? CompletedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

}