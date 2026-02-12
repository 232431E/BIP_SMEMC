using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;
using Newtonsoft.Json;

namespace BIP_SMEMC.Models;

[Table("community_threads")]
public class ForumThread : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Required, MaxLength(60)]
    [Column("category")]
    public string Category { get; set; } = "General";

    [Required, MaxLength(140)]
    [Column("title")]
    public string Title { get; set; } = "";

    [Required, MaxLength(2000)]
    [Column("content")]
    public string Content { get; set; } = "";

    [Required, MaxLength(40)]
    [Column("author")]
    public string Author { get; set; } = "Anonymous";

    [Column("author_reputation")]
    public int AuthorReputation { get; set; }

    [MaxLength(200)]
    [Column("excerpt")]
    public string Excerpt { get; set; } = "";

    [Column("tags")]
    public string TagsRaw { get; set; } = "";

    [JsonIgnore]
    public List<string> Tags { get; set; } = new();

    [Column("upvotes")]
    public int Upvotes { get; set; }

    [Column("view_count")]
    public int ViewCount { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public List<ForumReply> Replies { get; set; } = new();

    [JsonIgnore]
    public int RepliesCount { get; set; }
}
