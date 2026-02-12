using BIP_SMEMC.Models;
using BIP_SMEMC.Services;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;

namespace BIP_SMEMC.Controllers;

[RequireAppAuth]
public class ChatbotController : Controller
{
    private readonly GeminiService _geminiService;

    public ChatbotController(GeminiService geminiService)
    {
        _geminiService = geminiService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var model = new ChatbotPageViewModel();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeReport([FromBody] FinancialReportInput report)
    {
        if (report.Revenue < 0 || report.Expenses < 0)
        {
            return BadRequest(new { success = false, error = "Revenue/Expenses cannot be negative." });
        }

        HttpContext.Session.SetObject("financial_report", report);
        var prompt = BuildLegacyPrompt(report, null);
        var insight = await _geminiService.GenerateFinanceInsightAsync(prompt);
        HttpContext.Session.SetString("financial_insight", insight);

        return Json(new { success = true, insight });
    }

    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        var report = HttpContext.Session.GetObject<FinancialReportInput>("financial_report");
        if (report == null)
        {
            return BadRequest(new { success = false, error = "Please submit your annual report first." });
        }

        var prompt = BuildLegacyPrompt(report, request.Message);
        var answer = await _geminiService.GenerateFinanceInsightAsync(prompt);
        return Json(new { success = true, answer });
    }

    private static string BuildLegacyPrompt(FinancialReportInput report, string? userQuestion)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an SME finance assistant.");
        sb.AppendLine("Analyze the annual report and return actionable recommendations.");
        sb.AppendLine();
        sb.AppendLine("Annual report:");
        sb.AppendLine($"Year: {report.Year}");
        sb.AppendLine($"Revenue: {report.Revenue.ToString("N2", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Expenses: {report.Expenses.ToString("N2", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Profit Margin (%): {report.ProfitMargin.ToString("N2", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(report.Notes))
        {
            sb.AppendLine($"Notes: {report.Notes}");
        }

        sb.AppendLine();
        sb.AppendLine("Output format:");
        sb.AppendLine("1) Financial diagnosis (3 bullet points)");
        sb.AppendLine("2) Profit improvement opportunities (3 bullet points with estimated savings)");
        sb.AppendLine("3) 30-60-90 day action plan");
        sb.AppendLine("4) Risks and assumptions");

        if (!string.IsNullOrWhiteSpace(userQuestion))
        {
            sb.AppendLine();
            sb.AppendLine($"User follow-up question: {userQuestion}");
        }

        return sb.ToString();
    }
}
