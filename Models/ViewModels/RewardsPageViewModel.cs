using BIP_SMEMC.Models;

namespace BIP_SMEMC.Models.ViewModels;

public class RewardsPageViewModel
{
    public int CurrentPoints { get; set; }
    public int TotalEarned { get; set; }
    public int TotalRedeemed { get; set; }
    public int AchievementsUnlocked { get; set; }
    public int TotalAchievements { get; set; }
    public string ActiveTab { get; set; } = "vouchers";
    public List<Reward> Rewards { get; set; } = new();
    public List<RewardHistoryItem> History { get; set; } = new();
}
