using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("reward_profiles")]
public class RewardProfile : BaseModel
{
    [Key]
    [PrimaryKey("user_id", false)]
    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Column("points")]
    public int Points { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
