using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("community_thread_votes")]
public class CommunityThreadVote : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("thread_id")]
    public int ThreadId { get; set; }

    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
