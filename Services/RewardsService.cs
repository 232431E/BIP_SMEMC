using BIP_SMEMC.Models;

namespace BIP_SMEMC.Services;

public class RewardsService
{
    private static readonly List<Reward> _rewards = new()
    {
        new Reward { Id = 1, Category = "Dining", Title = "Starbucks $50 Voucher", Partner = "Starbucks", PointsCost = 500, Description = "Enjoy your favorite coffee and treats." },
        new Reward { Id = 2, Category = "Transport", Title = "Grab Ride Voucher", Partner = "Grab", PointsCost = 300, Description = "$30 credit for Grab rides." },
        new Reward { Id = 3, Category = "Shopping", Title = "Amazon Gift Card", Partner = "Amazon", PointsCost = 1000, Description = "$100 shopping credit on Amazon." },
        new Reward { Id = 4, Category = "Premium", Title = "Premium Features - 3 Months", Partner = "Optiflow.AI", PointsCost = 750, Description = "Access advanced analytics and AI insights." },
        new Reward { Id = 5, Category = "Shopping", Title = "FairPrice $25 Voucher", Partner = "FairPrice", PointsCost = 250, Description = "Grocery shopping voucher." },
        new Reward { Id = 6, Category = "Entertainment", Title = "Golden Village Movie Tickets", Partner = "Golden Village", PointsCost = 400, Description = "2 movie tickets for any show." }
    };

    private static readonly int[] AchievementThresholds = [300, 600, 900, 1200, 1500, 2000];
    private const int WelcomeBonus = 1000;
    private readonly Supabase.Client _supabase;

    public RewardsService(Supabase.Client supabase)
    {
        _supabase = supabase;
    }

    public List<Reward> GetRewards() => _rewards;

    public int GetUnlockedAchievementCount(int points)
    {
        return AchievementThresholds.Count(t => points >= t);
    }

    public async Task<int> GetPointsAsync(string userId)
    {
        await EnsureSignupBonusAsync(userId);
        return await GetPointsInternalAsync(userId);
    }

    public async Task AddPointsAsync(string userId, int amount, string activity)
    {
        await EnsureSignupBonusAsync(userId);
        await ChangePointsInternalAsync(userId, amount);
        await AddHistorySafeAsync(userId, activity, amount);
    }

    public async Task<(bool Success, Reward? Reward, string Error)> TryRedeemAsync(string userId, int rewardId)
    {
        var reward = _rewards.FirstOrDefault(r => r.Id == rewardId);
        if (reward == null)
        {
            return (false, null, "Reward not found.");
        }

        var points = await GetPointsInternalAsync(userId);
        if (points < reward.PointsCost)
        {
            return (false, reward, $"Not enough points. You have {points}, need {reward.PointsCost}.");
        }

        await ChangePointsInternalAsync(userId, -reward.PointsCost);
        await AddHistorySafeAsync(userId, $"Redeemed: {reward.Title}", -reward.PointsCost);

        return (true, reward, "");
    }

    public async Task<List<RewardHistoryItem>> GetHistoryAsync(string userId)
    {
        try
        {
            await EnsureSignupBonusAsync(userId);
            var history = await GetHistoryRowsAsync(userId);
            return history
                .Where(h => !string.Equals(h.Activity, "Balance sync adjustment", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (MissingRelation(ex, "reward_history"))
        {
            return new List<RewardHistoryItem>();
        }
    }

    public async Task<List<CommunityBadge>> GetAchievementsAsync(string userId)
    {
        await EnsureSignupBonusAsync(userId);
        var catalog = await GetAchievementCatalogAsync();
        var userBadges = await GetUserBadgesAsync(userId);
        if (catalog.Count == 0) return userBadges;

        var catalogTitles = new HashSet<string>(
            catalog.Select(c => c.Name.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        var merged = new List<CommunityBadge>(
            userBadges.Where(b => catalogTitles.Contains(b.Title.Trim().ToLowerInvariant())));
        var existing = new HashSet<string>(
            merged.Select(b => b.Title.Trim().ToLowerInvariant())
        );

        var nextSyntheticId = merged.Count == 0 ? 900000 : merged.Max(b => b.Id) + 1;
        foreach (var item in catalog)
        {
            var key = item.Name.Trim().ToLowerInvariant();
            if (existing.Contains(key)) continue;

            merged.Add(new CommunityBadge
            {
                Id = nextSyntheticId++,
                UserId = userId,
                Title = item.Name,
                Description = item.Description,
                Status = "in_progress",
                Points = item.Points,
                ProgressCurrent = 0,
                ProgressTarget = 1,
                ProgressPercent = 0,
                Icon = ToBootstrapIcon(item.IconName),
                IconColor = "text-warning"
            });
        }

        await SyncAchievementProgressAsync(userId, merged, catalog);

        return merged
            .OrderBy(b => string.Equals(b.Status, "earned", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(b => b.Points)
            .ThenBy(b => b.Title)
            .ToList();
    }

    private async Task SyncAchievementProgressAsync(
        string userId,
        List<CommunityBadge> badges,
        List<RewardAchievementCatalog> catalog)
    {
        var learningProgress = await GetLearningProgressRowsAsync(userId);
        var completedModules = learningProgress.Count(x => x.Passed);
        var totalModules = await GetTotalModulesCountAsync();
        var anyPerfectQuiz = learningProgress.Any(x => x.BestScore >= 100);

        var threads = await GetAllThreadsAsync();
        var replies = await GetAllRepliesAsync();
        var resources = await GetAllResourcesAsync();

        var userThreads = threads.Where(t => string.Equals(t.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
        var userRepliesCount = replies.Count(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase));
        var maxSingleThreadUpvotes = userThreads.Count == 0 ? 0 : userThreads.Max(t => Math.Max(0, t.Upvotes));
        var uploadedResourcesCount = resources.Count(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase));

        var downloadedResources = await GetUserResourceDownloadCountAsync(userId);
        var rsvps = await GetUserEventRsvpCountAsync(userId);
        var communityBadgesEarned = await GetCommunityBadgesEarnedCountAsync(userId);

        var history = await GetHistorySafeAsync(userId);
        var totalEarnedPoints = history.Where(h => h.Points > 0).Sum(h => h.Points);
        if (totalEarnedPoints <= 0)
            totalEarnedPoints = await GetPointsInternalAsync(userId);

        foreach (var badge in badges)
        {
            var catalogItem = catalog.FirstOrDefault(c =>
                string.Equals(c.Name.Trim(), badge.Title.Trim(), StringComparison.OrdinalIgnoreCase));

            var fallbackTarget = ExtractFirstPositiveInt(catalogItem?.UnlockCriteria)
                                ?? ExtractFirstPositiveInt(badge.Description)
                                ?? 1;

            var target = badge.ProgressTarget.GetValueOrDefault(fallbackTarget);
            if (fallbackTarget > 1 && target <= 1)
                target = fallbackTarget;
            if (target <= 0)
                target = fallbackTarget;

            var metric = GetAchievementMetric(
                badge.Title,
                completedModules,
                anyPerfectQuiz,
                userThreads.Count,
                maxSingleThreadUpvotes,
                userRepliesCount,
                rsvps,
                uploadedResourcesCount,
                downloadedResources,
                totalEarnedPoints,
                communityBadgesEarned);

            if (badge.Title.Contains("Pass all modules", StringComparison.OrdinalIgnoreCase))
                target = Math.Max(1, totalModules);

            var current = Math.Max(0, Math.Min(metric, target));
            var percent = target <= 0 ? 0 : (int)Math.Round((double)current * 100 / target);
            var earned = current >= target;
            var nextStatus = earned ? "earned" : "in_progress";
            var nextEarnedAt = earned ? (badge.EarnedAt ?? (DateTime?)DateTime.UtcNow.Date) : null;

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

            if (!changed || badge.Id >= 900000)
                continue;

            try
            {
                await _supabase.From<CommunityBadge>().Update(badge);
            }
            catch
            {
                // Do not break reward page rendering on a single badge write failure.
            }
        }
    }

    private static int GetAchievementMetric(
        string title,
        int completedModules,
        bool anyPerfectQuiz,
        int threadsCreated,
        int maxSingleThreadUpvotes,
        int repliesCount,
        int rsvps,
        int uploads,
        int downloads,
        int totalEarnedPoints,
        int communityBadgesEarned)
    {
        return title.Trim().ToLowerInvariant() switch
        {
            "first steps" => completedModules,
            "knowledge seeker" => completedModules,
            "scholar" => completedModules,
            "master learner" => completedModules,
            "perfect score" => anyPerfectQuiz ? 1 : 0,
            "community voice" => threadsCreated,
            "discussion leader" => threadsCreated,
            "popular voice" => maxSingleThreadUpvotes,
            "helpful member" => repliesCount,
            "event enthusiast" => rsvps,
            "social butterfly" => rsvps,
            "resource contributor" => uploads,
            "knowledge sharer" => uploads,
            "resource hunter" => downloads,
            "point collector" => totalEarnedPoints,
            "point master" => totalEarnedPoints,
            "badge collector" => communityBadgesEarned,
            "badge enthusiast" => communityBadgesEarned,
            "early bird" => 0,
            "dedicated learner" => 0,
            _ => 0
        };
    }

    private async Task<List<CommunityBadge>> GetUserBadgesAsync(string userId)
    {
        try
        {
            var res = await _supabase.From<CommunityBadge>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Order("id", Postgrest.Constants.Ordering.Ascending)
                .Get();
            return res.Models;
        }
        catch (Exception ex) when (MissingRelation(ex, "community_badges"))
        {
            return new List<CommunityBadge>();
        }
    }

    private async Task<List<RewardAchievementCatalog>> GetAchievementCatalogAsync()
    {
        try
        {
            var res = await _supabase.From<RewardAchievementCatalog>()
                .Order("achievement_id", Postgrest.Constants.Ordering.Ascending)
                .Get();
            return res.Models;
        }
        catch (Exception ex) when (MissingRelation(ex, "reward_achievement_catalog"))
        {
            return new List<RewardAchievementCatalog>();
        }
    }

    private async Task<List<LearningProgress>> GetLearningProgressRowsAsync(string userId)
    {
        try
        {
            var res = await _supabase.From<LearningProgress>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();
            return res.Models;
        }
        catch
        {
            return new List<LearningProgress>();
        }
    }

    private async Task<int> GetTotalModulesCountAsync()
    {
        try
        {
            var res = await _supabase.From<LearningModule>().Get();
            return res.Models.Count;
        }
        catch
        {
            return 1;
        }
    }

    private async Task<List<ForumThread>> GetAllThreadsAsync()
    {
        try
        {
            var res = await _supabase.From<ForumThread>().Get();
            return res.Models;
        }
        catch
        {
            return new List<ForumThread>();
        }
    }

    private async Task<List<ForumReply>> GetAllRepliesAsync()
    {
        try
        {
            var res = await _supabase.From<ForumReply>().Get();
            return res.Models;
        }
        catch
        {
            return new List<ForumReply>();
        }
    }

    private async Task<List<CommunityResource>> GetAllResourcesAsync()
    {
        try
        {
            var res = await _supabase.From<CommunityResource>().Get();
            return res.Models;
        }
        catch
        {
            return new List<CommunityResource>();
        }
    }

    private async Task<int> GetCommunityBadgesEarnedCountAsync(string userId)
    {
        try
        {
            var discussionParticipation = await GetUserDiscussionParticipationCountAsync(userId);
            var rsvps = await GetUserEventRsvpCountAsync(userId);
            var downloads = await GetUserResourceDownloadCountAsync(userId);
            var uploads = await GetUserResourceUploadCountAsync(userId);

            var catalogRes = await _supabase.From<CommunityBadgeCatalog>()
                .Order("badge_id", Postgrest.Constants.Ordering.Ascending)
                .Get();

            var communityCatalog = catalogRes.Models
                .Where(IsCommunityBadgeCatalogItem)
                .ToList();

            if (communityCatalog.Count == 0)
            {
                var res = await _supabase.From<CommunityBadge>()
                    .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                    .Get();
                return res.Models.Count(b => string.Equals(b.Status, "earned", StringComparison.OrdinalIgnoreCase));
            }

            var earned = 0;
            foreach (var badge in communityCatalog)
            {
                var target =
                    ExtractFirstPositiveInt(badge.Requirements) ??
                    ExtractFirstPositiveInt(badge.Description) ??
                    ExtractFirstPositiveInt(badge.Name) ??
                    1;

                if (target <= 0) target = 1;

                var metric = GetCommunityBadgeMetricFromText(
                    $"{badge.Category} {badge.Name} {badge.Description}",
                    discussionParticipation,
                    rsvps,
                    downloads,
                    uploads);
                if (metric >= target) earned++;
            }

            return earned;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<List<RewardHistoryItem>> GetHistorySafeAsync(string userId)
    {
        try
        {
            return await GetHistoryRowsAsync(userId);
        }
        catch
        {
            return new List<RewardHistoryItem>();
        }
    }

    private async Task<int> GetPointsInternalAsync(string userId)
    {
        try
        {
            var profile = await GetOrCreateRewardProfileAsync(userId);
            return profile.Points;
        }
        catch (Exception ex) when (MissingRelation(ex, "reward_profiles"))
        {
            var profile = await GetOrCreateCommunityProfileAsync(userId);
            return profile.ReputationPoints;
        }
    }

    private async Task ChangePointsInternalAsync(string userId, int delta)
    {
        try
        {
            var profile = await GetOrCreateRewardProfileAsync(userId);
            profile.Points = Math.Max(0, profile.Points + delta);
            profile.UpdatedAt = DateTime.UtcNow;
            await _supabase.From<RewardProfile>().Update(profile);
        }
        catch (Exception ex) when (MissingRelation(ex, "reward_profiles"))
        {
            var profile = await GetOrCreateCommunityProfileAsync(userId);
            profile.ReputationPoints = Math.Max(0, profile.ReputationPoints + delta);
            await _supabase.From<CommunityProfile>().Update(profile);
        }
    }

    private async Task<RewardProfile> GetOrCreateRewardProfileAsync(string userId)
    {
        var res = await _supabase.From<RewardProfile>()
            .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
            .Get();

        var profile = res.Models.FirstOrDefault();
        if (profile != null) return profile;

        var created = new RewardProfile
        {
            UserId = userId,
            Points = WelcomeBonus,
            UpdatedAt = DateTime.UtcNow
        };

        var insert = await _supabase.From<RewardProfile>().Insert(created);
        return insert.Models.FirstOrDefault() ?? created;
    }

    private async Task<CommunityProfile> GetOrCreateCommunityProfileAsync(string userId)
    {
        var res = await _supabase.From<CommunityProfile>()
            .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
            .Get();

        var profile = res.Models.FirstOrDefault();
        if (profile != null) return profile;

        var created = new CommunityProfile
        {
            UserId = userId,
            ReputationPoints = WelcomeBonus,
            DiscussionsCount = 0,
            EventsRsvpedCount = 0,
            ResourcesDownloadedCount = 0,
            BadgesEarnedCount = 0
        };

        var insert = await _supabase.From<CommunityProfile>().Insert(created);
        return insert.Models.FirstOrDefault() ?? created;
    }

    private async Task AddHistorySafeAsync(string userId, string activity, int points)
    {
        try
        {
            var history = new RewardHistoryItem
            {
                UserId = userId,
                Activity = activity,
                Points = points,
                Date = DateTime.UtcNow
            };

            await _supabase.From<RewardHistoryItem>().Insert(history);
        }
        catch (Exception ex) when (MissingRelation(ex, "reward_history"))
        {
            // No-op when reward history table is not present in this database.
        }
    }

    private async Task<List<RewardHistoryItem>> GetHistoryRowsAsync(string userId)
    {
        var res = await _supabase.From<RewardHistoryItem>()
            .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
            .Order("created_at", Postgrest.Constants.Ordering.Descending)
            .Get();
        return res.Models;
    }

    public async Task EnsureSignupBonusAsync(string userId)
    {
        try
        {
            await GetOrCreateRewardProfileAsync(userId);

            var signupBonus = await _supabase.From<RewardHistoryItem>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Filter("activity", Postgrest.Constants.Operator.Equals, "Sign up bonus")
                .Filter("points_delta", Postgrest.Constants.Operator.Equals, WelcomeBonus)
                .Get();

            if (!signupBonus.Models.Any())
            {
                await AddHistorySafeAsync(userId, "Sign up bonus", WelcomeBonus);
            }
        }
        catch (Exception ex) when (MissingRelation(ex, "reward_profiles"))
        {
            var profile = await GetOrCreateCommunityProfileAsync(userId);
            if (profile.ReputationPoints <= 0)
            {
                profile.ReputationPoints = WelcomeBonus;
                await _supabase.From<CommunityProfile>().Update(profile);
            }
        }
        catch (Exception ex) when (MissingRelation(ex, "reward_history"))
        {
            // History table missing in this environment, keep silent.
        }
    }

    private static bool MissingRelation(Exception ex, string relationName)
    {
        return ex.Message.Contains(relationName, StringComparison.OrdinalIgnoreCase)
            && (ex.Message.Contains("PGRST205", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("could not find the table", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("relation", StringComparison.OrdinalIgnoreCase));
    }

    private static int? ExtractFirstPositiveInt(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var digits = new List<char>();
        foreach (var ch in input)
        {
            if (char.IsDigit(ch))
            {
                digits.Add(ch);
                continue;
            }

            if (digits.Count > 0)
                break;
        }

        if (digits.Count == 0)
            return null;

        return int.TryParse(new string(digits.ToArray()), out var parsed) && parsed > 0
            ? parsed
            : null;
    }

    private static bool IsCommunityBadgeCatalogItem(CommunityBadgeCatalog badge)
    {
        var text = $"{badge.Category} {badge.Name} {badge.Description} {badge.Requirements}".ToLowerInvariant();
        return text.Contains("thread")
            || text.Contains("discussion")
            || text.Contains("reply")
            || text.Contains("event")
            || text.Contains("rsvp")
            || text.Contains("resource")
            || text.Contains("upload")
            || text.Contains("download");
    }

    private static int GetCommunityBadgeMetricFromText(
        string text,
        int discussionParticipation,
        int rsvps,
        int downloads,
        int uploads)
    {
        var haystack = text.ToLowerInvariant();
        if (haystack.Contains("event") || haystack.Contains("rsvp"))
            return Math.Max(0, rsvps);
        if (haystack.Contains("upload"))
            return Math.Max(0, uploads);
        if (haystack.Contains("download") || haystack.Contains("resource"))
            return Math.Max(0, downloads);
        if (haystack.Contains("thread") || haystack.Contains("discussion") || haystack.Contains("reply"))
            return Math.Max(0, discussionParticipation);
        return 0;
    }

    private async Task<int> GetUserEventRsvpCountAsync(string userId)
    {
        try
        {
            var rsvps = await _supabase.From<CommunityEventRsvp>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();
            return rsvps.Models.Count;
        }
        catch
        {
            var profile = await GetOrCreateCommunityProfileAsync(userId);
            return Math.Max(0, profile.EventsRsvpedCount);
        }
    }

    private async Task<int> GetUserResourceDownloadCountAsync(string userId)
    {
        try
        {
            var downloads = await _supabase.From<CommunityResourceDownload>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();
            return downloads.Models.Count;
        }
        catch
        {
            var profile = await GetOrCreateCommunityProfileAsync(userId);
            return Math.Max(0, profile.ResourcesDownloadedCount);
        }
    }

    private async Task<int> GetUserResourceUploadCountAsync(string userId)
    {
        try
        {
            var uploads = await _supabase.From<CommunityResource>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();
            return uploads.Models.Count;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<int> GetUserDiscussionParticipationCountAsync(string userId)
    {
        try
        {
            var threadRes = await _supabase.From<ForumThread>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();
            var replyRes = await _supabase.From<ForumReply>()
                .Filter("user_id", Postgrest.Constants.Operator.Equals, userId)
                .Get();

            var set = new HashSet<int>(threadRes.Models.Select(t => t.Id));
            foreach (var threadId in replyRes.Models.Select(r => r.ThreadId))
                set.Add(threadId);
            return set.Count;
        }
        catch
        {
            var profile = await GetOrCreateCommunityProfileAsync(userId);
            return Math.Max(0, profile.DiscussionsCount);
        }
    }

    private static string ToBootstrapIcon(string iconName)
    {
        return (iconName ?? "").Trim().ToLowerInvariant() switch
        {
            "trophy" => "bi-trophy",
            "book" => "bi-book",
            "graduation-cap" => "bi-mortarboard",
            "crown" => "bi-crown",
            "star" => "bi-star",
            "message-circle" => "bi-chat-dots",
            "megaphone" => "bi-megaphone",
            "trending-up" => "bi-graph-up-arrow",
            "users" => "bi-people",
            "zap" => "bi-lightning-charge",
            "moon" => "bi-moon",
            "target" => "bi-bullseye",
            "message-square" => "bi-chat-square-dots",
            "hand-helping" => "bi-hand-index-thumb",
            "award" => "bi-award",
            "medal" => "bi-award",
            "gem" => "bi-gem",
            "infinity" => "bi-infinity",
            _ => "bi-award"
        };
    }
}
