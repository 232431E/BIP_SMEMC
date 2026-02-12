using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("learning_progress")]
public class LearningProgress : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Required, MaxLength(64)]
    [Column("user_id")]
    public string UserId { get; set; } = "admin";

    [Required]
    [Column("topic_id")]
    public int TopicId { get; set; }

    [Required]
    [Column("difficulty")]
    public LearningDifficulty Difficulty { get; set; }

    [Column("best_score")]
    public int BestScore { get; set; } = 0;     // 0-100

    [Column("passed")]
    public bool Passed { get; set; } = false;

    [Column("points_awarded")]
    public int PointsAwarded { get; set; } = 0;

    [Column("updated_at_utc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
