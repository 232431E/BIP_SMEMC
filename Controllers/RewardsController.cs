using BIP_SMEMC.Models.ViewModels;
using BIP_SMEMC.Services;
using Microsoft.AspNetCore.Mvc;

namespace BIP_SMEMC.Controllers;

public class RewardsController : Controller
{
    private readonly RewardsService _rewards;

    public RewardsController(RewardsService rewards)
    {
        _rewards = rewards;
    }

    private string? GetCurrentUserId()
    {
        return HttpContext.Session.GetString("UserEmail");
    }

    // GET: /Rewards
    public async Task<IActionResult> Index(string? tab = null)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");
        var pointsTask = _rewards.GetPointsAsync(userId);
        var historyTask = _rewards.GetHistoryAsync(userId);
        var achievementsTask = _rewards.GetAchievementsAsync(userId);

        await Task.WhenAll(pointsTask, historyTask, achievementsTask);

        var history = historyTask.Result;
        var achievements = achievementsTask.Result;
        var earned = history.Where(h => h.Points > 0).Sum(h => h.Points);
        var redeemed = Math.Abs(history.Where(h => h.Points < 0).Sum(h => h.Points));
        var currentPoints = pointsTask.Result;
        var unlocked = achievements.Count(a => string.Equals(a.Status, "earned", StringComparison.OrdinalIgnoreCase));

        var vm = new RewardsPageViewModel
        {
            CurrentPoints = currentPoints,
            TotalEarned = earned,
            TotalRedeemed = redeemed,
            TotalAchievements = achievements.Count,
            AchievementsUnlocked = unlocked,
            ActiveTab = string.IsNullOrWhiteSpace(tab) ? "vouchers" : tab.ToLowerInvariant(),
            Rewards = _rewards.GetRewards(),
            History = history,
            Achievements = achievements
        };

        return View(vm);
    }

    // GET: /Rewards/History
    public IActionResult History()
    {
        return RedirectToAction(nameof(Index), new { tab = "history" });
    }

    // GET: /Rewards/Redeem
    [HttpGet]
    public IActionResult Redeem()
    {
        return RedirectToAction(nameof(Index), new { tab = "vouchers" });
    }

    // POST: /Rewards/Redeem
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Redeem(int rewardId)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");
        var result = await _rewards.TryRedeemAsync(userId, rewardId);
        if (result.Success)
        {
            TempData["Success"] = $"Redeemed successfully: {result.Reward!.Title}";
            return RedirectToAction(nameof(Index), new { tab = "history" });
        }

        TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Index), new { tab = "vouchers" });
    }
}
