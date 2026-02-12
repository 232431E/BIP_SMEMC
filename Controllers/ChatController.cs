using BIP_SMEMC.Services;
using BIP_SMEMC.Services.Finance;
using BIP_SMEMC.ViewModels.Chat;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace BIP_SMEMC.Controllers;

[RequireAppAuth]
public class ChatController : Controller
{
    private readonly FinanceChatService _chatService;
    private readonly FinancialDataService _financialDataService;

    public ChatController(FinanceChatService chatService, FinancialDataService financialDataService)
    {
        _chatService = chatService;
        _financialDataService = financialDataService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? year = null)
    {
        var userId = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var years = await _financialDataService.GetAvailableYearsAsync(userId);
        if (!years.Any())
        {
            TempData["ErrorMessage"] = "No uploaded report data found yet. Please upload your yearly report first.";
            return RedirectToAction("Index", "Expense");
        }

        var selectedYear = year.HasValue && years.Contains(year.Value) ? year.Value : years.First();
        List<ChatMessageViewModel> messages;
        string preview;

        try
        {
            var historyTask = _chatService.GetHistoryAsync(userId, selectedYear);
            var previewTask = _chatService.BuildContextPreviewAsync(userId, selectedYear);
            await Task.WhenAll(
                historyTask.WaitAsync(TimeSpan.FromSeconds(4)),
                previewTask.WaitAsync(TimeSpan.FromSeconds(4)));
            messages = historyTask.Result;
            preview = previewTask.Result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] Chat page degraded mode: {ex.Message}");
            messages = new List<ChatMessageViewModel>();
            preview = "Financial context is currently unavailable due to database connectivity. Please verify your Supabase Postgres connection string and try again.";
            TempData["ErrorMessage"] = "Chat storage temporarily unavailable. You can still access other features.";
        }

        return View(new ChatPageViewModel
        {
            SelectedYear = selectedYear,
            AvailableYears = years,
            FinancialContextPreview = preview,
            Messages = messages
        });
    }

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] ChatSendRequest request)
    {
        var userId = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        (bool Success, string Error, ChatStructuredResponse? Response) result;
        try
        {
            result = await _chatService.AskAsync(userId, request.ReportYear, request.Message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] Chat send failed: {ex.Message}");
            return BadRequest(new
            {
                success = false,
                error = "Chat service is temporarily unavailable. Check database connection and try again."
            });
        }

        if (!result.Success)
        {
            return BadRequest(new
            {
                success = false,
                error = result.Error
            });
        }

        return Json(new
        {
            success = true,
            response = result.Response
        });
    }
}
