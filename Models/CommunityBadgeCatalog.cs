using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("community_badge_catalog")]
public class CommunityBadgeCatalog : BaseModel
{
    [Key]
    [PrimaryKey("badge_id", false)]
    public string BadgeId { get; set; } = "";

    [Required, Column("name")]
    public string Name { get; set; } = "";

    [Required, Column("description")]
    public string Description { get; set; } = "";

    [Column("icon_name")]
    public string IconName { get; set; } = "";

    [Column("requirements")]
    public string Requirements { get; set; } = "";

    [Column("category")]
    public string Category { get; set; } = "";

    [Column("tier")]
    public string Tier { get; set; } = "";

    [Column("points_reward")]
    public int PointsReward { get; set; }
}
