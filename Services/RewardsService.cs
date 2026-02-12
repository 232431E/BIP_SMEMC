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
    private const int WelcomeBonus = 1200;
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
        return await GetPointsInternalAsync(userId);
    }

    public async Task AddPointsAsync(string userId, int amount, string activity)
    {
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
            await ReconcileHistoryAsync(userId);
            return await GetHistoryRowsAsync(userId);
        }
        catch (Exception ex) when (MissingRelation(ex, "reward_history"))
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

    // Keeps reward_history in sync with stored point balance so UI cards and history match.
    private async Task ReconcileHistoryAsync(string userId)
    {
        var history = await GetHistoryRowsAsync(userId);
        var historyNet = history.Sum(h => h.Points);
        var currentPoints = await GetPointsInternalAsync(userId);
        var expectedNet = currentPoints - WelcomeBonus;
        var adjustment = expectedNet - historyNet;

        if (adjustment == 0) return;

        var entry = new RewardHistoryItem
        {
            UserId = userId,
            Activity = "Balance sync adjustment",
            Points = adjustment,
            Date = DateTime.UtcNow
        };

        await _supabase.From<RewardHistoryItem>().Insert(entry);
    }

    private static bool MissingRelation(Exception ex, string relationName)
    {
        return ex.Message.Contains(relationName, StringComparison.OrdinalIgnoreCase)
            && (ex.Message.Contains("PGRST205", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("could not find the table", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("relation", StringComparison.OrdinalIgnoreCase));
    }
}
