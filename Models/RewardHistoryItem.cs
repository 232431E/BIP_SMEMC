using System;
using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("reward_history")]
public class RewardHistoryItem : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Column("activity")]
    public string Activity { get; set; } = "";

    [Column("points_delta")]
    public int Points { get; set; }

    [Column("created_at")]
    public DateTime Date { get; set; } = DateTime.UtcNow;
}
