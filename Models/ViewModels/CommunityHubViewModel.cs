namespace BIP_SMEMC.Models.ViewModels;

public class CommunityHubViewModel
{
    public List<ForumThread> Threads { get; set; } = new();
    public CommunityProfile? Profile { get; set; }
    public List<CommunityEvent> Events { get; set; } = new();
    public List<CommunityResource> Resources { get; set; } = new();
    public HashSet<int> DownloadedResourceIds { get; set; } = new();
    public List<CommunityBadge> BadgesEarned { get; set; } = new();
    public List<CommunityBadge> BadgesInProgress { get; set; } = new();
}
