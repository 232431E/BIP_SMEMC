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
    private readonly Supabase.Client _supabase;
    private readonly IHttpClientFactory _httpClientFactory;
    private const string ResourceBucket = "community-resources";

    public CommunityController(
        CommunityService community,
        RewardsService rewards,
        Supabase.Client supabase,
        IHttpClientFactory httpClientFactory)
    {
        _community = community;
        _rewards = rewards;
        _supabase = supabase;
        _httpClientFactory = httpClientFactory;
    }

    private string? GetCurrentUserId()
    {
        return HttpContext.Session.GetString("UserEmail");
    }

    private async Task<UserModel?> GetCurrentUserProfileAsync(string userEmail)
    {
        var userRes = await _supabase.From<UserModel>()
            .Where(x => x.Email == userEmail)
            .Get();

        return userRes.Models.FirstOrDefault();
    }

    public async Task<IActionResult> Index(string? tab = null)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");
        var userTask = GetCurrentUserProfileAsync(userId);
        var threadsTask = _community.GetThreadsAsync();

        var profileTask = _community.GetProfileAsync(userId);
        var eventsTask = _community.GetEventsAsync(userId);
        var resourcesTask = _community.GetResourcesAsync();
        var downloadedIdsTask = _community.GetDownloadedResourceIdsAsync(userId);
        var resourceCountTask = _community.GetUserResourceDownloadCountAsync(userId);
        var badgesTask = _community.GetBadgesAsync(userId);

        await Task.WhenAll(userTask, threadsTask, profileTask, eventsTask, resourcesTask, downloadedIdsTask, resourceCountTask, badgesTask);

        var user = userTask.Result;
        var badges = badgesTask.Result;
        var rewardPoints = await _rewards.GetPointsAsync(userId);
        var profile = profileTask.Result ?? new CommunityProfile { UserId = userId };
        profile.DiscussionsCount = await _community.GetDiscussionParticipationCountAsync(userId);
        profile.ReputationPoints = rewardPoints;
        profile.EventsRsvpedCount = eventsTask.Result.Count(e => e.IsRegistered);
        profile.ResourcesDownloadedCount = resourceCountTask.Result;
        badges = await _community.SyncCommunityBadgeProgressAsync(userId, profile, badges);
        profile.BadgesEarnedCount = badges.Count(b => string.Equals(b.Status, "earned", StringComparison.OrdinalIgnoreCase));
        await _supabase.From<CommunityProfile>().Update(profile);
        ViewData["RewardPoints"] = rewardPoints;

        var vm = new CommunityHubViewModel
        {
            Threads = threadsTask.Result,
            Profile = profile,
            Events = eventsTask.Result,
            Resources = resourcesTask.Result,
            DownloadedResourceIds = downloadedIdsTask.Result,
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
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

        var thread = await _community.GetThreadAsync(id);
        if (thread == null)
            return RedirectToAction(nameof(Index));

        var viewerId = User?.Identity?.IsAuthenticated == true
            ? (User.Identity?.Name ?? userId)
            : HttpContext.Session.GetString("community_viewer_id");

        if (string.IsNullOrWhiteSpace(viewerId))
        {
            viewerId = $"anon-{Guid.NewGuid():N}";
            HttpContext.Session.SetString("community_viewer_id", viewerId);
        }

        await _community.TrackViewAsync(id, viewerId);
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
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");
        var user = await GetCurrentUserProfileAsync(userId);

        if (!ModelState.IsValid)
            return View(thread);

        thread.Author = string.IsNullOrWhiteSpace(user?.FullName) ? userId : user.FullName;
        thread.UserId = userId;

        await _community.CreateThreadAsync(thread);

        await _rewards.AddPointsAsync(userId, 25, "Created forum thread");
        var profile = await _community.GetProfileAsync(userId);
        profile.DiscussionsCount = await _community.GetDiscussionParticipationCountAsync(userId);
        await _supabase.From<CommunityProfile>().Update(profile);
        var badges = await _community.GetBadgesAsync(userId);
        await _community.SyncCommunityBadgeProgressAsync(userId, profile, badges);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int threadId, ForumReply reply)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");
        var user = await GetCurrentUserProfileAsync(userId);

        if (!ModelState.IsValid)
            return RedirectToAction(nameof(Thread), new { id = threadId });

        reply.Author = string.IsNullOrWhiteSpace(user?.FullName) ? userId : user.FullName;
        reply.UserId = userId;
        reply.ThreadId = threadId;

        await _community.AddReplyAsync(threadId, reply);

        await _rewards.AddPointsAsync(userId, 10, "Replied in forum");
        var profile = await _community.GetProfileAsync(userId);
        profile.DiscussionsCount = await _community.GetDiscussionParticipationCountAsync(userId);
        await _supabase.From<CommunityProfile>().Update(profile);
        var badges = await _community.GetBadgesAsync(userId);
        await _community.SyncCommunityBadgeProgressAsync(userId, profile, badges);

        return RedirectToAction(nameof(Thread), new { id = threadId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upvote(int id)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        var count = await _community.UpvoteAsync(id, userId);
        return Json(new { upvotes = count });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RsvpEvent(int id)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var ev = await _community.RsvpEventAsync(id, userId);
        if (ev == null) return NotFound();

        var events = await _community.GetEventsAsync(userId);
        var registeredCount = events.Count(e => e.IsRegistered);

        var profile = await _community.GetProfileAsync(userId);
        profile.EventsRsvpedCount = registeredCount;
        await _supabase.From<CommunityProfile>().Update(profile);

        var badges = await _community.GetBadgesAsync(userId);
        await _community.SyncCommunityBadgeProgressAsync(userId, profile, badges);

        return Json(new
        {
            success = true,
            eventId = ev.Id,
            title = ev.Title,
            hostName = ev.HostName,
            hostTitle = ev.HostTitle,
            date = ev.StartAt.ToString("dddd, MMMM d, yyyy"),
            time = ev.StartAt.ToString("h:mm tt"),
            timezone = ev.Timezone,
            location = ev.Location,
            isOnline = ev.IsOnline,
            reminderSet = ev.ReminderSet,
            seatsBooked = ev.SeatsBooked,
            seatsTotal = ev.SeatsTotal,
            registeredCount
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelEventRsvp(int id)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var ev = await _community.CancelRsvpAsync(id, userId);
        if (ev == null) return NotFound();

        var events = await _community.GetEventsAsync(userId);
        var registeredCount = events.Count(e => e.IsRegistered);

        var profile = await _community.GetProfileAsync(userId);
        profile.EventsRsvpedCount = registeredCount;
        await _supabase.From<CommunityProfile>().Update(profile);

        var badges = await _community.GetBadgesAsync(userId);
        await _community.SyncCommunityBadgeProgressAsync(userId, profile, badges);

        return Json(new
        {
            success = true,
            eventId = ev.Id,
            seatsBooked = ev.SeatsBooked,
            seatsTotal = ev.SeatsTotal,
            registeredCount
        });
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
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) return RedirectToAction("Login", "Account");

        if (file == null || file.Length == 0)
            return RedirectToAction(nameof(Index));

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var data = ms.ToArray();

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;
        var user = await GetCurrentUserProfileAsync(userId);

        await _community.UploadResourceAsync(
            ResourceBucket,
            userId,
            string.IsNullOrWhiteSpace(user?.FullName) ? userId : user.FullName,
            title,
            summary,
            tagPrimary,
            tagSecondary,
            file.FileName,
            contentType,
            data
        );

        await _rewards.AddPointsAsync(userId, 10, "Uploaded community resource");

        var returnUrl = Url.Action(nameof(Index), new { tab = "resources" }) ?? "/Community?tab=resources";
        return Redirect($"{returnUrl}#panel-resources");
    }

    [HttpGet]
    public async Task<IActionResult> DownloadResource(int id, bool ajax = false)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            if (ajax) return Json(new { success = false, requiresLogin = true });
            return RedirectToAction("Login", "Account");
        }
        var result = await _community.DownloadResourceAsync(id, userId);
        if (string.IsNullOrWhiteSpace(result.Url))
        {
            if (ajax) return Json(new { success = false });
            return RedirectToAction(nameof(Index), new { tab = "resources" });
        }

        if (result.FirstDownload && result.PointsReward > 0)
        {
            await _rewards.AddPointsAsync(userId, result.PointsReward, "Downloaded community resource");
        }

        var profile = await _community.GetProfileAsync(userId);
        profile.ResourcesDownloadedCount = await _community.GetUserResourceDownloadCountAsync(userId);
        await _supabase.From<CommunityProfile>().Update(profile);
        var badges = await _community.GetBadgesAsync(userId);
        await _community.SyncCommunityBadgeProgressAsync(userId, profile, badges);

        if (ajax)
        {
            var userDownloadCount = await _community.GetUserResourceDownloadCountAsync(userId);
            return Json(new
            {
                success = true,
                downloadUrl = Url.Action(nameof(DownloadResourceFile), new { id }),
                downloadCount = result.DownloadCount,
                firstDownload = result.FirstDownload,
                userDownloadCount
            });
        }

        return RedirectToAction(nameof(DownloadResourceFile), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadResourceFile(int id)
    {
        try
        {
            var resource = await _community.GetResourceAsync(id);
            if (resource == null)
                return RedirectToAction(nameof(Index), new { tab = "resources" });

            var url = _community.GetResourceDownloadUrl(resource);
            if (string.IsNullOrWhiteSpace(url))
                return RedirectToAction(nameof(Index), new { tab = "resources" });

            var client = _httpClientFactory.CreateClient();
            var upstreamResponse = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!upstreamResponse.IsSuccessStatusCode)
                return RedirectToAction(nameof(Index), new { tab = "resources" });

            Response.RegisterForDispose(upstreamResponse);
            var stream = await upstreamResponse.Content.ReadAsStreamAsync();
            var contentType = upstreamResponse.Content.Headers.ContentType?.ToString()
                ?? "application/octet-stream";
            var fileName = string.IsNullOrWhiteSpace(resource.FileName)
                ? $"resource-{id}"
                : resource.FileName;

            return File(stream, contentType, fileName);
        }
        catch
        {
            return RedirectToAction(nameof(Index), new { tab = "resources" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> BadgeSnapshot()
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var profile = await _community.GetProfileAsync(userId);
        var events = await _community.GetEventsAsync(userId);
        profile.DiscussionsCount = await _community.GetDiscussionParticipationCountAsync(userId);
        profile.EventsRsvpedCount = events.Count(e => e.IsRegistered);
        profile.ResourcesDownloadedCount = await _community.GetUserResourceDownloadCountAsync(userId);

        var badges = await _community.GetBadgesAsync(userId);
        badges = await _community.SyncCommunityBadgeProgressAsync(userId, profile, badges);

        profile.BadgesEarnedCount = badges.Count(b => string.Equals(b.Status, "earned", StringComparison.OrdinalIgnoreCase));
        await _supabase.From<CommunityProfile>().Update(profile);

        var progress = badges
            .Where(b => string.Equals(b.Status, "in_progress", StringComparison.OrdinalIgnoreCase))
            .Select(b => new
            {
                title = b.Title,
                current = b.ProgressCurrent ?? 0,
                target = b.ProgressTarget ?? 0,
                percent = b.ProgressPercent ?? 0
            })
            .ToList();

        return Json(new
        {
            badgeCount = profile.BadgesEarnedCount,
            progress
        });
    }
}



