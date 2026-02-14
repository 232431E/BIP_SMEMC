using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace BIP_SMEMC.Models;

[Table("community_resource_downloads")]
public class CommunityResourceDownload : BaseModel
{
    [Key]
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("resource_id")]
    public int ResourceId { get; set; }

    [Column("user_id")]
    public string UserId { get; set; } = "";

    [Column("downloaded_at")]
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
}
