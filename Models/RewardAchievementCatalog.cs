using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("reward_achievement_catalog")]
public class RewardAchievementCatalog : BaseModel
{
    [Key]
    [PrimaryKey("achievement_id", false)]
    public string AchievementId { get; set; } = "";

    [Required, Column("name")]
    public string Name { get; set; } = "";

    [Required, Column("description")]
    public string Description { get; set; } = "";

    [Column("points")]
    public int Points { get; set; }

    [Column("unlock_criteria")]
    public string UnlockCriteria { get; set; } = "";

    [Column("icon_name")]
    public string IconName { get; set; } = "";

    [Column("category")]
    public string Category { get; set; } = "";
}
