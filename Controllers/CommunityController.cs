using Microsoft.AspNetCore.Mvc;
using BIP_SMEMC.Models;
using Microsoft.AspNetCore.Http;
using System.IO;
using BIP_SMEMC.Models.ViewModels;
using BIP_SMEMC.Services;

namespace BIP_SMEMC.Controllers;

public class CommunityController : Controller
{
    private readonly CommunityService _community;
    private readonly RewardsService _rewards;
    private const string ResourceBucket = "community-resources";

    public CommunityController(CommunityService community, RewardsService rewards)
    {
        _community = community;
        _rewards = rewards;
    }

    public async Task<IActionResult> Index(string? tab = null)
    {
        var threads = await _community.GetThreadsAsync();
        var userId = _community.GetDefaultUserId();

        var profileTask = _community.GetProfileAsync(userId);
        var eventsTask = _community.GetEventsAsync();
        var resourcesTask = _community.GetResourcesAsync();
        var badgesTask = _community.GetBadgesAsync(userId);

        await Task.WhenAll(profileTask, eventsTask, resourcesTask, badgesTask);

        var badges = badgesTask.Result;

        var vm = new CommunityHubViewModel
        {
            Threads = threads,
            Profile = profileTask.Result,
            Events = eventsTask.Result,
            Resources = resourcesTask.Result,
            BadgesEarned = badges.Where(b => b.Status == "earned").ToList(),
            BadgesInProgress = badges.Where(b => b.Status == "in_progress").ToList()
        };

        ViewData["ActiveTab"] = tab;
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string q)
    {
        var results = await _community.GetThreadsAsync(q);
        return PartialView("_ThreadList", results);
    }

    public async Task<IActionResult> Thread(int id)
    {
        var thread = await _community.GetThreadAsync(id);
        if (thread == null)
            return RedirectToAction(nameof(Index));

        await _community.IncrementViewAsync(id);
        return View(thread);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new ForumThread());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ForumThread thread)
    {
        if (!ModelState.IsValid)
            return View(thread);

        // ✅ For now: placeholder author (later replace with logged-in user)
        thread.Author = "Admin";

        await _community.CreateThreadAsync(thread);

        // ✅ Rewards points for creating thread
        await _rewards.AddPointsAsync("Admin", 25, "Created forum thread");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int threadId, ForumReply reply)
    {
        if (!ModelState.IsValid)
            return RedirectToAction(nameof(Thread), new { id = threadId });

        // ✅ For now: placeholder author (later replace with logged-in user)
        reply.Author = "Admin";
        reply.ThreadId = threadId;

        await _community.AddReplyAsync(threadId, reply);

        // ✅ Rewards points for replying
        await _rewards.AddPointsAsync("Admin", 10, "Replied in forum");

        return RedirectToAction(nameof(Thread), new { id = threadId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upvote(int id)
    {
        var userId = _community.GetDefaultUserId();
        var count = await _community.UpvoteAsync(id, userId);
        return Json(new { upvotes = count });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadResource(
        IFormFile file,
        string title,
        string summary,
        string tagPrimary,
        string tagSecondary
    )
    {
        if (file == null || file.Length == 0)
            return RedirectToAction(nameof(Index));

        var userId = _community.GetDefaultUserId();

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var data = ms.ToArray();

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;

        await _community.UploadResourceAsync(
            ResourceBucket,
            userId,
            title,
            summary,
            tagPrimary,
            tagSecondary,
            file.FileName,
            contentType,
            data
        );

        return RedirectToAction(nameof(Index), new { tab = "resources" });
    }
}

