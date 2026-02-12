using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("community_badges")]
public class CommunityBadge : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Required, Column("user_id")]
    public string UserId { get; set; } = "";

    [Required, Column("title")]
    public string Title { get; set; } = "";

    [Required, Column("description")]
    public string Description { get; set; } = "";

    [Required, Column("status")]
    public string Status { get; set; } = "";

    [Column("points")]
    public int Points { get; set; }

    [Column("earned_at")]
    public DateTime? EarnedAt { get; set; }

    [Column("progress_current")]
    public int? ProgressCurrent { get; set; }

    [Column("progress_target")]
    public int? ProgressTarget { get; set; }

    [Column("progress_percent")]
    public int? ProgressPercent { get; set; }

    [Column("icon")]
    public string Icon { get; set; } = "bi-award";

    [Column("icon_color")]
    public string IconColor { get; set; } = "text-warning";
}
