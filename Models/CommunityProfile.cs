using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("community_profiles")]
public class CommunityProfile : BaseModel
{
    [Key]
    [PrimaryKey("user_id", false)]
    public string UserId { get; set; } = "";

    [Column("reputation_points")]
    public int ReputationPoints { get; set; }

    [Column("discussions_count")]
    public int DiscussionsCount { get; set; }

    [Column("events_rsvped_count")]
    public int EventsRsvpedCount { get; set; }

    [Column("resources_downloaded_count")]
    public int ResourcesDownloadedCount { get; set; }

    [Column("badges_earned_count")]
    public int BadgesEarnedCount { get; set; }
}
