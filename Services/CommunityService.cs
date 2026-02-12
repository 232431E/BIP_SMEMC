using BIP_SMEMC.Models;

namespace BIP_SMEMC.Services;

public class CommunityService
{
    private const string DefaultUserId = "Admin";
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
                .Order("created_at", Postgrest.Constants.Ordering.Descending)
                .Get()
            : await _supabase.From<ForumThread>()
                .Filter("title", Postgrest.Constants.Operator.ILike, $"%{search}%")
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
        thread.CreatedAt = DateTime.UtcNow;
        thread.TagsRaw = SerializeTags(thread.Tags);
        thread.ViewCount = Math.Max(0, thread.ViewCount);
        thread.Upvotes = Math.Max(0, thread.Upvotes);

        await _supabase.From<ForumThread>().Insert(thread);
    }

    public async Task AddReplyAsync(int threadId, ForumReply reply)
    {
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

    public async Task IncrementViewAsync(int threadId)
    {
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

    public async Task<List<CommunityEvent>> GetEventsAsync()
    {
        var res = await _supabase.From<CommunityEvent>()
            .Order("start_at", Postgrest.Constants.Ordering.Ascending)
            .Get();
        return res.Models;
    }

    public async Task<List<CommunityResource>> GetResourcesAsync()
    {
        var res = await _supabase.From<CommunityResource>()
            .Order("id", Postgrest.Constants.Ordering.Ascending)
            .Get();
        return res.Models;
    }

    public async Task<CommunityResource> UploadResourceAsync(
        string bucketName,
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
        var res = await _supabase.From<CommunityBadge>()
            .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
            .Order("id", Postgrest.Constants.Ordering.Ascending)
            .Get();
        return res.Models;
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
}
