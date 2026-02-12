using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("community_threads")]
public class CommunityThreadUpdate : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("upvotes")]
    public int? Upvotes { get; set; }

    [Column("view_count")]
    public int? ViewCount { get; set; }
}
