using BIP_SMEMC.Models;

namespace BIP_SMEMC.Services;

public class CommunityService
{
    private const string DefaultUserId = "Admin";
    private const string ResourceBucketName = "community-resources";
    private readonly Supabase.Client _supabase;

    public CommunityService(Supabase.Client supabase)
    {
        _supabase = supabase;
    }

    public string GetDefaultUserId() => DefaultUserId;

    public async Task<List<ForumThread>> GetThreadsAsync(string? search = null)
    {
        var threadsRes = string.IsNullOrWhiteSpace(search)
            ? await _supabase.From<ForumThread>()
                .Order("upvotes", Postgrest.Constants.Ordering.Descending)
                .Order("created_at", Postgrest.Constants.Ordering.Descending)
                .Get()
            : await _supabase.From<ForumThread>()
                .Filter("title", Postgrest.Constants.Operator.ILike, $"%{search}%")
                .Order("upvotes", Postgrest.Constants.Ordering.Descending)
                .Order("created_at", Postgrest.Constants.Ordering.Descending)
                .Get();

        var threads = threadsRes.Models;
        if (threads.Count == 0) return threads;

        var repliesRes = await _supabase.From<ForumReply>().Get();
        var replyCounts = repliesRes.Models
            .GroupBy(r => r.ThreadId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var thread in threads)
        {
            thread.Tags = ParseTags(thread.TagsRaw);
            thread.RepliesCount = replyCounts.TryGetValue(thread.Id, out var count) ? count : 0;
        }

        return threads;
    }

    public async Task<int> GetDiscussionParticipationCountAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return 0;

        var participatedThreadIds = new HashSet<int>();

        var threadsRes = await _supabase.From<ForumThread>().Get();
        foreach (var thread in threadsRes.Models)
        {
            if (string.Equals(thread.UserId, userId, StringComparison.OrdinalIgnoreCase))
            {
                participatedThreadIds.Add(thread.Id);
            }
        }

        var repliesRes = await _supabase.From<ForumReply>().Get();
        foreach (var reply in repliesRes.Models)
        {
            if (string.Equals(reply.UserId, userId, StringComparison.OrdinalIgnoreCase))
            {
                participatedThreadIds.Add(reply.ThreadId);
            }
        }

        return participatedThreadIds.Count;
    }

    public async Task<ForumThread?> GetThreadAsync(int id)
    {
        var threadRes = await _supabase.From<ForumThread>()
            .Filter("id", Postgrest.Constants.Operator.Equals, id)
            .Get();

        var thread = threadRes.Models.FirstOrDefault();
        if (thread == null) return null;

        thread.Tags = ParseTags(thread.TagsRaw);

        var repliesRes = await _supabase.From<ForumReply>()
            .Filter("thread_id", Postgrest.Constants.Operator.Equals, id)
            .Order("created_at", Postgrest.Constants.Ordering.Descending)
            .Get();

        thread.Replies = repliesRes.Models;
        thread.RepliesCount = thread.Replies.Count;
        return thread;
    }

    public async Task CreateThreadAsync(ForumThread thread)
    {
        if (thread.Id <= 0)
            thread.Id = await GetNextThreadIdAsync();

        thread.CreatedAt = DateTime.UtcNow;
        thread.TagsRaw = SerializeTags(thread.Tags);
        thread.ViewCount = Math.Max(0, thread.ViewCount);
        thread.Upvotes = Math.Max(0, thread.Upvotes);

        await _supabase.From<ForumThread>().Insert(thread);
    }

    public async Task AddReplyAsync(int threadId, ForumReply reply)
    {
        if (reply.Id <= 0)
            reply.Id = await GetNextReplyIdAsync();

        reply.ThreadId = threadId;
        reply.CreatedAt = DateTime.UtcNow;
        await _supabase.From<ForumReply>().Insert(reply);
    }

    public async Task<int> UpvoteAsync(int threadId, string userId)
    {
        var existingVote = await _supabase.From<CommunityThreadVote>()
            .Filter("thread_id", Postgrest.Constants.Operator.Equals, threadId)
            .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
            .Get();

        if (existingVote.Models.Any())
        {
            var currentThread = await _supabase.From<ForumThread>()
                .Filter("id", Postgrest.Constants.Operator.Equals, threadId)
                .Get();
            return currentThread.Models.FirstOrDefault()?.Upvotes ?? 0;
        }

        var vote = new CommunityThreadVote
        {
            Id = await GetNextVoteIdAsync(),
            ThreadId = threadId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        await _supabase.From<CommunityThreadVote>().Insert(vote);

        var threadRes = await _supabase.From<ForumThread>()
            .Filter("id", Postgrest.Constants.Operator.Equals, threadId)
            .Get();

        var thread = threadRes.Models.FirstOrDefault();
        if (thread == null) return 0;

        var updated = new CommunityThreadUpdate
        {
            Id = thread.Id,
            Upvotes = thread.Upvotes + 1
        };

        var updateRes = await _supabase.From<CommunityThreadUpdate>().Update(updated);
        return updateRes.Models.FirstOrDefault()?.Upvotes ?? thread.Upvotes + 1;
    }

    public async Task TrackViewAsync(int threadId, string viewerId)
    {
        try
        {
            var existingView = await _supabase.From<CommunityThreadView>()
                .Filter("thread_id", Postgrest.Constants.Operator.Equals, threadId)
                .Filter("user_id", Postgrest.Constants.Operator.Equals, viewerId)
                .Get();

            if (existingView.Models.Any())
                return;

            var view = new CommunityThreadView
            {
                Id = await GetNextViewIdAsync(),
                ThreadId = threadId,
                UserId = viewerId,
                CreatedAt = DateTime.UtcNow
            };
            await _supabase.From<CommunityThreadView>().Insert(view);
        }
        catch (Exception ex) when (MissingRelation(ex, "community_thread_views"))
        {
            // Backward-compat fallback: continue and update thread view count.
        }
        catch
        {
            // Tracking failures should not break thread page rendering.
            return;
        }

        var threadRes = await _supabase.From<ForumThread>()
            .Filter("id", Postgrest.Constants.Operator.Equals, threadId)
            .Get();

        var thread = threadRes.Models.FirstOrDefault();
        if (thread == null) return;

        var updated = new CommunityThreadUpdate
        {
            Id = thread.Id,
            ViewCount = thread.ViewCount + 1,
            Upvotes = thread.Upvotes
        };

        await _supabase.From<CommunityThreadUpdate>().Update(updated);
    }

    public async Task<CommunityProfile> GetProfileAsync(string userId)
    {
        var res = await _supabase.From<CommunityProfile>()
            .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
            .Get();

        var profile = res.Models.FirstOrDefault();
        if (profile != null)
            return profile;

        var created = new CommunityProfile
        {
            UserId = userId,
            ReputationPoints = 0,
            DiscussionsCount = 0,
            EventsRsvpedCount = 0,
            ResourcesDownloadedCount = 0,
            BadgesEarnedCount = 0
        };

        var insert = await _supabase.From<CommunityProfile>().Insert(created);
        return insert.Models.FirstOrDefault() ?? created;
    }

    public async Task<List<CommunityEvent>> GetEventsAsync(string userId)
    {
        var res = await _supabase.From<CommunityEvent>()
            .Order("start_at", Postgrest.Constants.Ordering.Ascending)
            .Get();

        var events = res.Models;
        try
        {
            var rsvpRes = await _supabase.From<CommunityEventRsvp>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();

            var byEvent = rsvpRes.Models.ToDictionary(x => x.EventId, x => x);
            foreach (var ev in events)
            {
                var isRegistered = byEvent.ContainsKey(ev.Id);
                var reminderSet = byEvent.TryGetValue(ev.Id, out var row) && row.ReminderSet;

                ev.IsRegistered = isRegistered;
                ev.ReminderSet = reminderSet;
                ev.StatusLabel = isRegistered ? "You're Registered" : null;
                ev.ActionLabel = isRegistered ? "Cancel RSVP" : "RSVP";
            }
        }
        catch (Exception ex) when (MissingRelation(ex, "community_event_rsvps"))
        {
            // Backward compatibility: rely on event-level registration flags.
        }

        return events;
    }

    public async Task<CommunityEvent?> GetEventAsync(int eventId, string userId)
    {
        var events = await GetEventsAsync(userId);
        return events.FirstOrDefault(x => x.Id == eventId);
    }

    public async Task<CommunityEvent?> RsvpEventAsync(int eventId, string userId)
    {
        var eventRes = await _supabase.From<CommunityEvent>()
            .Filter("id", Postgrest.Constants.Operator.Equals, eventId)
            .Get();
        var ev = eventRes.Models.FirstOrDefault();
        if (ev == null) return null;

        try
        {
            var existing = await _supabase.From<CommunityEventRsvp>()
                .Filter("event_id", Postgrest.Constants.Operator.Equals, eventId)
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();

            if (!existing.Models.Any())
            {
                await _supabase.From<CommunityEventRsvp>().Insert(new CommunityEventRsvp
                {
                    EventId = eventId,
                    UserId = userId,
                    ReminderSet = true,
                    CreatedAt = DateTime.UtcNow
                });

                ev.SeatsBooked = Math.Min(ev.SeatsTotal, ev.SeatsBooked + 1);
                await _supabase.From<CommunityEvent>().Update(ev);
            }
        }
        catch (Exception ex) when (MissingRelation(ex, "community_event_rsvps"))
        {
            if (!ev.IsRegistered)
            {
                ev.IsRegistered = true;
                ev.ReminderSet = true;
                ev.StatusLabel = "You're Registered";
                ev.ActionLabel = "Cancel RSVP";
                ev.SeatsBooked = Math.Min(ev.SeatsTotal, ev.SeatsBooked + 1);
                await _supabase.From<CommunityEvent>().Update(ev);
            }
        }

        return await GetEventAsync(eventId, userId);
    }

    public async Task<CommunityEvent?> CancelRsvpAsync(int eventId, string userId)
    {
        var eventRes = await _supabase.From<CommunityEvent>()
            .Filter("id", Postgrest.Constants.Operator.Equals, eventId)
            .Get();
        var ev = eventRes.Models.FirstOrDefault();
        if (ev == null) return null;

        try
        {
            var existing = await _supabase.From<CommunityEventRsvp>()
                .Filter("event_id", Postgrest.Constants.Operator.Equals, eventId)
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();

            var row = existing.Models.FirstOrDefault();
            if (row != null)
            {
                await _supabase.From<CommunityEventRsvp>().Delete(row);
                ev.SeatsBooked = Math.Max(0, ev.SeatsBooked - 1);
                await _supabase.From<CommunityEvent>().Update(ev);
            }
        }
        catch (Exception ex) when (MissingRelation(ex, "community_event_rsvps"))
        {
            if (ev.IsRegistered)
            {
                ev.IsRegistered = false;
                ev.ReminderSet = false;
                ev.StatusLabel = null;
                ev.ActionLabel = "RSVP";
                ev.SeatsBooked = Math.Max(0, ev.SeatsBooked - 1);
                await _supabase.From<CommunityEvent>().Update(ev);
            }
        }

        return await GetEventAsync(eventId, userId);
    }

    public async Task<List<CommunityResource>> GetResourcesAsync()
    {
        var res = await _supabase.From<CommunityResource>()
            .Order("created_at", Postgrest.Constants.Ordering.Descending)
            .Get();
        return res.Models
            .Where(r => !string.IsNullOrWhiteSpace(r.FileUrl) || !string.IsNullOrWhiteSpace(r.FilePath))
            .ToList();
    }

    public async Task<HashSet<int>> GetDownloadedResourceIdsAsync(string userId)
    {
        try
        {
            var res = await _supabase.From<CommunityResourceDownload>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();

            return res.Models.Select(x => x.ResourceId).ToHashSet();
        }
        catch (Exception ex) when (MissingRelation(ex, "community_resource_downloads"))
        {
            return new HashSet<int>();
        }
    }

    public async Task<int> GetUserResourceDownloadCountAsync(string userId)
    {
        try
        {
            var res = await _supabase.From<CommunityResourceDownload>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();
            return res.Models.Count;
        }
        catch (Exception ex) when (MissingRelation(ex, "community_resource_downloads"))
        {
            var profile = await GetProfileAsync(userId);
            return profile.ResourcesDownloadedCount;
        }
    }

    public async Task<(string? Url, bool FirstDownload, int DownloadCount, int PointsReward)> DownloadResourceAsync(int resourceId, string userId)
    {
        var resource = await GetResourceAsync(resourceId);
        if (resource == null)
            return (null, false, 0, 0);

        var url = GetResourceUrl(resource);
        if (string.IsNullOrWhiteSpace(url))
            return (null, false, resource.DownloadCount, resource.PointsReward);

        var firstDownload = false;
        try
        {
            var existing = await _supabase.From<CommunityResourceDownload>()
                .Filter("resource_id", Postgrest.Constants.Operator.Equals, resourceId)
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();

            firstDownload = !existing.Models.Any();
            if (firstDownload)
            {
                var record = new CommunityResourceDownload
                {
                    ResourceId = resourceId,
                    UserId = userId,
                    DownloadedAt = DateTime.UtcNow
                };
                await _supabase.From<CommunityResourceDownload>().Insert(record);

                var profile = await GetProfileAsync(userId);
                profile.ResourcesDownloadedCount += 1;
                await _supabase.From<CommunityProfile>().Update(profile);
            }
        }
        catch (Exception ex) when (MissingRelation(ex, "community_resource_downloads"))
        {
            // Backward-compat fallback: continue download path without per-user tracking.
        }

        resource.DownloadCount += 1;
        await _supabase.From<CommunityResource>().Update(resource);

        return (url, firstDownload, resource.DownloadCount, resource.PointsReward);
    }

    public async Task<CommunityResource?> GetResourceAsync(int resourceId)
    {
        var resourceRes = await _supabase.From<CommunityResource>()
            .Filter("id", Postgrest.Constants.Operator.Equals, resourceId)
            .Get();
        return resourceRes.Models.FirstOrDefault();
    }

    public string? GetResourceDownloadUrl(CommunityResource resource)
    {
        return GetResourceUrl(resource);
    }

    public async Task<CommunityResource> UploadResourceAsync(
        string bucketName,
        string userId,
        string author,
        string title,
        string summary,
        string tagPrimary,
        string tagSecondary,
        string fileName,
        string contentType,
        byte[] data
    )
    {
        var safeFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
        var storage = _supabase.Storage.From(bucketName);

        await storage.Upload(data, safeFileName, new Supabase.Storage.FileOptions
        {
            ContentType = contentType,
            CacheControl = "3600",
            Upsert = false
        });

        var publicUrl = storage.GetPublicUrl(safeFileName);

        var resource = new CommunityResource
        {
            Title = title,
            Author = author,
            UserId = userId,
            Summary = summary,
            TagPrimary = tagPrimary,
            TagSecondary = tagSecondary,
            FileType = string.IsNullOrWhiteSpace(contentType) ? "File" : contentType,
            FileName = fileName,
            FilePath = safeFileName,
            FileUrl = publicUrl,
            FileSize = data.Length,
            DownloadCount = 0,
            PointsReward = 5
        };

        var insert = await _supabase.From<CommunityResource>().Insert(resource);
        return insert.Models.FirstOrDefault() ?? resource;
    }

    public async Task<List<CommunityBadge>> GetBadgesAsync(string userId)
    {
        var userBadges = await _supabase.From<CommunityBadge>()
            .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
            .Order("id", Postgrest.Constants.Ordering.Ascending)
            .Get();

        var badges = userBadges.Models
            .Where(IsCommunityTabBadge)
            .ToList();

        try
        {
            var catalogRes = await _supabase.From<CommunityBadgeCatalog>()
                .Order("badge_id", Postgrest.Constants.Ordering.Ascending)
                .Get();

            var existing = new HashSet<string>(
                badges.Select(b => b.Title.Trim().ToLowerInvariant())
            );

            var nextSyntheticId = badges.Count == 0 ? 900000 : badges.Max(b => b.Id) + 1;
            foreach (var item in catalogRes.Models.Where(IsForumBadge))
            {
                var key = item.Name.Trim().ToLowerInvariant();
                if (existing.Contains(key)) continue;

                badges.Add(new CommunityBadge
                {
                    Id = nextSyntheticId++,
                    UserId = userId,
                    Title = item.Name,
                    Description = item.Description,
                    Status = "in_progress",
                    Points = item.PointsReward,
                    ProgressCurrent = 0,
                    ProgressTarget = 1,
                    ProgressPercent = 0,
                    Icon = ToBootstrapIcon(item.IconName),
                    IconColor = "text-warning"
                });
            }
        }
        catch (Exception ex) when (MissingRelation(ex, "community_badge_catalog"))
        {
            // no catalog table; return user badges only
        }

        if (badges.Count == 0)
        {
            badges.AddRange(BuildDefaultCommunityBadgeSet(userId));
        }

        return badges
            .OrderBy(b => string.Equals(b.Status, "earned", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(b => b.Points)
            .ThenBy(b => b.Title)
            .ToList();
    }

    public async Task<List<CommunityBadge>> SyncCommunityBadgeProgressAsync(string userId, CommunityProfile profile, List<CommunityBadge>? badges = null)
    {
        badges ??= await GetBadgesAsync(userId);

        var threads = await _supabase.From<ForumThread>()
            .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
            .Get();
        var userThreads = threads.Models;

        var replies = await _supabase.From<ForumReply>()
            .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
            .Get();
        var userReplies = replies.Models;

        var resources = await _supabase.From<CommunityResource>()
            .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
            .Get();
        var userUploadsCount = resources.Models.Count;

        var userDownloadsCount = await GetUserResourceDownloadCountAsync(userId);

        var userEventsCount = profile.EventsRsvpedCount;
        try
        {
            var rsvps = await _supabase.From<CommunityEventRsvp>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();
            userEventsCount = rsvps.Models.Count;
        }
        catch (Exception ex) when (MissingRelation(ex, "community_event_rsvps"))
        {
            // fallback to profile snapshot
        }

        var totalUpvotesAcrossOwnThreads = userThreads.Sum(t => Math.Max(0, t.Upvotes));
        var maxUpvotesOnSingleOwnThread = userThreads.Count == 0 ? 0 : userThreads.Max(t => Math.Max(0, t.Upvotes));
        var distinctThreadsRepliedTo = userReplies.Select(r => r.ThreadId).Distinct().Count();
        var createdThreadsCount = userThreads.Count;
        var repliesCount = userReplies.Count;

        foreach (var badge in badges.Where(IsCommunityTabBadge))
        {
            var metricValue = GetBadgeMetricValue(
                badge,
                createdThreadsCount,
                repliesCount,
                distinctThreadsRepliedTo,
                userEventsCount,
                userUploadsCount,
                userDownloadsCount,
                totalUpvotesAcrossOwnThreads,
                maxUpvotesOnSingleOwnThread);
            var target = badge.ProgressTarget.GetValueOrDefault();
            var parsedTarget = ExtractFirstPositiveInt(badge.Description);
            if (parsedTarget <= 0)
                parsedTarget = ExtractFirstPositiveInt(badge.Title);

            // Repair previously seeded badges that were created with target=1.
            if (parsedTarget > 1 && target <= 1)
                target = parsedTarget;

            if (target <= 0)
                target = parsedTarget;
            if (target <= 0)
                target = 1;

            var current = Math.Min(metricValue, target);
            var percent = target <= 0 ? 0 : (int)Math.Round((double)current * 100 / target);
            var earned = current >= target;

            var nextStatus = earned ? "earned" : "in_progress";
            DateTime? nextEarnedAt = earned ? (badge.EarnedAt ?? DateTime.UtcNow.Date) : null;

            var changed =
                badge.ProgressCurrent != current ||
                badge.ProgressTarget != target ||
                badge.ProgressPercent != percent ||
                !string.Equals(badge.Status, nextStatus, StringComparison.OrdinalIgnoreCase) ||
                badge.EarnedAt != nextEarnedAt;

            badge.ProgressCurrent = current;
            badge.ProgressTarget = target;
            badge.ProgressPercent = percent;
            badge.Status = nextStatus;
            badge.EarnedAt = nextEarnedAt;

            // Synthetic catalog-only badges are generated with ids >= 900000 and are not persisted.
            if (!changed || badge.Id >= 900000)
                continue;

            try
            {
                await _supabase.From<CommunityBadge>().Update(badge);
            }
            catch
            {
                // Do not block page load if a single badge update fails.
            }
        }

        return badges
            .OrderBy(b => string.Equals(b.Status, "earned", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(b => b.Points)
            .ThenBy(b => b.Title)
            .ToList();
    }

    private async Task<int> GetNextThreadIdAsync()
    {
        var res = await _supabase.From<ForumThread>()
            .Order("id", Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get();

        return (res.Models.FirstOrDefault()?.Id ?? 0) + 1;
    }

    private async Task<int> GetNextReplyIdAsync()
    {
        var res = await _supabase.From<ForumReply>()
            .Order("id", Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get();

        return (res.Models.FirstOrDefault()?.Id ?? 0) + 1;
    }

    private async Task<int> GetNextVoteIdAsync()
    {
        var res = await _supabase.From<CommunityThreadVote>()
            .Order("id", Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get();

        return (res.Models.FirstOrDefault()?.Id ?? 0) + 1;
    }

    private async Task<int> GetNextViewIdAsync()
    {
        var res = await _supabase.From<CommunityThreadView>()
            .Order("id", Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get();

        return (res.Models.FirstOrDefault()?.Id ?? 0) + 1;
    }

    private static List<string> ParseTags(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static string SerializeTags(List<string> tags)
    {
        return tags == null || tags.Count == 0
            ? ""
            : string.Join(", ", tags);
    }

    private string? GetResourceUrl(CommunityResource resource)
    {
        if (!string.IsNullOrWhiteSpace(resource.FileUrl))
            return resource.FileUrl;

        if (!string.IsNullOrWhiteSpace(resource.FilePath))
            return _supabase.Storage.From(ResourceBucketName).GetPublicUrl(resource.FilePath);

        return null;
    }

    private static bool MissingRelation(Exception ex, string relationName)
    {
        return ex.Message.Contains(relationName, StringComparison.OrdinalIgnoreCase)
            && (ex.Message.Contains("PGRST205", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("could not find the table", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("relation", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsForumBadge(CommunityBadgeCatalog badge)
    {
        var haystack = $"{badge.Category} {badge.Name} {badge.Description} {badge.Requirements}".ToLowerInvariant();
        return haystack.Contains("thread")
            || haystack.Contains("discussion")
            || haystack.Contains("reply")
            || haystack.Contains("event")
            || haystack.Contains("rsvp")
            || haystack.Contains("resource")
            || haystack.Contains("download")
            || haystack.Contains("upload");
    }

    private static bool IsCommunityTabBadge(CommunityBadge badge)
    {
        var haystack = $"{badge.Title} {badge.Description}".ToLowerInvariant();
        return haystack.Contains("thread")
            || haystack.Contains("discussion")
            || haystack.Contains("reply")
            || haystack.Contains("event")
            || haystack.Contains("rsvp")
            || haystack.Contains("resource")
            || haystack.Contains("download")
            || haystack.Contains("upload");
    }

    private static int GetBadgeMetricValue(
        CommunityBadge badge,
        int createdThreadsCount,
        int repliesCount,
        int distinctThreadsRepliedTo,
        int userEventsCount,
        int userUploadsCount,
        int userDownloadsCount,
        int totalUpvotesAcrossOwnThreads,
        int maxUpvotesOnSingleOwnThread)
    {
        var haystack = $"{badge.Title} {badge.Description}".ToLowerInvariant();

        if (haystack.Contains("total upvote"))
            return Math.Max(0, totalUpvotesAcrossOwnThreads);

        if (haystack.Contains("upvote"))
            return Math.Max(0, maxUpvotesOnSingleOwnThread);

        if (haystack.Contains("event") || haystack.Contains("rsvp"))
            return Math.Max(0, userEventsCount);

        if (haystack.Contains("upload"))
            return Math.Max(0, userUploadsCount);

        if (haystack.Contains("download"))
            return Math.Max(0, userDownloadsCount);

        if (haystack.Contains("reply") && haystack.Contains("different") && haystack.Contains("thread"))
            return Math.Max(0, distinctThreadsRepliedTo);

        if (haystack.Contains("reply") || haystack.Contains("feedback"))
            return Math.Max(0, repliesCount);

        if (haystack.Contains("thread") || haystack.Contains("discussion"))
            return Math.Max(0, createdThreadsCount);

        if (haystack.Contains("resource"))
            return Math.Max(0, userUploadsCount);

        return 0;
    }

    private static int ExtractFirstPositiveInt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var current = new List<char>();
        foreach (var ch in text)
        {
            if (char.IsDigit(ch))
            {
                current.Add(ch);
                continue;
            }

            if (current.Count > 0)
                break;
        }

        if (current.Count == 0)
            return 0;

        return int.TryParse(new string(current.ToArray()), out var parsed) && parsed > 0
            ? parsed
            : 0;
    }

    private static List<CommunityBadge> BuildDefaultCommunityBadgeSet(string userId)
    {
        return new List<CommunityBadge>
        {
            new()
            {
                Id = 990001,
                UserId = userId,
                Title = "Discussion Starter",
                Description = "Create 5 discussion threads",
                Status = "in_progress",
                Points = 200,
                ProgressCurrent = 0,
                ProgressTarget = 5,
                ProgressPercent = 0,
                Icon = "bi-chat-square-dots",
                IconColor = "text-warning"
            },
            new()
            {
                Id = 990002,
                UserId = userId,
                Title = "Event Regular",
                Description = "RSVP to 3 community events",
                Status = "in_progress",
                Points = 250,
                ProgressCurrent = 0,
                ProgressTarget = 3,
                ProgressPercent = 0,
                Icon = "bi-calendar-event",
                IconColor = "text-warning"
            },
            new()
            {
                Id = 990003,
                UserId = userId,
                Title = "Resource Collector",
                Description = "Download 10 resources",
                Status = "in_progress",
                Points = 100,
                ProgressCurrent = 0,
                ProgressTarget = 10,
                ProgressPercent = 0,
                Icon = "bi-download",
                IconColor = "text-warning"
            }
        };
    }

    private static string ToBootstrapIcon(string iconName)
    {
        return (iconName ?? "").Trim().ToLowerInvariant() switch
        {
            "message-square" => "bi-chat-square-dots",
            "message-circle" => "bi-chat-dots",
            "calendar" => "bi-calendar-event",
            "ticket" => "bi-ticket-perforated",
            "download" => "bi-download",
            "users" => "bi-people",
            "award" => "bi-award",
            "medal" => "bi-award",
            "trophy" => "bi-trophy",
            _ => "bi-award"
        };
    }
}
