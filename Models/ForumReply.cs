using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("community_thread_replies")]
public class ForumReply : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("thread_id")]
    public int ThreadId { get; set; }

    [Required, MaxLength(40)]
    [Column("author")]
    public string Author { get; set; } = "Admin";

    [Required, MaxLength(1000)]
    [Column("message")]
    public string Message { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
