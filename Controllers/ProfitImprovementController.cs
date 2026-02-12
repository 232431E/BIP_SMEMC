using BIP_SMEMC.Services;
using BIP_SMEMC.Services.Finance;
using BIP_SMEMC.ViewModels.ProfitImprovement;
using Microsoft.AspNetCore.Mvc;

namespace BIP_SMEMC.Controllers;

[RequireAppAuth]
public class ProfitImprovementController : Controller
{
    private readonly ProfitImprovementService _service;

    public ProfitImprovementController(ProfitImprovementService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? year = null)
    {
        var userId = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var model = await _service.BuildPageAsync(userId, year);
        if (model is null)
        {
            TempData["ErrorMessage"] = "No uploaded report data found yet. Please upload your yearly report first.";
            return RedirectToAction("Index", "Expense");
        }

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SetGoal([FromForm] SetGoalRequest request)
    {
        var userId = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var result = await _service.SaveGoalAsync(userId, request);
        return Json(new
        {
            success = result.Success,
            message = result.Message,
            goal = result.Goal
        });
    }

    [HttpPost]
    public async Task<IActionResult> StartFix([FromForm] Guid fixId)
    {
        var userId = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var ok = await _service.StartFixAsync(userId, fixId);
        return Json(new { success = ok });
    }

    [HttpPost]
    public async Task<IActionResult> CompleteFix([FromForm] CompleteFixRequest request)
    {
        var userId = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (request.RealizedSavings < 0)
        {
            return BadRequest(new { success = false, error = "Realized savings cannot be negative." });
        }

        var result = await _service.CompleteFixAsync(userId, request);
        return Json(new
        {
            success = result.Success,
            completedSavings = result.CompletedSavings
        });
    }
}

